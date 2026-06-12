namespace TaxNetGuardian.Api;

public enum FailureBehavior { Offline, StaleData, PartialData, RateLimited, ServerError }

public sealed record FailureRuleRequest(
    string ProviderCode,
    string Behavior,                 // parsed case-insensitively into FailureBehavior
    int? InjectedLatencyMs);         // optional 0..60000

public sealed record FailureRule(
    string RuleId,
    string ProviderCode,
    FailureBehavior Behavior,
    int InjectedLatencyMs,
    bool Active,
    DateTimeOffset CreatedAtUtc);

public enum FailureApplication { None, Offline, StaleData, PartialData, RateLimited, ServerError }

public sealed record FailureDecision(
    FailureApplication Application,
    int InjectedLatencyMs,
    string? RuleId);                 // None when no active rule applies (AC 14)
