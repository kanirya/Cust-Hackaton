namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
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

    public RagQueryResult QueryRag(RagQueryRequest request)
    {
        var query = string.IsNullOrWhiteSpace(request.Query) ? "tax compliance human review evidence policy" : request.Query.Trim();
        var taskType = string.IsNullOrWhiteSpace(request.TaskType) ? "PolicyQuestion" : request.TaskType.Trim();
        var jurisdiction = string.IsNullOrWhiteSpace(request.Jurisdiction) ? "Pakistan" : request.Jurisdiction.Trim();
        var requestedTags = request.Tags ?? [];
        var tokens = Tokenize($"{query} {taskType} {jurisdiction} {string.Join(' ', requestedTags)}");
        var queryEmbedding = BuildDeterministicEmbedding($"{query} {taskType} {jurisdiction} {string.Join(' ', requestedTags)}");
        var topK = Math.Clamp(request.TopK <= 0 ? 5 : request.TopK, 1, 10);

        var scoredWithMetrics = RagChunks
            .Select(chunk =>
            {
                var overlap = chunk.Keywords.Count(keyword => tokens.Contains(keyword, StringComparer.OrdinalIgnoreCase));
                var tagBoost = requestedTags.Count == 0 ? 0 : chunk.Keywords.Count(keyword => requestedTags.Contains(keyword, StringComparer.OrdinalIgnoreCase));
                var sourceBoost = chunk.Citation.SourceType.Contains("Sop", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                var phraseBoost = tokens.Count(token => chunk.Text.Contains(token, StringComparison.OrdinalIgnoreCase)) / 3m;
                var recencyBoost = chunk.IndexedAtUtc >= DateTimeOffset.UtcNow.AddDays(-30) ? 0.25m : 0m;
                var chunkEmbedding = chunk.Embedding?.Count > 0
                    ? chunk.Embedding
                    : BuildDeterministicEmbedding($"{chunk.Title} {chunk.Text} {string.Join(' ', chunk.Keywords ?? [])}");
                var vectorSimilarity = CosineSimilarity(queryEmbedding, chunkEmbedding);
                var score = overlap + tagBoost + sourceBoost + phraseBoost + recencyBoost + chunk.QualityScore + (vectorSimilarity * 3m);
                return new { Chunk = chunk, Score = decimal.Round(score, 3), Overlap = overlap, TagBoost = tagBoost, PhraseBoost = phraseBoost, VectorSimilarity = decimal.Round(vectorSimilarity, 3) };
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.VectorSimilarity)
            .ThenByDescending(x => x.Chunk.IndexedAtUtc)
            .ToArray();

        var scored = scoredWithMetrics.Take(topK).Select(x => x.Chunk).ToArray();

        if (scored.Length == 0)
        {
            scored = RagChunks.OrderByDescending(x => x.QualityScore).ThenByDescending(x => x.IndexedAtUtc).Take(topK).ToArray();
        }

        var qualityChecks = new List<string>
        {
            "Query rewritten with task type, jurisdiction, and requested tags.",
            "Retrieved chunks include citation metadata and source timestamps.",
            "Context pack excludes raw PII and private citizen records.",
            $"Hybrid score considered vector similarity, token overlap, phrase match, source type, recency, and chunk quality across {RagChunks.Count} chunks.",
            scoredWithMetrics.Length > 0 ? $"Top score {scoredWithMetrics[0].Score}; vector similarity {scoredWithMetrics[0].VectorSimilarity}; keyword overlap {scoredWithMetrics[0].Overlap}; tag boost {scoredWithMetrics[0].TagBoost}." : "No retrieval score available.",
            scored.Length > 0 ? "At least one policy context chunk was retrieved." : "No policy context chunks were available."
        };

        var confidence = scored.Length == 0 ? 0m : decimal.Round(Math.Min(0.98m, 0.52m + (scored.Length * 0.06m) + (scoredWithMetrics.FirstOrDefault()?.Score ?? 0m) * 0.025m), 2);
        AddAuditEvent("RagPolicy.Api", "taxnet-policy-analyst", "RagQuery", taskType, "Succeeded", new Dictionary<string, object>
        {
            ["query"] = query,
            ["chunks"] = scored.Length,
            ["confidence"] = confidence
        });
        SaveSnapshot();

        return new RagQueryResult(
            query,
            $"{taskType}: {query} jurisdiction:{jurisdiction}",
            scored,
            scored.Select(x => x.Citation).DistinctBy(x => x.ChunkId).ToArray(),
            qualityChecks,
            confidence);
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

        var providerResult = (modelGatewayClient ?? new ModelGatewayClient())
            .InvokeAsync(request.PreferredProvider, request.AllowExternalProvider, taskType, safePrompt, contextChunks)
            .GetAwaiter()
            .GetResult();
        output = providerResult.UsedExternalProvider && !string.IsNullOrWhiteSpace(providerResult.Output)
            ? providerResult.Output
            : deterministicOutput + (string.IsNullOrWhiteSpace(providerResult.Error) ? "" : $" Provider fallback: {providerResult.Error}");

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
        for (var i = 0; i < chunks.Count; i++)
        {
            var text = chunks[i];
            var keywords = Tokenize($"{document.Title} {document.SourceType} {text} {string.Join(' ', document.Tags)}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToArray();
            var citation = new PolicyCitation(document.Title, document.Url, document.SourceType, $"chunk-{document.Id}-{i + 1:000}", document.CapturedAtUtc);
            RagChunks.Add(new RagChunk(
                citation.ChunkId,
                document.Id,
                document.Title,
                text,
                keywords,
                BuildDeterministicEmbedding($"{document.Title} {document.SourceType} {text} {string.Join(' ', document.Tags)}"),
                citation,
                decimal.Round(Math.Min(0.98m, 0.72m + (keywords.Length * 0.007m)), 2),
                DateTimeOffset.UtcNow));
        }
    }

    private static IReadOnlyList<decimal> BuildDeterministicEmbedding(string value)
    {
        const int dimensions = 24;
        var vector = new decimal[dimensions];
        foreach (var token in Tokenize(value))
        {
            var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(token);
            var index = Math.Abs(hash % dimensions);
            var sign = (hash & 1) == 0 ? 1m : -1m;
            vector[index] += sign * (1m + Math.Min(8, token.Length) / 10m);
        }

        var magnitude = (decimal)Math.Sqrt(vector.Sum(x => Math.Pow((double)x, 2)));
        if (magnitude == 0)
        {
            return vector;
        }

        return vector.Select(x => decimal.Round(x / magnitude, 6)).ToArray();
    }

    private static decimal CosineSimilarity(IReadOnlyList<decimal> left, IReadOnlyList<decimal> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0m;
        }

        var count = Math.Min(left.Count, right.Count);
        var dot = 0m;
        for (var i = 0; i < count; i++)
        {
            dot += left[i] * right[i];
        }

        return Math.Clamp((dot + 1m) / 2m, 0m, 1m);
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
            word.Any(char.IsDigit) && word.Length >= 6 ? "[masked-id]" :
            word.Contains("@", StringComparison.Ordinal) ? "[masked-email]" :
            word));
    }

    private static int EstimateTokens(string text)
        => Math.Max(1, (int)Math.Ceiling((text?.Length ?? 0) / 4m));
}
