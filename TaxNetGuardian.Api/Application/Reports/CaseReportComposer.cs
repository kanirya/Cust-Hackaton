using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace TaxNetGuardian.Api;

/// <summary>
/// Renders the audit report sections from §8.13 of the system design as a PDF (QuestPDF)
/// and produces CSV exports for supervisors. The composer is pure: it takes a fully-built
/// <see cref="CaseReportModel"/> and has no dependency on application state.
/// </summary>
public static class CaseReportComposer
{
    public static byte[] RenderPdf(CaseReportModel m)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(34);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("TaxNet Guardian").FontSize(18).Bold().FontColor(Colors.Blue.Darken3);
                            c.Item().Text("Confidential Audit Report — Decision Support").FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                        row.ConstantItem(170).AlignRight().Column(c =>
                        {
                            c.Item().Text(m.ReportId).FontSize(9).Bold();
                            c.Item().Text($"Case {m.CaseId}").FontSize(9);
                            c.Item().Text($"Generated {m.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC").FontSize(8).FontColor(Colors.Grey.Darken1);
                            c.Item().Text($"By {m.GeneratedBy}").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                    });
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Background(BandColor(m.RiskBand)).Padding(10).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(m.SubjectName).FontSize(14).Bold().FontColor(Colors.White);
                            c.Item().Text($"{m.City}, {m.Province}   •   CNIC {m.CnicMasked}").FontSize(9).FontColor(Colors.White);
                        });
                        row.ConstantItem(130).AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text($"{m.Score}/100").FontSize(20).Bold().FontColor(Colors.White);
                            c.Item().AlignRight().Text($"{m.RiskBand} • {m.Confidence:P0}").FontSize(9).FontColor(Colors.White);
                        });
                    });

                    Section(col, "1. Case Summary", c => c.Item().Text(m.Summary));

                    Section(col, "2. Risk Score Breakdown", c =>
                    {
                        c.Item().Text($"Recommended action: {m.RecommendedAction}").FontSize(9).Italic();
                        c.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(cd => { cd.RelativeColumn(3); cd.ConstantColumn(48); cd.RelativeColumn(6); });
                            TableHeader(table, "Component", "Score", "Explanation");
                            foreach (var comp in m.Components)
                            {
                                table.Cell().Element(Cell).Text(comp.Name);
                                table.Cell().Element(Cell).Text($"{comp.Score}/{comp.MaxScore}");
                                table.Cell().Element(Cell).Text(comp.Explanation);
                            }
                        });
                    });

                    Section(col, "3. Evidence", c =>
                    {
                        if (m.Evidence.Count == 0) { c.Item().Text("No structured evidence recorded.").Italic(); return; }
                        c.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cd => { cd.ConstantColumn(64); cd.RelativeColumn(5); cd.ConstantColumn(78); cd.RelativeColumn(3); });
                            TableHeader(table, "Type", "Title / Description", "Amount (PKR)", "Source");
                            foreach (var e in m.Evidence)
                            {
                                table.Cell().Element(Cell).Text(e.Type);
                                table.Cell().Element(Cell).Text($"{e.Title}: {e.Description}");
                                table.Cell().Element(Cell).Text(e.Amount.HasValue ? e.Amount.Value.ToString("N0") : "-");
                                table.Cell().Element(Cell).Text(e.Source);
                            }
                        });
                    });

                    Section(col, "4. Graph Snapshot", c =>
                    {
                        c.Item().Text($"Investigation neighborhood: {m.GraphNodeCount} nodes, {m.GraphEdgeCount} relationships.");
                        if (m.GraphRelated.Count > 0)
                            c.Item().PaddingTop(2).Text("Related entities: " + string.Join(", ", m.GraphRelated)).FontSize(9);
                    });

                    Section(col, "5. Policy Citations", c =>
                    {
                        if (m.Citations.Count == 0) { c.Item().Text("No policy citations attached.").Italic(); return; }
                        foreach (var cit in m.Citations)
                            c.Item().Text($"• {cit.Title}  ({cit.SourceType})  —  {cit.Url}").FontSize(9);
                    });

                    Section(col, "6. AI-Assisted Narrative", c =>
                    {
                        c.Item().Text(m.AiUsedExternal ? $"Source: {m.AiProvider} (live model)" : "Source: deterministic template fallback")
                            .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
                        c.Item().PaddingTop(4).Text(m.AiNarrative);
                    });

                    Section(col, "7. Citizen Correction History", c =>
                    {
                        if (m.Corrections.Count == 0) { c.Item().Text("No citizen corrections submitted.").Italic(); return; }
                        foreach (var corr in m.Corrections)
                            c.Item().Text($"• [{corr.SubmittedAtUtc:yyyy-MM-dd}] {corr.CorrectionType} ({corr.Status}): {corr.Message}").FontSize(9);
                    });

                    Section(col, "8. Final Recommendation", c => c.Item().Text(m.FinalRecommendation));

                    col.Item().PaddingTop(4).Background(Colors.Grey.Lighten3).Padding(8)
                        .Text(m.Disclaimer).FontSize(8).Italic().FontColor(Colors.Grey.Darken2);
                });

                page.Footer().Column(col =>
                {
                    col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                    col.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text(m.Watermark).FontSize(7).FontColor(Colors.Grey.Darken1);
                        row.ConstantItem(90).AlignRight().Text(t =>
                        {
                            t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                            t.Span("Page "); t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
                        });
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    public static string ExportCasesCsv(IEnumerable<CaseCsvRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("caseId,subject,cnicMasked,city,province,status,assignedTo,score,riskBand,confidence,evidenceCount,topReason,updatedAtUtc");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.CaseId), Csv(r.Subject), Csv(r.CnicMasked), Csv(r.City), Csv(r.Province),
                Csv(r.Status), Csv(r.AssignedTo), r.Score, Csv(r.RiskBand),
                r.Confidence.ToString("0.00"), r.EvidenceCount, Csv(r.TopReason), Csv(r.UpdatedAtUtc.ToString("o"))));
        }

        return sb.ToString();
    }

    private static void Section(ColumnDescriptor col, string title, Action<ColumnDescriptor> body)
        => col.Item().Column(c =>
        {
            c.Item().Text(title).FontSize(12).Bold().FontColor(Colors.Blue.Darken2);
            c.Item().PaddingTop(2).Column(body);
        });

    private static void TableHeader(TableDescriptor table, params string[] headers)
        => table.Header(h =>
        {
            foreach (var head in headers)
                h.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text(head).Bold().FontSize(9);
        });

    private static IContainer Cell(IContainer c)
        => c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(3);

    private static string BandColor(string band) => (band ?? "").ToLowerInvariant() switch
    {
        "critical" => Colors.Red.Darken2,
        "high" => Colors.Orange.Darken2,
        "medium" => Colors.Amber.Darken3,
        _ => Colors.Green.Darken2
    };

    private static string Csv(string? value)
    {
        value ??= "";
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}

public sealed record CaseReportModel(
    string ReportId,
    string CaseId,
    string Watermark,
    string GeneratedBy,
    DateTimeOffset GeneratedAtUtc,
    string SubjectName,
    string CnicMasked,
    string City,
    string Province,
    int Score,
    string RiskBand,
    decimal Confidence,
    string RecommendedAction,
    IReadOnlyList<RiskScoreComponent> Components,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<PolicyCitation> Citations,
    IReadOnlyList<string> KeyReasons,
    string Summary,
    string AiNarrative,
    string AiProvider,
    bool AiUsedExternal,
    int GraphNodeCount,
    int GraphEdgeCount,
    IReadOnlyList<string> GraphRelated,
    IReadOnlyList<CitizenCorrection> Corrections,
    string FinalRecommendation,
    string Disclaimer);

public sealed record CaseCsvRow(
    string CaseId,
    string Subject,
    string CnicMasked,
    string City,
    string Province,
    string Status,
    string AssignedTo,
    int Score,
    string RiskBand,
    decimal Confidence,
    int EvidenceCount,
    string TopReason,
    DateTimeOffset UpdatedAtUtc);
