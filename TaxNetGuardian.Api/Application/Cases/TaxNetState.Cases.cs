using System.Text.Json;

namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    public CitizenCorrection AddCorrection(CitizenCorrectionRequest request)
    {
        lock (_lock)
        {
            var correction = new CitizenCorrection(
                $"corr-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{_corrections.Count + 1:000}",
                request.CaseId,
                request.CorrectionType,
                request.Message,
                request.EvidenceFileIds,
                "Submitted",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);

            _corrections.Insert(0, correction);
            UpdateCaseStatus(request.CaseId, "CitizenResponded", "Citizen Portal", $"Citizen submitted {request.CorrectionType} correction.");
            Notifications.Insert(0, new NotificationItem(
                $"notif-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Notifications.Count + 1:000}",
                "Regional Queue",
                "InApp",
                $"Citizen correction submitted for {request.CaseId}",
                request.Message,
                "Queued",
                DateTimeOffset.UtcNow));
            AddAuditEvent("Citizen Portal", "taxnet-citizen", "CitizenCorrectionSubmitted", request.CaseId, "Succeeded", new Dictionary<string, object>
            {
                ["correctionId"] = correction.Id,
                ["correctionType"] = correction.CorrectionType
            });
            SaveSnapshot();
            return correction;
        }
    }

    public CaseItem AssignCase(string caseId, CaseAssignmentRequest request, string actor)
    {
        lock (_lock)
        {
            var caseItem = GetCaseOrThrow(caseId);
            var assignedTo = string.IsNullOrWhiteSpace(request.AssignedTo) ? actor : request.AssignedTo.Trim();
            var updated = caseItem with { AssignedTo = assignedTo, Status = "Assigned", UpdatedAtUtc = DateTimeOffset.UtcNow };
            ReplaceCase(updated);
            AddTimeline(caseId, "CaseAssigned", actor, $"Case assigned to {assignedTo}.");
            AddAuditEvent(actor, "taxnet-supervisor", "CaseAssigned", caseId, "Succeeded", new Dictionary<string, object> { ["assignedTo"] = assignedTo });
            SaveSnapshot();
            return updated;
        }
    }

    public CaseItem RequestCitizenClarification(string caseId, string actor)
    {
        lock (_lock)
        {
            var updated = UpdateCaseStatus(caseId, "CitizenClarificationRequested", actor, "Citizen clarification requested before escalation.");
            SaveSnapshot();
            return updated;
        }
    }

    public CaseItem RecordDecision(string caseId, CaseDecisionRequest request, string actor)
    {
        lock (_lock)
        {
            var normalized = string.IsNullOrWhiteSpace(request.Decision) ? "UnderReview" : request.Decision.Trim();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "UnderReview",
                "EvidenceVerified",
                "ClosedNoAction",
                "ClosedEscalated",
                "ClosedRecovered",
                "ClosedFalsePositive"
            };

            if (!allowed.Contains(normalized))
            {
                throw new InvalidOperationException($"Decision '{request.Decision}' is not allowed.");
            }

            var summary = string.IsNullOrWhiteSpace(request.Notes)
                ? $"Auditor recorded decision {normalized}."
                : $"Auditor recorded decision {normalized}: {request.Notes.Trim()}";

            var updated = UpdateCaseStatus(caseId, normalized, actor, summary);
            SaveSnapshot();
            return updated;
        }
    }

    public AuditExplanation BuildExplanation(string caseId)
    {
        var caseItem = Cases.First(x => x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase));
        var person = People.First(x => x.Id == caseItem.PersonId);
        var topComponents = caseItem.Score.Components
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(4)
            .ToArray();

        var reasons = topComponents
            .Select(x => $"{x.Name}: {x.Explanation}")
            .ToArray();

        var summary = $"{person.FullName} was flagged for human review because the declared tax profile is inconsistent with observed asset, consumption, and business signals. The current score is {caseItem.Score.Score}/100 ({caseItem.Score.RiskBand}) with {caseItem.Score.Confidence:P0} confidence.";

        return new AuditExplanation(
            caseItem.Id,
            summary,
            reasons,
            topComponents.SelectMany(x => x.EvidenceIds).Distinct().ToArray(),
            BuildCitations(topComponents.Select(x => x.Name).ToArray()),
            "This is a decision-support explanation. It does not prove fraud and must be reviewed by an authorized human auditor.");
    }

    public object BuildReport(string caseId)
    {
        var caseItem = Cases.First(x => x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase));
        var person = People.First(x => x.Id == caseItem.PersonId);
        var explanation = BuildExplanation(caseId);
        var reportId = $"rpt-{caseId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var watermark = $"TaxNet Guardian | {caseId} | Generated {DateTimeOffset.UtcNow:O}";
        var report = new GeneratedReport(
            reportId,
            caseId,
            $"s3://taxnet-dev-audit-reports/audit-reports/{caseId}/{reportId}.json",
            "demo-auditor",
            DateTimeOffset.UtcNow,
            watermark,
            explanation.Summary);

        Reports.Insert(0, report);
        StoreObject("taxnet-dev-audit-reports", $"audit-reports/{caseId}/{reportId}.json", "application/json", JsonSerializer.Serialize(report));
        AddTimeline(caseId, "ReportGenerated", report.GeneratedBy, $"Audit report {reportId} generated and stored at {report.StorageUri}.");
        AddAuditEvent(report.GeneratedBy, "taxnet-auditor", "ReportGenerated", caseId, "Succeeded", new Dictionary<string, object>
        {
            ["reportId"] = report.Id,
            ["storageUri"] = report.StorageUri
        });
        SaveSnapshot();

        return new
        {
            reportId,
            generatedAtUtc = report.GeneratedAtUtc,
            storageUri = report.StorageUri,
            watermark,
            caseSummary = explanation.Summary,
            subject = new
            {
                person.FullName,
                person.City,
                person.Province,
                person.CnicMasked
            },
            score = caseItem.Score,
            evidence = caseItem.Evidence,
            explanation.KeyReasons,
            explanation.Citations,
            disclaimer = explanation.HumanReviewWarning
        };
    }

    public IReadOnlyList<CaseTimelineEvent> GetTimeline(string caseId)
    {
        return TimelineEvents
            .Where(x => x.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.TimestampUtc)
            .ToArray();
    }

    public object AskAssistant(string caseId, string question)
    {
        var explanation = BuildExplanation(caseId);
        var caseItem = Cases.First(x => x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase));
        var lower = question.ToLowerInvariant();
        string answer;

        if (lower.Contains("missing") || lower.Contains("verify"))
        {
            answer = "Recommended next verification steps: confirm asset ownership dates, verify whether business income was declared under another NTN, request updated utility meter ownership, and give the citizen a correction window before escalation.";
        }
        else if (lower.Contains("citizen") || lower.Contains("explain"))
        {
            answer = "Citizen-safe explanation: some records linked to this profile appear inconsistent with the latest tax filing. The citizen should review asset ownership, business relationships, and utility records, then submit corrections if any record is outdated or incorrectly linked.";
        }
        else
        {
            answer = explanation.Summary + " Key reasons: " + string.Join(" ", explanation.KeyReasons.Take(3));
        }

        return new
        {
            answer,
            caseItem.Score.Score,
            caseItem.Score.RiskBand,
            evidenceIds = explanation.EvidenceIds,
            citations = explanation.Citations,
            warnings = new[] { explanation.HumanReviewWarning },
            modelRoute = new
            {
                orchestrator = "TaxNet.AI.Orchestrator",
                selectedModel = "deterministic-demo-model",
                fallbackOrder = new[] { "external-frontier-llm", "local-model", "template" },
                piiPolicy = "Case context is masked before external model calls."
            }
        };
    }

    private static decimal EstimateRecoverableTax(CaseItem caseItem)
    {
        var assetSignals = caseItem.Evidence.Where(x => x.Type is "Vehicle" or "Property").Sum(x => x.Amount ?? 0);
        return decimal.Round(assetSignals * (caseItem.Score.Score / 100m) * 0.035m, 0);
    }

    private CaseItem GetCaseOrThrow(string caseId)
    {
        return Cases.FirstOrDefault(x => x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Case {caseId} was not found.");
    }

    private CaseItem UpdateCaseStatus(string caseId, string status, string actor, string summary)
    {
        var caseItem = GetCaseOrThrow(caseId);
        var updated = caseItem with { Status = status, UpdatedAtUtc = DateTimeOffset.UtcNow };
        ReplaceCase(updated);
        AddTimeline(caseId, "CaseStatusChanged", actor, summary);
        AddAuditEvent(actor, "taxnet-auditor", "CaseStatusChanged", caseId, "Succeeded", new Dictionary<string, object>
        {
            ["status"] = status,
            ["summary"] = summary
        });
        return updated;
    }

    private void ReplaceCase(CaseItem updated)
    {
        var index = Cases.FindIndex(x => x.Id.Equals(updated.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            Cases[index] = updated;
        }
    }

    private void AddTimeline(string caseId, string eventType, string actor, string summary)
    {
        TimelineEvents.Insert(0, new CaseTimelineEvent(
            $"evt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{TimelineEvents.Count + 1:000}",
            caseId,
            eventType,
            actor,
            summary,
            DateTimeOffset.UtcNow));
    }
}
