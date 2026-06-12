namespace TaxNetGuardian.Api;

// A single supervised training example captured from a frontier-LLM invocation. The frontier
// model's (prompt -> response) pair is the teacher signal we distil the local model from.
public sealed record TrainingExample(
    string Id,
    string TaskType,
    string Prompt,
    string Response,
    string TeacherProvider,
    string PromptHash,
    int PromptTokens,
    int ResponseTokens,
    int TrainedIntoVersion,
    DateTimeOffset CreatedAtUtc);

// Hold-out evaluation metrics produced by a training run.
public sealed record ModelEvaluationMetrics(
    double ValidationSimilarity,
    double Coverage,
    double GroundednessScore,
    double AvgRetrievalConfidence,
    double AvgLatencyMs,
    IReadOnlyDictionary<string, double> PerTaskSimilarity);

// One training run = one model version. Status moves Queued -> Running -> Succeeded/Failed.
public sealed record ModelTrainingRun(
    string Id,
    int Version,
    string Status,
    string TriggeredBy,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    double DurationMs,
    int TotalExamples,
    int TrainCount,
    int ValidationCount,
    int VocabularySize,
    ModelEvaluationMetrics? Metrics,
    string? Notes);

// Request to preview the custom model against an arbitrary prompt.
public sealed record CustomModelTestRequest(string TaskType, string Prompt);

// Request to switch inference routing.
public sealed record InferenceModeRequest(string Mode);
