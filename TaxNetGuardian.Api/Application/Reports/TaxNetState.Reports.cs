namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    /// <summary>
    /// Generates a full PDF audit report (§8.13) for a case: gathers the explanation, score
    /// components, evidence, policy citations, graph snapshot, AI narrative (live model when a
    /// key is configured), and citizen correction history; renders the PDF; stores it to the
    /// audit-reports bucket; and records the report, timeline, and audit events.
    /// </summary>
    public (byte[] Bytes, GeneratedReport Report) BuildReportPdf(string caseId, GraphNeighborhood graph, ModelGatewayClient? modelGatewayClient = null)
    {
        var caseItem = Cases.First(x => x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase));
        var person = People.First(x => x.Id == caseItem.PersonId);
        var explanation = BuildExplanation(caseId);
        var corrections = Corrections
            .Where(x => x.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.SubmittedAtUtc)
            .ToArray();

        var draft = InvokeModelGateway(new ModelInvocationRequest(
            "ReportDraft",
            $"{explanation.Summary}\nEvidence: {string.Join(", ", explanation.EvidenceIds)}",
            caseId,
            "auto",
            ModelGatewayClient.ExternalProvidersAllowed(true)),
            modelGatewayClient);
        var aiUsedExternal = draft.UsedExternalProvider && !string.IsNullOrWhiteSpace(draft.Output);

        var reportId = $"rpt-{caseId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var watermark = $"TaxNet Guardian | {caseId} | {reportId} | Generated {DateTimeOffset.UtcNow:O} | Confidential";
        var related = graph.Nodes
            .Where(n => !n.Id.Equals(caseItem.EntityId, StringComparison.OrdinalIgnoreCase))
            .Select(n => $"{n.Label} ({n.Type})")
            .Take(8)
            .ToArray();

        var model = new CaseReportModel(
            reportId,
            caseId,
            watermark,
            "demo-auditor",
            DateTimeOffset.UtcNow,
            person.FullName,
            person.CnicMasked,
            person.City,
            person.Province,
            caseItem.Score.Score,
            caseItem.Score.RiskBand,
            caseItem.Score.Confidence,
            caseItem.Score.RecommendedAction,
            caseItem.Score.Components,
            caseItem.Evidence,
            explanation.Citations,
            explanation.KeyReasons,
            explanation.Summary,
            draft.Output,
            draft.SelectedProvider,
            aiUsedExternal,
            graph.Nodes.Count,
            graph.Edges.Count,
            related,
            corrections,
            BuildFinalRecommendation(caseItem),
            explanation.HumanReviewWarning);

        var bytes = CaseReportComposer.RenderPdf(model);
        var key = $"audit-reports/{caseId}/{reportId}.pdf";
        var storageUri = $"s3://taxnet-dev-audit-reports/{key}";

        var report = new GeneratedReport(reportId, caseId, storageUri, "demo-auditor", DateTimeOffset.UtcNow, watermark, explanation.Summary);
        Reports.Insert(0, report);
        StoreObjectBytes("taxnet-dev-audit-reports", key, "application/pdf", bytes);
        AddTimeline(caseId, "ReportGenerated", report.GeneratedBy, $"PDF audit report {reportId} generated ({bytes.Length:N0} bytes) and stored at {storageUri}.");
        AddAuditEvent(report.GeneratedBy, "taxnet-auditor", "ReportGenerated", caseId, "Succeeded", new Dictionary<string, object>
        {
            ["reportId"] = report.Id,
            ["storageUri"] = storageUri,
            ["format"] = "pdf",
            ["bytes"] = bytes.Length
        });
        SaveSnapshot();
        return (bytes, report);
    }

    /// <summary>Returns the stored PDF bytes for a previously generated report, or null if missing.</summary>
    public byte[]? GetReportPdf(string reportId)
    {
        var report = Reports.FirstOrDefault(x => x.Id.Equals(reportId, StringComparison.OrdinalIgnoreCase));
        if (report is null)
        {
            return null;
        }

        var path = ObjectPath("taxnet-dev-audit-reports", $"audit-reports/{report.CaseId}/{reportId}.pdf");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>CSV export of the case worklist for supervisors (§8.13).</summary>
    public string ExportCasesCsv()
    {
        var rows = Cases
            .OrderByDescending(x => x.Score.Score)
            .Select(c =>
            {
                var person = People.FirstOrDefault(p => p.Id == c.PersonId);
                var topReason = c.Score.Components.Where(x => x.Score > 0).OrderByDescending(x => x.Score).FirstOrDefault();
                return new CaseCsvRow(
                    c.Id,
                    person?.FullName ?? c.PersonId,
                    person?.CnicMasked ?? "",
                    c.City,
                    c.Province,
                    c.Status,
                    c.AssignedTo,
                    c.Score.Score,
                    c.Score.RiskBand,
                    c.Score.Confidence,
                    c.Evidence.Count,
                    topReason?.Name ?? "",
                    c.UpdatedAtUtc);
            });

        return CaseReportComposer.ExportCasesCsv(rows);
    }

    internal static string BuildFinalRecommendation(CaseItem caseItem) => (caseItem.Score.RiskBand ?? "").ToLowerInvariant() switch
    {
        "critical" => "Escalate for priority human audit. Issue a citizen clarification notice, verify asset ownership dates and filing status, and prepare a recovery assessment if discrepancies are confirmed. The AI score is a review trigger, not proof of evasion.",
        "high" => "Assign to an auditor for evidence verification within the current cycle. Request a citizen correction where records appear outdated before any enforcement step.",
        "medium" => "Queue for routine review. Monitor for additional signals; take no enforcement action without corroborating evidence.",
        _ => "Low priority. Retain for periodic monitoring and close as no-action unless new evidence emerges."
    };
}
