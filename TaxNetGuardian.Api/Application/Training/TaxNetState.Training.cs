using System.Security.Cryptography;
using System.Text;

namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    // ---- Knowledge-distillation training state (persisted in the snapshot) ----
    public List<TrainingExample> TrainingExamples { get; } = [];
    public List<ModelTrainingRun> ModelRuns { get; } = [];

    // Inference routing mode: BigLlm (always frontier), CustomModel (always local), Auto (local when
    // confident, else frontier). Controlled from the Model Training page's two-button switch.
    public string InferenceMode { get; private set; } = "BigLlm";

    private readonly TaxNetCustomModel _customModel = new();
    private readonly object _trainingLock = new();
    private volatile bool _isTraining;
    private int _examplesSinceSave;

    private const int MaxTrainingExamples = 1500;
    private const int MaxStoredPromptChars = 4000;
    private const int MaxStoredResponseChars = 8000;
    private const double AutoConfidenceThreshold = 0.55;

    public bool IsTrainingInProgress => _isTraining;
    public int CustomModelVersion => _customModel.Version;
    public bool CustomModelReady => _customModel.IsTrained;

    // True when inference should route to the fine-tuned local LLM (Ollama) rather than the
    // retrieval model or the frontier provider.
    public bool RouteToLocalLlm => InferenceMode.Equals("LocalLlm", StringComparison.OrdinalIgnoreCase);

    // Only genuine frontier providers are valid teachers for distillation — never record the
    // student's own output (the local LLM or the retrieval model) as new training data.
    public static bool IsFrontierTeacher(string? provider)
    {
        var p = (provider ?? "").Trim().ToLowerInvariant();
        return p is "claude" or "openai" or "gemini" or "deepseek";
    }

    public int PendingTrainingExamples
    {
        get
        {
            lock (_trainingLock)
            {
                return TrainingExamples.Count(e => e.TrainedIntoVersion == 0);
            }
        }
    }

    /// <summary>
    /// Captures a frontier-LLM (prompt -> response) pair as a teacher example. Deduplicates by a
    /// content hash, caps corpus size (FIFO), and throttles snapshot writes.
    /// </summary>
    public void RecordTrainingExample(string taskType, string prompt, string response, string teacherProvider)
    {
        if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(response))
        {
            return;
        }

        var storedPrompt = Truncate(prompt, MaxStoredPromptChars);
        var storedResponse = Truncate(response, MaxStoredResponseChars);
        var hash = ComputeHash(taskType + "\u0001" + storedPrompt);

        lock (_trainingLock)
        {
            if (TrainingExamples.Any(e => e.PromptHash == hash))
            {
                return; // already captured this teacher signal
            }

            var example = new TrainingExample(
                $"tex-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{TrainingExamples.Count + 1:0000}",
                string.IsNullOrWhiteSpace(taskType) ? "general" : taskType.Trim(),
                storedPrompt,
                storedResponse,
                string.IsNullOrWhiteSpace(teacherProvider) ? "frontier" : teacherProvider,
                hash,
                EstimateTokens(storedPrompt),
                EstimateTokens(storedResponse),
                0,
                DateTimeOffset.UtcNow);

            TrainingExamples.Add(example);
            while (TrainingExamples.Count > MaxTrainingExamples)
            {
                TrainingExamples.RemoveAt(0);
            }

            _examplesSinceSave++;
        }

        // Throttle persistence: every 5 captures (cheap durability without IO storms).
        if (Interlocked.CompareExchange(ref _examplesSinceSave, _examplesSinceSave, 0) >= 5)
        {
            _examplesSinceSave = 0;
            SaveSnapshot();
        }
    }

    /// <summary>
    /// Trains a new model version on all collected teacher examples, evaluates it on a hold-out
    /// split, records the run, and (on success) activates the new model in-process.
    /// </summary>
    public ModelTrainingRun TrainCustomModel(string triggeredBy)
    {
        if (_isTraining)
        {
            return ModelRuns.FirstOrDefault() ?? FailedRun(triggeredBy, "A training run is already in progress.");
        }

        List<TrainingExample> snapshot;
        int version;
        lock (_trainingLock)
        {
            _isTraining = true;
            snapshot = TrainingExamples.ToList();
            version = (ModelRuns.Count == 0 ? 0 : ModelRuns.Max(r => r.Version)) + 1;
        }

        var runId = $"run-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-v{version}";
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (snapshot.Count < 3)
            {
                throw new InvalidOperationException($"Need at least 3 teacher examples to train (have {snapshot.Count}). Use the system through the frontier model to collect more.");
            }

            ModelEvaluationMetrics metrics;
            int vocabSize;
            int trainCount;
            int validationCount;
            lock (_trainingLock)
            {
                metrics = _customModel.Train(snapshot, version);
                vocabSize = _customModel.VocabularySize;
                validationCount = snapshot.Count < 8 ? 0 : Math.Max(1, (int)(snapshot.Count * 0.18));
                trainCount = snapshot.Count - validationCount;

                // Mark these examples as folded into this version.
                for (var i = 0; i < TrainingExamples.Count; i++)
                {
                    if (TrainingExamples[i].TrainedIntoVersion == 0)
                    {
                        TrainingExamples[i] = TrainingExamples[i] with { TrainedIntoVersion = version };
                    }
                }
            }

            stopwatch.Stop();
            var run = new ModelTrainingRun(
                runId, version, "Succeeded", triggeredBy, startedAt, DateTimeOffset.UtcNow,
                stopwatch.Elapsed.TotalMilliseconds, snapshot.Count, trainCount, validationCount,
                vocabSize, metrics, $"Trained v{version} on {snapshot.Count} examples ({vocabSize} terms).");

            lock (_trainingLock)
            {
                ModelRuns.Insert(0, run);
                while (ModelRuns.Count > 50)
                {
                    ModelRuns.RemoveAt(ModelRuns.Count - 1);
                }
            }

            AddAuditEvent(triggeredBy, "taxnet-model-admin", "CustomModelTrained", runId, "Succeeded", new Dictionary<string, object>
            {
                ["version"] = version,
                ["examples"] = snapshot.Count,
                ["validationSimilarity"] = Math.Round(metrics.ValidationSimilarity, 4),
                ["vocabulary"] = vocabSize
            });
            SaveSnapshot();
            return run;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var run = new ModelTrainingRun(
                runId, version, "Failed", triggeredBy, startedAt, DateTimeOffset.UtcNow,
                stopwatch.Elapsed.TotalMilliseconds, snapshot.Count, 0, 0, 0, null, ex.Message);
            lock (_trainingLock)
            {
                ModelRuns.Insert(0, run);
            }

            AddAuditEvent(triggeredBy, "taxnet-model-admin", "CustomModelTrained", runId, "Failed", new Dictionary<string, object>
            {
                ["error"] = ex.Message
            });
            return run;
        }
        finally
        {
            _isTraining = false;
        }
    }

    /// <summary>
    /// Routing decision + local prediction. Returns whether the custom model should serve this
    /// request and, if so, its output and confidence. Honours the active InferenceMode.
    /// </summary>
    public (bool Serve, string Output, double Confidence, int Version) TryCustomInference(string taskType, string prompt)
    {
        if (InferenceMode.Equals("BigLlm", StringComparison.OrdinalIgnoreCase) ||
            InferenceMode.Equals("LocalLlm", StringComparison.OrdinalIgnoreCase) ||
            !_customModel.IsTrained)
        {
            return (false, "", 0, 0);
        }

        (bool ok, string response, double confidence) prediction;
        lock (_trainingLock)
        {
            prediction = _customModel.Predict(taskType, prompt);
        }

        if (!prediction.ok)
        {
            return (false, "", 0, _customModel.Version);
        }

        // CustomModel mode always serves locally; Auto serves only when confident.
        var serve = InferenceMode.Equals("CustomModel", StringComparison.OrdinalIgnoreCase)
            || (InferenceMode.Equals("Auto", StringComparison.OrdinalIgnoreCase) && prediction.confidence >= AutoConfidenceThreshold);

        return serve
            ? (true, prediction.response, prediction.confidence, _customModel.Version)
            : (false, "", prediction.confidence, _customModel.Version);
    }

    public string SetInferenceMode(string mode, string actor)
    {
        var normalized = mode?.Trim().ToLowerInvariant() switch
        {
            "bigllm" or "big" or "frontier" or "llm" => "BigLlm",
            "custommodel" or "custom" or "retrieval" or "distill" => "CustomModel",
            "localllm" or "local" or "ollama" or "finetuned" or "fine-tuned" => "LocalLlm",
            "auto" or "hybrid" => "Auto",
            _ => InferenceMode
        };

        if (!normalized.Equals(InferenceMode, StringComparison.Ordinal))
        {
            InferenceMode = normalized;
            AddAuditEvent(actor, "taxnet-model-admin", "InferenceModeChanged", "custom-model", "Succeeded", new Dictionary<string, object>
            {
                ["mode"] = normalized
            });
            SaveSnapshot();
        }

        return InferenceMode;
    }

    public object GetCustomModelStatus()
    {
        lock (_trainingLock)
        {
            var latest = ModelRuns.FirstOrDefault(r => r.Status == "Succeeded");
            var teacherProviders = TrainingExamples
                .GroupBy(e => e.TeacherProvider)
                .ToDictionary(g => g.Key, g => g.Count());
            var byTask = TrainingExamples
                .GroupBy(e => e.TaskType)
                .ToDictionary(g => g.Key, g => g.Count());

            return new
            {
                inferenceMode = InferenceMode,
                ready = _customModel.IsTrained,
                training = _isTraining,
                activeVersion = _customModel.Version,
                vocabularySize = _customModel.VocabularySize,
                indexedExamples = _customModel.ExampleCount,
                totalExamples = TrainingExamples.Count,
                untrainedExamples = TrainingExamples.Count(e => e.TrainedIntoVersion == 0),
                examplesByTeacher = teacherProviders,
                examplesByTask = byTask,
                autoConfidenceThreshold = AutoConfidenceThreshold,
                latestRun = latest,
                metrics = latest?.Metrics,
                runs = ModelRuns.Take(15).ToList(),
                totalTrainingTokens = TrainingExamples.Sum(e => (long)e.PromptTokens + e.ResponseTokens),
                localLlm = new
                {
                    enabled = IsTruthy(Environment.GetEnvironmentVariable("OLLAMA_ENABLED")),
                    baseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434/v1",
                    model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "taxnet-guardian"
                }
            };
        }
    }

    public object GetTrainingExamples(int limit)
    {
        lock (_trainingLock)
        {
            var items = TrainingExamples
                .AsEnumerable()
                .Reverse()
                .Take(Math.Clamp(limit, 1, 100))
                .Select(e => new
                {
                    e.Id,
                    e.TaskType,
                    e.TeacherProvider,
                    e.TrainedIntoVersion,
                    e.PromptTokens,
                    e.ResponseTokens,
                    e.CreatedAtUtc,
                    promptPreview = Truncate(e.Prompt, 220),
                    responsePreview = Truncate(e.Response, 320)
                })
                .ToList();
            return new { total = TrainingExamples.Count, items };
        }
    }

    public (bool Ok, string Response, double Confidence, int Version) TestCustomModel(string taskType, string prompt)
    {
        lock (_trainingLock)
        {
            var prediction = _customModel.Predict(taskType, prompt);
            return (prediction.Ok, prediction.Response, prediction.Confidence, _customModel.Version);
        }
    }

    /// <summary>
    /// Exports the captured teacher corpus as JSONL for offline fine-tuning. Supports the OpenAI
    /// chat format (messages[]) used by most LoRA/SFT toolchains (Unsloth, Axolotl, OpenAI), and an
    /// instruction format (prompt/response) for alternative pipelines.
    /// </summary>
    public string ExportTrainingJsonl(string format)
    {
        var systemPrompt = "You are TaxNet Guardian, a senior Pakistani tax-compliance intelligence analyst. "
            + "Produce thorough, evidence-grounded, professional analysis. Risk indicators are not proof; "
            + "always preserve human-review safeguards.";

        List<TrainingExample> items;
        lock (_trainingLock)
        {
            items = TrainingExamples.ToList();
        }

        var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = false };
        var lines = new List<string>(items.Count);
        var useInstruction = (format ?? "").Trim().Equals("instruction", StringComparison.OrdinalIgnoreCase);

        foreach (var example in items)
        {
            object record = useInstruction
                ? new
                {
                    instruction = systemPrompt,
                    input = example.Prompt,
                    output = example.Response,
                    task = example.TaskType
                }
                : new
                {
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = example.Prompt },
                        new { role = "assistant", content = example.Response }
                    }
                };
            lines.Add(System.Text.Json.JsonSerializer.Serialize(record, jsonOptions));
        }

        return string.Join('\n', lines);
    }

    public int TrainingExampleCount
    {
        get
        {
            lock (_trainingLock)
            {
                return TrainingExamples.Count;
            }
        }
    }

    // Rebuild the in-memory model from persisted examples after a snapshot load, so the custom
    // model is immediately available post-restart without waiting for the worker.
    public void RehydrateCustomModel()
    {
        try
        {
            lock (_trainingLock)
            {
                var lastSucceeded = ModelRuns.FirstOrDefault(r => r.Status == "Succeeded");
                if (lastSucceeded is not null && TrainingExamples.Count >= 3)
                {
                    _customModel.Train(TrainingExamples.ToList(), lastSucceeded.Version);
                }
            }
        }
        catch
        {
            // Non-fatal: model simply stays untrained until the next run.
        }
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    private static bool IsTruthy(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..32];
    }

    private ModelTrainingRun FailedRun(string triggeredBy, string message)
        => new($"run-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}", _customModel.Version, "Failed", triggeredBy,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, TrainingExamples.Count, 0, 0, 0, null, message);
}
