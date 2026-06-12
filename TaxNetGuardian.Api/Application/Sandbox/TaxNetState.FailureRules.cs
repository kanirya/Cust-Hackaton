namespace TaxNetGuardian.Api;

/// <summary>
/// Sandbox Failure and Latency Simulator state methods (Req 3).
/// Failure rules live on <see cref="TaxNetState"/>; enforcement happens at the shared
/// <c>SandboxFailureSimulator</c> evaluation point. All mutations validate before mutating,
/// audit, and snapshot.
/// </summary>
public sealed partial class TaxNetState
{
    // AC 1,2,3,4,15 — validate fully, then create. On any validation failure return
    // (null, error) WITHOUT mutating state.
    public (FailureRule? Rule, string? ValidationError) CreateFailureRule(FailureRuleRequest request, string actor)
    {
        lock (_lock)
        {
            if (request is null)
            {
                return (null, "Failure-rule request is required.");
            }

            // AC 2 — provider code must match an existing sandbox provider (case-insensitive).
            var providerCode = request.ProviderCode?.Trim();
            if (string.IsNullOrWhiteSpace(providerCode))
            {
                return (null, "Provider code is required.");
            }

            var matchedProvider = ResolveKnownProviderCode(providerCode);
            if (matchedProvider is null)
            {
                return (null, $"Unknown provider code '{providerCode}'. It must match an existing sandbox provider.");
            }

            // AC 3 — behavior must parse (case-insensitively) to exactly one FailureBehavior.
            if (string.IsNullOrWhiteSpace(request.Behavior) ||
                !Enum.TryParse<FailureBehavior>(request.Behavior.Trim(), ignoreCase: true, out var behavior) ||
                !Enum.IsDefined(behavior))
            {
                return (null, $"Invalid behavior '{request.Behavior}'. Allowed: {string.Join(", ", Enum.GetNames<FailureBehavior>())}.");
            }

            // AC 3 — injected latency, when present, must be within [0, 60000].
            if (request.InjectedLatencyMs is { } latency && (latency < 0 || latency > 60000))
            {
                return (null, "Injected latency must be between 0 and 60000 milliseconds.");
            }

            // Validation complete — safe to mutate (AC 1, 15).
            var rule = new FailureRule(
                $"frule-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{FailureRules.Count + 1:000}",
                matchedProvider,
                behavior,
                request.InjectedLatencyMs ?? 0,
                Active: true,
                CreatedAtUtc: DateTimeOffset.UtcNow);

            FailureRules.Add(rule);
            AddAuditEvent(actor, "taxnet-sandbox-admin", "FailureRuleCreated", matchedProvider, "Succeeded", new Dictionary<string, object>
            {
                ["ruleId"] = rule.RuleId,
                ["behavior"] = rule.Behavior.ToString()
            });
            SaveSnapshot();
            return (rule, null);
        }
    }

    // AC 5,6,15 — remove the rule; return false when not found.
    public bool DeleteFailureRule(string ruleId, string actor)
    {
        lock (_lock)
        {
            var rule = FailureRules.FirstOrDefault(r =>
                r.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
            if (rule is null)
            {
                return false;
            }

            FailureRules.Remove(rule);
            AddAuditEvent(actor, "taxnet-sandbox-admin", "FailureRuleDeleted", rule.ProviderCode, "Succeeded", new Dictionary<string, object>
            {
                ["ruleId"] = rule.RuleId,
                ["behavior"] = rule.Behavior.ToString()
            });
            SaveSnapshot();
            return true;
        }
    }

    // AC 13,14 — most-recent active rule for the provider wins; None when no active rule.
    public FailureDecision ResolveFailureDecision(string providerCode)
    {
        lock (_lock)
        {
            var rule = FailureRules
                .Where(r => r.Active && r.ProviderCode.Equals(providerCode, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefault();

            if (rule is null)
            {
                return new FailureDecision(FailureApplication.None, 0, null);
            }

            var application = rule.Behavior switch
            {
                FailureBehavior.Offline => FailureApplication.Offline,
                FailureBehavior.StaleData => FailureApplication.StaleData,
                FailureBehavior.PartialData => FailureApplication.PartialData,
                FailureBehavior.RateLimited => FailureApplication.RateLimited,
                FailureBehavior.ServerError => FailureApplication.ServerError,
                _ => FailureApplication.None
            };

            return new FailureDecision(application, rule.InjectedLatencyMs, rule.RuleId);
        }
    }

    // Returns the canonical (existing) provider code for a request, or null when unknown.
    // Valid codes are the seeded sandbox providers plus the canonical "SANDBOX" pipeline code.
    private string? ResolveKnownProviderCode(string providerCode)
    {
        var match = Providers.FirstOrDefault(p =>
            p.ProviderCode.Equals(providerCode, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match.ProviderCode;
        }

        return providerCode.Equals("SANDBOX", StringComparison.OrdinalIgnoreCase) ? "SANDBOX" : null;
    }
}
