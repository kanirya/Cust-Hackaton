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

    public object BuildReport(string caseId, ModelGatewayClient? modelGatewayClient = null)
    {
        var caseItem = Cases.First(x => x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase));
        var person = People.First(x => x.Id == caseItem.PersonId);
        var explanation = BuildExplanation(caseId);

        // Draft the report narrative through the Model Gateway. Uses live Claude when a key is
        // configured, and falls back to the deterministic ReportDraft template otherwise.
        var draft = InvokeModelGateway(new ModelInvocationRequest(
            "ReportDraft",
            $"{explanation.Summary}\nEvidence: {string.Join(", ", explanation.EvidenceIds)}",
            caseId,
            "auto",
            ModelGatewayClient.ExternalProvidersAllowed(true)),
            modelGatewayClient);
        var draftedByExternal = draft.UsedExternalProvider && !string.IsNullOrWhiteSpace(draft.Output);

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
            narrative = draft.Output,
            narrativeModel = new
            {
                selectedProvider = draft.SelectedProvider,
                route = draft.Route,
                usedExternalProvider = draftedByExternal,
                invocationId = draft.InvocationId,
                citations = draft.Citations
            },
            correctionHistory = Corrections
                .Where(x => x.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.SubmittedAtUtc),
            finalRecommendation = BuildFinalRecommendation(caseItem),
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

    public object AskAssistant(string caseId, string question, ModelGatewayClient? modelGatewayClient = null)
    {
        var explanation = BuildExplanation(caseId);
        var caseItem = Cases.First(x => x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase));
        var lower = question.ToLowerInvariant();
        string deterministicAnswer;

        if (lower.Contains("missing") || lower.Contains("verify"))
        {
            deterministicAnswer = "Recommended next verification steps: confirm asset ownership dates, verify whether business income was declared under another NTN, request updated utility meter ownership, and give the citizen a correction window before escalation.";
        }
        else if (lower.Contains("citizen") || lower.Contains("explain"))
        {
            deterministicAnswer = "Citizen-safe explanation: some records linked to this profile appear inconsistent with the latest tax filing. The citizen should review asset ownership, business relationships, and utility records, then submit corrections if any record is outdated or incorrectly linked.";
        }
        else
        {
            deterministicAnswer = explanation.Summary + " Key reasons: " + string.Join(" ", explanation.KeyReasons.Take(3));
        }

        // Route the question through the real Model Gateway (live Claude/etc. when a key is
        // configured, deterministic fallback otherwise). The gateway adds RAG grounding,
        // guardrails, PII redaction, cost tracking and an audit event.
        var invocation = InvokeModelGateway(new ModelInvocationRequest(
            "AuditExplanation",
            $"Auditor question about case {caseId}: {question}\nCase summary: {explanation.Summary}",
            caseId,
            "auto",
            ModelGatewayClient.ExternalProvidersAllowed(true)),
            modelGatewayClient);

        var usedExternal = invocation.UsedExternalProvider && !string.IsNullOrWhiteSpace(invocation.Output);
        var answer = usedExternal ? invocation.Output : deterministicAnswer;

        return new
        {
            answer,
            caseItem.Score.Score,
            caseItem.Score.RiskBand,
            evidenceIds = explanation.EvidenceIds,
            citations = invocation.Citations.Count > 0 ? invocation.Citations : explanation.Citations,
            warnings = new[] { explanation.HumanReviewWarning },
            modelRoute = new
            {
                orchestrator = "TaxNet.AI.Orchestrator",
                selectedModel = invocation.SelectedProvider,
                route = invocation.Route,
                usedExternalProvider = usedExternal,
                invocationId = invocation.InvocationId,
                fallbackOrder = new[] { "external-frontier-llm", "local-model", "template" },
                piiPolicy = "Case context is masked before external model calls."
            }
        };
    }

    public CnicInvestigationResult InvestigateByCnic(CnicInvestigationRequest request, ModelGatewayClient? modelGatewayClient = null)
    {
        var rawCnic = (request.Cnic ?? "").Trim();
        if (string.IsNullOrWhiteSpace(rawCnic))
        {
            throw new InvalidOperationException("CNIC is required for investigation.");
        }

        var person = ResolvePersonByCnic(rawCnic)
            ?? throw new InvalidOperationException("No sandbox identity matched the supplied CNIC.");
        var token = person.IdentityToken.Value;
        var caseItem = Cases.FirstOrDefault(x => x.PersonId.Equals(person.Id, StringComparison.OrdinalIgnoreCase));
        var entity = Entities.FirstOrDefault(x => x.PersonId.Equals(person.Id, StringComparison.OrdinalIgnoreCase));
        var records = BuildCnicLinkedRecords(token);
        var signals = BuildCnicInvestigationSignals(person, records, caseItem);
        var prompt = BuildCnicInvestigationPrompt(person, caseItem, records, signals);

        var invocation = InvokeModelGateway(new ModelInvocationRequest(
            "CnicInvestigation",
            prompt,
            caseItem?.Id ?? "",
            string.IsNullOrWhiteSpace(request.PreferredProvider) ? "claude" : request.PreferredProvider.Trim(),
            ModelGatewayClient.ExternalProvidersAllowed(request.AllowExternalProvider)),
            modelGatewayClient);

        var fallbackNarrative = BuildDeterministicCnicNarrative(person, caseItem, records, signals);
        var usedExternal = invocation.UsedExternalProvider && !string.IsNullOrWhiteSpace(invocation.Output);
        var narrative = usedExternal ? invocation.Output : fallbackNarrative;
        var findings = signals
            .OrderByDescending(x => x.Severity == "Critical")
            .ThenByDescending(x => x.Severity == "High")
            .Select(x => x.Detail)
            .Take(6)
            .ToArray();
        var actions = BuildCnicRecommendedActions(caseItem, records, usedExternal).ToArray();

        AddAuditEvent("TaxNet.AI.CnicInvestigator", "taxnet-auditor", "CnicInvestigation", person.Id, "Succeeded", new Dictionary<string, object>
        {
            ["caseId"] = caseItem?.Id ?? "",
            ["records"] = records.Count,
            ["signals"] = signals.Count,
            ["modelProvider"] = invocation.SelectedProvider,
            ["usedExternalProvider"] = usedExternal
        });
        SaveSnapshot();

        return new CnicInvestigationResult(
            $"cnic-investigation-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            "Completed",
            person.CnicMasked,
            new
            {
                person.Id,
                person.FullName,
                person.FatherName,
                person.City,
                person.Province,
                identityTokenType = person.IdentityToken.TokenType,
                identityLinking = "All matched provider records share the same canonical CNIC identity token."
            },
            caseItem is null ? null : new
            {
                caseItem.Id,
                caseItem.Status,
                caseItem.Score.Score,
                caseItem.Score.RiskBand,
                caseItem.Score.Confidence,
                caseItem.Score.RecommendedAction
            },
            records,
            signals,
            narrative,
            findings,
            actions,
            new
            {
                selectedProvider = invocation.SelectedProvider,
                invocation.Route,
                usedExternalProvider = usedExternal,
                invocation.InvocationId,
                invocation.PromptTokens,
                invocation.CompletionTokens
            },
            "CNIC-linked investigation is a decision-support view only. It does not prove fraud and requires authorized human review plus citizen correction opportunity.",
            DateTimeOffset.UtcNow);
    }

    private static decimal EstimateRecoverableTax(CaseItem caseItem)
    {
        var assetSignals = caseItem.Evidence.Where(x => x.Type is "Vehicle" or "Property").Sum(x => x.Amount ?? 0);
        return decimal.Round(assetSignals * (caseItem.Score.Score / 100m) * 0.035m, 0);
    }

    private SyntheticPerson? ResolvePersonByCnic(string cnic)
    {
        var normalized = NormalizeCnic(cnic);
        var digits = DigitsOnly(cnic);
        return People.FirstOrDefault(person =>
            NormalizeCnic(person.CnicMasked).Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            DigitsOnly(person.CnicMasked).Equals(digits, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(digits) &&
             digits.Length >= 2 &&
             DigitsOnly(person.CnicMasked).StartsWith(digits[..Math.Min(5, digits.Length)], StringComparison.OrdinalIgnoreCase) &&
             DigitsOnly(person.CnicMasked).EndsWith(digits[^2..], StringComparison.OrdinalIgnoreCase)) ||
            person.IdentityToken.Value.Equals(cnic, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<CnicInvestigationRecord> BuildCnicLinkedRecords(string identityToken)
    {
        var records = new List<CnicInvestigationRecord>();
        records.AddRange(TaxProfiles
            .Where(x => x.IdentityToken.Value == identityToken)
            .Select(x => new CnicInvestigationRecord("FBR", "TaxProfile", x.ProviderRecordId, x.Ntn, $"{x.FilerStatus}; declared income PKR {x.DeclaredAnnualIncome:N0}; tax paid PKR {x.TaxPaid:N0}.", x.DeclaredAnnualIncome, x.SourceUpdatedAtUtc)));
        records.AddRange(Vehicles
            .Where(x => x.OwnerIdentityToken.Value == identityToken)
            .Select(x => new CnicInvestigationRecord("Excise", "Vehicle", x.ProviderRecordId, x.RegistrationNumberMasked, $"{x.Make} {x.Model}, {x.EngineCc}cc, estimated value PKR {x.EstimatedValue:N0}.", x.EstimatedValue, x.SourceUpdatedAtUtc)));
        records.AddRange(Properties
            .Where(x => x.OwnerIdentityToken.Value == identityToken)
            .Select(x => new CnicInvestigationRecord("Property", "Property", x.ProviderRecordId, x.PropertyToken, $"{x.PropertyType} property at {x.Area}, {x.City}; estimated value PKR {x.EstimatedValue:N0}.", x.EstimatedValue, x.SourceUpdatedAtUtc)));
        records.AddRange(UtilityBills
            .Where(x => x.OwnerIdentityToken.Value == identityToken)
            .Select(x => new CnicInvestigationRecord("Utility", "UtilityBill", x.ProviderRecordId, x.MeterToken, $"{x.UtilityType}; average monthly bill PKR {x.AverageMonthlyBill:N0}; latest bill PKR {x.LatestBillAmount:N0}.", x.AverageMonthlyBill, x.SourceUpdatedAtUtc)));
        records.AddRange(Businesses
            .Where(x => x.RelatedIdentityToken.Value == identityToken)
            .Select(x => new CnicInvestigationRecord("SECP", "Business", x.ProviderRecordId, x.CompanyRegistrationNumber, $"{x.RelationshipType} of {x.CompanyName}; status {x.Status}.", null, x.SourceUpdatedAtUtc)));
        records.AddRange(Travel
            .Where(x => x.TravelerIdentityToken.Value == identityToken)
            .Select(x => new CnicInvestigationRecord("Travel", "Travel", x.ProviderRecordId, x.Destination, $"{x.TripsInLast24Months} trip(s) in last 24 months; estimated spend PKR {x.EstimatedSpend:N0}.", x.EstimatedSpend, x.SourceUpdatedAtUtc)));
        return records.OrderBy(x => x.Provider).ThenBy(x => x.RecordType).ToArray();
    }

    private static IReadOnlyList<CnicInvestigationSignal> BuildCnicInvestigationSignals(
        SyntheticPerson person,
        IReadOnlyList<CnicInvestigationRecord> records,
        CaseItem? caseItem)
    {
        var signals = new List<CnicInvestigationSignal>
        {
            new(
                "CNIC identity linkage",
                "Verified",
                $"CNIC {person.CnicMasked} resolves to one canonical sandbox identity token used across tax, vehicle, property, utility, business, and travel records even if displayed names differ.",
                records.Select(x => x.RecordId).Take(12).ToArray())
        };

        if (caseItem is not null)
        {
            signals.AddRange(caseItem.Score.Components
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(5)
                .Select(x => new CnicInvestigationSignal(
                    x.Name,
                    x.Score >= Math.Max(1, x.MaxScore * 0.75m) ? "High" : "Medium",
                    x.Explanation,
                    x.EvidenceIds)));
        }

        var taxIncome = records.FirstOrDefault(x => x.RecordType == "TaxProfile")?.Amount ?? 0m;
        var assetValue = records.Where(x => x.RecordType is "Vehicle" or "Property").Sum(x => x.Amount ?? 0);
        if (taxIncome > 0 && assetValue / taxIncome > 10)
        {
            signals.Add(new CnicInvestigationSignal(
                "CNIC asset-to-income mismatch",
                "High",
                $"The same CNIC identity links PKR {assetValue:N0} of vehicle/property records against declared income PKR {taxIncome:N0}.",
                records.Where(x => x.RecordType is "Vehicle" or "Property" or "TaxProfile").Select(x => x.RecordId).ToArray()));
        }

        return signals.DistinctBy(x => x.Name).ToArray();
    }

    private static string BuildCnicInvestigationPrompt(
        SyntheticPerson person,
        CaseItem? caseItem,
        IReadOnlyList<CnicInvestigationRecord> records,
        IReadOnlyList<CnicInvestigationSignal> signals)
    {
        var recordLines = string.Join("\n", records.Select(x => $"- {x.Provider}/{x.RecordType}/{x.RecordId}: {x.Summary}"));
        var signalLines = string.Join("\n", signals.Select(x => $"- {x.Severity} {x.Name}: {x.Detail}"));
        return $"""
               Investigate this taxpayer by CNIC identity linkage.
               CNIC is the stable identifier in Pakistan. Names may vary across vehicle registration, utility bills, salary/tax records, or business records, but records sharing the same CNIC identity token should be assessed together.

               Subject:
               {person.FullName}, {person.CnicMasked}, {person.City}, {person.Province}

               Case:
               {(caseItem is null ? "No risk case exists." : $"{caseItem.Id}; score {caseItem.Score.Score}/100 {caseItem.Score.RiskBand}; status {caseItem.Status}.")}

               Matched CNIC-linked records:
               {recordLines}

               Deterministic signals:
               {signalLines}

               Return a concise auditor-ready narrative with these headings:
               Assessment, CNIC linkage, Key mismatches, Evidence to verify, Recommended next steps, Human review warning.
               Do not return JSON. Do not say fraud is proven.
               """;
    }

    private static string BuildDeterministicCnicNarrative(
        SyntheticPerson person,
        CaseItem? caseItem,
        IReadOnlyList<CnicInvestigationRecord> records,
        IReadOnlyList<CnicInvestigationSignal> signals)
    {
        var topSignals = string.Join(" ", signals.Take(4).Select(x => $"{x.Name}: {x.Detail}"));
        return $"Assessment: {person.FullName} has {records.Count} records linked through the same CNIC identity. CNIC linkage: the investigation uses {person.CnicMasked} as the stable identity key, so name differences in vehicle, utility, tax, salary, or business systems do not break linkage. Key mismatches: {topSignals} Recommended next steps: verify ownership dates, confirm filing status and declared salary/tax records, request citizen clarification for stale records, and document evidence before escalation. Human review warning: this is decision support only and does not prove fraud.";
    }

    private static IEnumerable<string> BuildCnicRecommendedActions(
        CaseItem? caseItem,
        IReadOnlyList<CnicInvestigationRecord> records,
        bool usedExternal)
    {
        yield return "Verify the CNIC token against authoritative identity records before relying on cross-provider matches.";
        yield return "Compare tax/salary declaration records with asset ownership dates, utility consumption, and business directorship periods.";
        if (records.Any(x => x.RecordType is "Vehicle" or "Property"))
        {
            yield return "Confirm current ownership and transfer dates for linked vehicle/property records.";
        }

        if (caseItem?.Score.RiskBand is "Critical" or "High")
        {
            yield return "Keep the case in human review and request citizen clarification before escalation.";
        }

        yield return usedExternal
            ? "Review the Claude-generated narrative against structured evidence before copying it into a report."
            : "Configure a Claude API key for a richer external-model narrative; deterministic safeguards are currently being used.";
    }

    private static string NormalizeCnic(string value)
        => new((value ?? "").Where(c => char.IsLetterOrDigit(c) || c == '*').Select(char.ToUpperInvariant).ToArray());

    private static string DigitsOnly(string value)
        => new((value ?? "").Where(char.IsDigit).ToArray());

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
