namespace TaxNetGuardian.Api;

/// <summary>
/// Outcome of a claim-level explainability guardrail evaluation.
/// <see cref="Validated"/> means classification completed and a <see cref="ValidatedExplanation"/>
/// was produced. <see cref="EvaluationFailed"/> means the explanation could not be evaluated
/// (e.g. the case is missing or explanation construction threw) and no claims may be presented.
/// </summary>
public enum GuardrailStatus
{
    Validated,
    EvaluationFailed
}

public sealed partial class TaxNetState
{
    // Req 6 — Explainability Evidence Guardrail.
    // Decomposes a generated explanation into claims, maps each claim to evidence references
    // (case evidence ids ∪ score-component evidence ids ∪ citation chunk ids), classifies each
    // claim grounded/ungrounded, and only assembles the ValidatedExplanation AFTER classification
    // completes so raw claims are never returned before validation (AC 1). Never throws — any
    // failure to evaluate yields (EvaluationFailed, null, error) so the endpoint can withhold the
    // explanation and report a guardrail failure (AC 7).
    public (GuardrailStatus Status, ValidatedExplanation? Result, string? Error) ValidateExplanation(string caseId, string actor)
    {
        lock (_lock)
        {
            try
            {
                // Case must exist; otherwise we cannot evaluate (AC 7).
                var caseItem = Cases.FirstOrDefault(x => x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase));
                if (caseItem is null)
                {
                    return (GuardrailStatus.EvaluationFailed, null, $"Case {caseId} was not found.");
                }

                // Build the base explanation. This is the only AI-derived surface; its claims
                // are withheld until classification below completes.
                var explanation = BuildExplanation(caseId);

                // The set of valid evidence references: case evidence ids ∪ score-component
                // evidence ids ∪ explanation citation chunk ids.
                var validEvidence = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var evidence in caseItem.Evidence)
                {
                    if (!string.IsNullOrWhiteSpace(evidence.Id))
                    {
                        validEvidence.Add(evidence.Id);
                    }
                }
                foreach (var component in caseItem.Score.Components)
                {
                    foreach (var id in component.EvidenceIds)
                    {
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            validEvidence.Add(id);
                        }
                    }
                }
                foreach (var citation in explanation.Citations)
                {
                    if (!string.IsNullOrWhiteSpace(citation.ChunkId))
                    {
                        validEvidence.Add(citation.ChunkId);
                    }
                }

                // KeyReasons are built from the top scoring components (Score > 0, ranked, top 4).
                // Reconstruct that same ordered component list so claim i maps to component i.
                var topComponents = caseItem.Score.Components
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .Take(4)
                    .ToArray();

                // Claim decomposition: one claim per KeyReason. For each claim collect the
                // originating score-component evidence ids plus any citation chunk id whose topic
                // matches the claim text. A claim is grounded iff it has >= 1 evidence reference.
                var claims = new List<ExplanationClaim>(explanation.KeyReasons.Count);
                for (var i = 0; i < explanation.KeyReasons.Count; i++)
                {
                    var reason = explanation.KeyReasons[i];
                    var references = new List<string>();

                    if (i < topComponents.Length)
                    {
                        foreach (var id in topComponents[i].EvidenceIds)
                        {
                            if (!string.IsNullOrWhiteSpace(id) && validEvidence.Contains(id) && !references.Contains(id))
                            {
                                references.Add(id);
                            }
                        }
                    }

                    foreach (var citation in explanation.Citations)
                    {
                        if (string.IsNullOrWhiteSpace(citation.ChunkId) || references.Contains(citation.ChunkId))
                        {
                            continue;
                        }

                        if (CitationMatchesClaim(citation, reason))
                        {
                            references.Add(citation.ChunkId);
                        }
                    }

                    var grounded = references.Count >= 1;
                    claims.Add(new ExplanationClaim(
                        $"claim-{caseId}-{i + 1}",
                        reason,
                        references,
                        grounded));
                }

                // Counts partition the total (AC 4); evidence-backing follows GroundedCount (AC 8).
                var groundedCount = claims.Count(x => x.Grounded);
                var ungroundedCount = claims.Count - groundedCount;
                var outcome = new GuardrailOutcome(groundedCount, ungroundedCount, claims.Count);
                var evidenceBacked = groundedCount > 0;

                var explanationId = $"expl-{caseId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";

                // Assemble the validated explanation only after classification completes (AC 1).
                var validated = new ValidatedExplanation(
                    explanationId,
                    caseItem.Id,
                    explanation.Summary,
                    claims,
                    outcome,
                    evidenceBacked,
                    explanation.HumanReviewWarning);

                AddAuditEvent(actor, "taxnet-auditor", "GuardrailEvaluated", explanationId, "Succeeded", new Dictionary<string, object>
                {
                    ["explanationId"] = explanationId,
                    ["groundedCount"] = groundedCount,
                    ["ungroundedCount"] = ungroundedCount,
                    ["totalClaimCount"] = claims.Count
                });
                SaveSnapshot();

                return (GuardrailStatus.Validated, validated, null);
            }
            catch (Exception ex)
            {
                // Never let an exception escape the guardrail — the endpoint withholds claims (AC 7).
                return (GuardrailStatus.EvaluationFailed, null, ex.Message);
            }
        }
    }

    // Deterministic topic match: a citation grounds a claim when the claim text mentions a
    // significant word from the citation title or its source type.
    private static bool CitationMatchesClaim(PolicyCitation citation, string claimText)
    {
        if (string.IsNullOrWhiteSpace(claimText))
        {
            return false;
        }

        var haystack = claimText.ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(citation.SourceType) &&
            haystack.Contains(citation.SourceType.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(citation.Title))
        {
            return false;
        }

        foreach (var token in citation.Title.Split(new[] { ' ', '-', '_', '/', ',', '.', ':', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length >= 4 && haystack.Contains(token.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
