namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    private IEmbeddingProvider? _embeddingProvider;
    private IVectorStore? _vectorStore;

    /// <summary>
    /// Wires the optional embedding/vector-store services into the singleton state once at startup
    /// (Req 5). When an embedding provider is configured, existing indexed chunks are back-filled
    /// into the vector store so the embedding retrieval path can serve pre-seeded content too.
    /// Safe to call when services are absent; retrieval/indexing then use the lexical fallback.
    /// </summary>
    public void ConfigureRagServices(IEmbeddingProvider embeddingProvider, IVectorStore vectorStore)
    {
        lock (_lock)
        {
            _embeddingProvider = embeddingProvider;
            _vectorStore = vectorStore;

            if (embeddingProvider is { IsConfigured: true } && RagChunks.Count > 0)
            {
                var entries = RagChunks
                    .Select(chunk => new VectorEntry(
                        chunk.Id,
                        chunk.DocumentId,
                        embeddingProvider.EmbedAsync(chunk.Text, CancellationToken.None).GetAwaiter().GetResult(),
                        chunk.Citation))
                    .ToList();
                vectorStore.UpsertAsync(entries, CancellationToken.None).GetAwaiter().GetResult();
            }
        }
    }

    public ImportJob FeedRagDocument(RagFeedRequest request)
    {
        lock (_lock)
        {
            var id = $"rag-{RagDocuments.Count + 1:000}";
            var sourceType = string.IsNullOrWhiteSpace(request.SourceType) ? "PolicyDocument" : request.SourceType;
            var tags = request.Tags?.Count > 0 ? request.Tags : sourceType.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
            var document = new RagDocument(
                id,
                string.IsNullOrWhiteSpace(request.Title) ? $"Policy Document {RagDocuments.Count + 1}" : request.Title,
                sourceType,
                string.IsNullOrWhiteSpace(request.Url) ? $"sandbox://rag/{id}" : request.Url,
                Summarize(request.Content),
                DateTimeOffset.UtcNow,
                tags.ToArray());

            RagDocuments.Insert(0, document);
            IndexRagDocument(document, request.Content);
            StoreObject("taxnet-dev-raw-source-snapshots", $"rag-source/{id}.txt", "text/plain", request.Content);

            var job = new ImportJob(
                $"job-rag-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
                "RagIndex",
                "Succeeded",
                request.Title,
                RagChunks.Count(x => x.DocumentId == id),
                RagChunks.Count(x => x.DocumentId == id),
                0,
                DateTimeOffset.UtcNow.AddSeconds(-2),
                DateTimeOffset.UtcNow,
                [$"Indexed policy document '{document.Title}'.", "Generated lexical embeddings, chunk metadata, and citation records."]);

            ImportJobs.Insert(0, job);
            AddAuditEvent("RagPolicy.Worker", "taxnet-policy-analyst", "RagDocumentIndexed", document.Id, "Succeeded", new Dictionary<string, object>
            {
                ["title"] = document.Title,
                ["chunks"] = RagChunks.Count(x => x.DocumentId == id)
            });
            SeedWorkers();
            SaveSnapshot();
            return job;
        }
    }

    public RagQueryResult QueryRag(RagQueryRequest request, IEmbeddingProvider? embeddings = null, IVectorStore? vectorStore = null)
    {
        embeddings ??= _embeddingProvider;
        vectorStore ??= _vectorStore;

        var query = string.IsNullOrWhiteSpace(request.Query) ? "tax compliance human review evidence policy" : request.Query.Trim();
        var taskType = string.IsNullOrWhiteSpace(request.TaskType) ? "PolicyQuestion" : request.TaskType.Trim();
        var jurisdiction = string.IsNullOrWhiteSpace(request.Jurisdiction) ? "Pakistan" : request.Jurisdiction.Trim();
        var requestedTags = request.Tags ?? [];
        var tokens = Tokenize($"{query} {taskType} {jurisdiction} {string.Join(' ', requestedTags)}");
        var defaultTopK = _platformOptions.Rag.DefaultTopK <= 0 ? 5 : _platformOptions.Rag.DefaultTopK;
        var topK = Math.Clamp(request.TopK <= 0 ? defaultTopK : request.TopK, 1, 50);

        // Retrievable pool excludes raw PII / private-citizen records on every path (AC 9).
        var retrievable = RagChunks.Where(IsRetrievableChunk).ToArray();

        // Pure query relevance (drives honest confidence) is computed separately from the
        // quality/recency/source prior, which is only a small tie-breaker. This keeps unrelated
        // queries genuinely low-confidence instead of always returning high-confidence filler.
        var queryTokens = Tokenize(query).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var queryTokenCount = Math.Max(1, queryTokens.Length);

        var scoredWithMetrics = retrievable
            .Select(chunk =>
            {
                var matched = queryTokens.Count(t =>
                    chunk.Keywords.Contains(t, StringComparer.OrdinalIgnoreCase) ||
                    chunk.Text.Contains(t, StringComparison.OrdinalIgnoreCase));
                var relevance = decimal.Round((decimal)matched / queryTokenCount, 3); // 0..1, pure query match
                var tagBoost = requestedTags.Count == 0 ? 0 : chunk.Keywords.Count(keyword => requestedTags.Contains(keyword, StringComparer.OrdinalIgnoreCase));
                var sourceBoost = chunk.Citation.SourceType.Contains("Sop", StringComparison.OrdinalIgnoreCase) ? 0.3m : 0m;
                var recencyBoost = chunk.IndexedAtUtc >= DateTimeOffset.UtcNow.AddDays(-30) ? 0.15m : 0m;
                // Query relevance dominates the rank; quality/recency/source only break ties.
                var score = decimal.Round((relevance * 10m) + (tagBoost * 1.2m) + sourceBoost + recencyBoost + (chunk.QualityScore * 0.3m), 3);
                return new { Chunk = chunk, Score = score, Relevance = relevance, Matched = matched, TagBoost = tagBoost };
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Chunk.IndexedAtUtc)
            .ToArray();

        var retrievalPath = "deterministic_fallback";
        string? embeddingUnavailableNote = null;
        RagChunk[] scored;

        if (embeddings is { IsConfigured: true } && vectorStore is not null && TryEmbeddingRetrieval(
                $"{query} {taskType} {jurisdiction} {string.Join(' ', requestedTags)}",
                topK,
                embeddings,
                vectorStore,
                out var embeddingChunks))
        {
            scored = embeddingChunks;
            retrievalPath = "embedding";
        }
        else
        {
            if (embeddings is { IsConfigured: true })
            {
                // Embedding provider was configured but timed out or errored (AC 8).
                embeddingUnavailableNote = "Embedding path unavailable; used deterministic fallback retrieval.";
            }

            scored = scoredWithMetrics.Where(x => x.Relevance > 0m).Take(topK).Select(x => x.Chunk).ToArray();
            if (scored.Length == 0)
            {
                // No lexical query match — surface the strongest policy context as a weak fallback.
                scored = scoredWithMetrics.Take(Math.Min(topK, 3)).Select(x => x.Chunk).ToArray();
            }
        }

        var topRelevance = scoredWithMetrics.FirstOrDefault()?.Relevance ?? 0m;
        var relevantCount = scoredWithMetrics.Count(x => x.Relevance > 0m);

        var qualityChecks = new List<string>
        {
            "Query rewritten with task type, jurisdiction, and requested tags.",
            "Retrieved chunks include citation metadata (source document id and chunk id) and source timestamps.",
            "Context pack excludes raw PII and private citizen records.",
            $"Hybrid score considered query-term relevance, tag match, source type, recency, and chunk quality across {retrievable.Length} chunks.",
            scoredWithMetrics.Length > 0 ? $"Top relevance {topRelevance:P0}; matched {scoredWithMetrics[0].Matched}/{queryTokenCount} query terms; {relevantCount} chunk(s) matched the query." : "No lexical score available.",
            relevantCount == 0 && retrievalPath != "embedding" ? "No strong lexical match for this query; returning closest policy context as a weak fallback." : "At least one chunk matched the query.",
            $"Retrieval path: {retrievalPath}."
        };

        if (embeddingUnavailableNote is not null)
        {
            qualityChecks.Add(embeddingUnavailableNote);
        }

        decimal confidence;
        if (scored.Length == 0)
        {
            confidence = 0m;
        }
        else if (retrievalPath == "embedding")
        {
            confidence = 0.9m;
        }
        else if (topRelevance == 0m)
        {
            confidence = 0.12m; // weak fallback — no query terms matched
        }
        else
        {
            confidence = decimal.Round(Math.Min(0.97m, 0.45m + (topRelevance * 0.45m) + (Math.Min(relevantCount, 4) * 0.02m)), 2);
        }

        var ranked = scored
            .Select(c =>
            {
                var m = scoredWithMetrics.First(x => x.Chunk.Id == c.Id);
                return new RagRankedChunk(c.Id, c.DocumentId, c.Title, c.Citation.SourceType, m.Relevance, m.Score, m.Matched);
            })
            .ToArray();

        AddAuditEvent("RagPolicy.Api", "taxnet-policy-analyst", "RagQuery", taskType, "Succeeded", new Dictionary<string, object>
        {
            ["query"] = query,
            ["chunks"] = scored.Length,
            ["confidence"] = confidence,
            ["retrievalPath"] = retrievalPath
        });
        SaveSnapshot();

        return new RagQueryResult(
            query,
            $"{taskType}: {query} jurisdiction:{jurisdiction}",
            scored,
            scored.Select(x => x.Citation).DistinctBy(x => x.ChunkId).ToArray(),
            qualityChecks,
            confidence,
            retrievalPath,
            ranked,
            queryTokens);
    }

    /// <summary>
    /// Embeds the query under a configurable timeout (default 10s, AC 8), queries the vector store,
    /// and resolves matches back to indexed <see cref="RagChunk"/>s by chunk id (AC 2). Returns
    /// <c>false</c> on timeout/error so the caller falls back to deterministic lexical retrieval.
    /// PII / private-citizen chunks are filtered from the resolved set on this path too (AC 9).
    /// </summary>
    private bool TryEmbeddingRetrieval(string queryText, int topK, IEmbeddingProvider embeddings, IVectorStore vectorStore, out RagChunk[] chunks)
    {
        try
        {
            var timeoutSeconds = _platformOptions.Rag.EmbeddingTimeoutSeconds <= 0 ? 10 : _platformOptions.Rag.EmbeddingTimeoutSeconds;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var vector = embeddings.EmbedAsync(queryText, cts.Token).GetAwaiter().GetResult();
            var matches = vectorStore.QueryAsync(vector, topK, cts.Token).GetAwaiter().GetResult();

            // Resolve in ranked order; skip matches with no live chunk and exclude PII/private records.
            chunks = matches
                .Select(match => RagChunks.FirstOrDefault(chunk => chunk.Id.Equals(match.ChunkId, StringComparison.OrdinalIgnoreCase)))
                .Where(chunk => chunk is not null && IsRetrievableChunk(chunk))
                .Select(chunk => chunk!)
                .ToArray();
            return true;
        }
        catch
        {
            chunks = [];
            return false;
        }
    }

    /// <summary>
    /// Excludes raw PII and private-citizen records from RAG retrieval on every path (AC 9).
    /// Indexed chunks today are policy documents only; this guard keeps that invariant explicit.
    /// </summary>
    private static bool IsRetrievableChunk(RagChunk chunk)
    {
        var sourceType = chunk.Citation.SourceType ?? "";
        return !sourceType.Contains("PrivateCitizen", StringComparison.OrdinalIgnoreCase)
            && !sourceType.Contains("CitizenRecord", StringComparison.OrdinalIgnoreCase)
            && !sourceType.Contains("PII", StringComparison.OrdinalIgnoreCase);
    }

    public ModelInvocationResponse InvokeModelGateway(ModelInvocationRequest request, ModelGatewayClient? modelGatewayClient = null)
    {
        var taskType = string.IsNullOrWhiteSpace(request.TaskType) ? "PolicyQuestion" : request.TaskType.Trim();
        var safePrompt = RedactSensitivePrompt(request.Prompt);
        var citations = Array.Empty<PolicyCitation>();
        var contextChunks = Array.Empty<RagChunk>();
        string output;
        string deterministicOutput;

        if (!string.IsNullOrWhiteSpace(request.CaseId) && Cases.Any(x => x.Id.Equals(request.CaseId, StringComparison.OrdinalIgnoreCase)))
        {
            var explanation = BuildExplanation(request.CaseId);
            var rag = QueryRag(new RagQueryRequest(
                string.Join(' ', explanation.KeyReasons),
                taskType,
                "Pakistan",
                5,
                ["audit", "human-review", "citizen", "asset"]));

            citations = rag.Citations.ToArray();
            contextChunks = rag.Chunks.ToArray();
            deterministicOutput = taskType switch
            {
                "CitizenExplanation" => "Citizen-safe draft: records linked to this profile may need review. The citizen should verify ownership dates, filing status, and any outdated business or utility links through the correction workflow.",
                "ReportDraft" => BuildReportDraft(request.CaseId, explanation, rag),
                _ => $"{explanation.Summary} RAG confidence {rag.RetrievalConfidence:P0}. Key grounded reasons: {string.Join(" ", explanation.KeyReasons.Take(3))}"
            };
        }
        else
        {
            var rag = QueryRag(new RagQueryRequest(safePrompt, taskType, "Pakistan", 5, []));
            citations = rag.Citations.ToArray();
            contextChunks = rag.Chunks.ToArray();
            deterministicOutput = $"Model gateway deterministic response for {taskType}: {safePrompt}. Retrieved {rag.Chunks.Count} policy context chunk(s) with {rag.RetrievalConfidence:P0} confidence.";
        }

        // --- Inference routing ---
        // Retrieval distillation model (CustomModel / Auto modes) serves first when eligible.
        // In LocalLlm mode we skip retrieval and route to the fine-tuned local LLM instead.
        if (!RouteToLocalLlm)
        {
            var (serveCustom, customOutput, customConfidence, customVersion) = TryCustomInference(taskType, safePrompt);
            if (serveCustom)
            {
                var customPromptTokens = EstimateTokens(safePrompt);
                var customCompletionTokens = EstimateTokens(customOutput);
                var customResponse = new ModelInvocationResponse(
                    $"model-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{ModelInvocations.Count + 1:000}",
                    taskType,
                    $"taxnet-custom-v{customVersion}",
                    "custom-model-local",
                    customOutput,
                    false,
                    ["served by TaxNet custom model", $"retrieval confidence {customConfidence:P0}", "no external call", "human review warning"],
                    citations,
                    customPromptTokens,
                    customCompletionTokens,
                    0m,
                    DateTimeOffset.UtcNow);

                ModelInvocations.Insert(0, customResponse);
                AddAuditEvent("TaxNet.AI.CustomModel", "taxnet-model-admin", "ModelInvoked", taskType, "Succeeded", new Dictionary<string, object>
                {
                    ["provider"] = customResponse.SelectedProvider,
                    ["confidence"] = Math.Round(customConfidence, 4),
                    ["servedLocally"] = true
                });
                return customResponse;
            }
        }

        // LocalLlm mode routes to the fine-tuned local model (Ollama); otherwise honour the request.
        var routedProvider = RouteToLocalLlm ? "ollama" : request.PreferredProvider;
        var routedAllowExternal = RouteToLocalLlm || request.AllowExternalProvider;

        var providerResult = (modelGatewayClient ?? new ModelGatewayClient())
            .InvokeAsync(routedProvider, routedAllowExternal, taskType, safePrompt, contextChunks)
            .GetAwaiter()
            .GetResult();
        output = providerResult.UsedExternalProvider && !string.IsNullOrWhiteSpace(providerResult.Output)
            ? providerResult.Output
            : deterministicOutput + (string.IsNullOrWhiteSpace(providerResult.Error) ? "" : $" Provider fallback: {providerResult.Error}");

        // Capture the frontier model's teacher signal for knowledge distillation (never the
        // student's own output — only true frontier providers are valid teachers).
        if (providerResult.UsedExternalProvider && !string.IsNullOrWhiteSpace(providerResult.Output) && IsFrontierTeacher(providerResult.Provider))
        {
            RecordTrainingExample(taskType, safePrompt, providerResult.Output, providerResult.Provider);
        }

        var promptTokens = EstimateTokens(safePrompt);
        var completionTokens = EstimateTokens(output);
        var response = new ModelInvocationResponse(
            $"model-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{ModelInvocations.Count + 1:000}",
            taskType,
            providerResult.Provider,
            providerResult.Route,
            output,
            providerResult.UsedExternalProvider,
            ["PII redaction", "evidence ID validation", "citation validation", "no fraud-proven language", "human review warning"],
            citations,
            promptTokens,
            completionTokens,
            providerResult.UsedExternalProvider ? decimal.Round((promptTokens + completionTokens) * 0.000002m, 6) : 0m,
            DateTimeOffset.UtcNow);

        ModelInvocations.Insert(0, response);
        AddAuditEvent("TaxNet.AI.ModelGateway", "taxnet-model-admin", "ModelInvoked", taskType, "Succeeded", new Dictionary<string, object>
        {
            ["provider"] = response.SelectedProvider,
            ["route"] = response.Route,
            ["model"] = providerResult.Model,
            ["external"] = providerResult.UsedExternalProvider,
            ["caseId"] = request.CaseId ?? "",
            ["promptTokens"] = response.PromptTokens,
            ["completionTokens"] = response.CompletionTokens
        });
        SaveSnapshot();

        return response;
    }

    private static string Summarize(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "Document stored for RAG retrieval. Content summary was not provided.";
        }

        var clean = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= 220 ? clean : clean[..220] + "...";
    }

    private void IndexRagDocument(RagDocument document, string content)
    {
        RagChunks.RemoveAll(x => x.DocumentId == document.Id);
        var clean = string.IsNullOrWhiteSpace(content) ? document.Summary : content;
        var chunks = ChunkText(clean, 420);
        var indexedChunks = new List<RagChunk>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var text = chunks[i];
            var keywords = Tokenize($"{document.Title} {document.SourceType} {text} {string.Join(' ', document.Tags)}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToArray();
            var citation = new PolicyCitation(document.Title, document.Url, document.SourceType, $"chunk-{document.Id}-{i + 1:000}", document.CapturedAtUtc);
            var chunk = new RagChunk(
                citation.ChunkId,
                document.Id,
                document.Title,
                text,
                keywords,
                citation,
                decimal.Round(Math.Min(0.98m, 0.72m + (keywords.Length * 0.007m)), 2),
                DateTimeOffset.UtcNow);
            RagChunks.Add(chunk);
            indexedChunks.Add(chunk);
        }

        // Embedding indexing path (Req 5 AC 6): when an embedding provider is configured, embed each
        // chunk and write the vectors to the pluggable vector store. No-op when services are absent.
        if (_embeddingProvider is { IsConfigured: true } && _vectorStore is not null)
        {
            _vectorStore.RemoveDocumentAsync(document.Id, CancellationToken.None).GetAwaiter().GetResult();
            if (indexedChunks.Count > 0)
            {
                var entries = indexedChunks
                    .Select(chunk => new VectorEntry(
                        chunk.Id,
                        chunk.DocumentId,
                        _embeddingProvider.EmbedAsync(chunk.Text, CancellationToken.None).GetAwaiter().GetResult(),
                        chunk.Citation))
                    .ToList();
                _vectorStore.UpsertAsync(entries, CancellationToken.None).GetAwaiter().GetResult();
            }
        }
    }

    private static IReadOnlyList<string> ChunkText(string content, int targetLength)
    {
        var clean = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(clean))
        {
            return ["No policy content was provided; document metadata remains searchable."];
        }

        var chunks = new List<string>();
        for (var start = 0; start < clean.Length; start += targetLength)
        {
            var length = Math.Min(targetLength, clean.Length - start);
            chunks.Add(clean.Substring(start, length).Trim());
        }

        return chunks;
    }

    private static IReadOnlyList<string> Tokenize(string value)
    {
        var normalized = new string((value ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "for", "with", "that", "this", "from", "into", "are", "should", "must", "can", "when", "where", "was", "were", "has", "have", "policy", "document"
        };

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 2 && !stopWords.Contains(x))
            .ToArray();
    }

    private static string RedactSensitivePrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "No prompt supplied.";
        }

        var words = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(word =>
        {
            if (word.Contains('@', StringComparison.Ordinal)) return "[masked-email]";
            if (word.Contains('*', StringComparison.Ordinal)) return word; // already masked — leave it
            var lower = word.ToLowerInvariant();
            // Preserve non-PII internal identifiers so the model can reference them.
            if (lower.StartsWith("case-") || lower.StartsWith("entity-") || lower.StartsWith("rpt-") ||
                lower.StartsWith("ev-") || lower.StartsWith("chunk-") || lower.StartsWith("rag-") ||
                lower.StartsWith("ntn-") || lower.StartsWith("idtk-"))
            {
                return word;
            }

            // Preserve formatted financial figures (commas / decimals) — these are essential to the
            // analysis and are not PII (e.g. "28,000,000", "PKR", "3.5").
            if (word.Contains(',', StringComparison.Ordinal) || word.Contains('.', StringComparison.Ordinal))
            {
                return word;
            }

            // Mask only identifier-length digit runs: a raw unmasked CNIC (13 digits) or a phone
            // number (11–12 digits). Shorter numbers (amounts, years, cc, scores) are preserved.
            var digitCount = word.Count(char.IsDigit);
            var nonSeparator = word.Where(c => c != '-' && c != '+' && c != '(' && c != ')').All(char.IsDigit);
            if (nonSeparator && digitCount >= 11)
            {
                return "[masked-id]";
            }

            return word;
        }));
    }

    // Display-only masked form of a CNIC: keep the first block and last digit, mask the middle.
    // Used to backfill any "[masked-id]" the model emits with a readable, non-PII identifier.
    internal static string MaskCnicForDisplay(string cnic)
    {
        if (string.IsNullOrWhiteSpace(cnic)) return "[redacted]";
        if (cnic.Contains('*')) return cnic; // already masked
        var digits = new string(cnic.Where(char.IsDigit).ToArray());
        if (digits.Length < 6) return cnic;
        var first = digits.Length >= 5 ? digits[..5] : digits[..1];
        var last = digits[^1..];
        return $"{first}-*****-{last}";
    }

    private static int EstimateTokens(string text)
        => Math.Max(1, (int)Math.Ceiling((text?.Length ?? 0) / 4m));
}
