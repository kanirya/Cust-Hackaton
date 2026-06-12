namespace TaxNetGuardian.Api;

/// <summary>
/// Default offline <see cref="IVectorStore"/>. Ranks stored <see cref="VectorEntry"/> values by
/// cosine similarity to the query embedding. <see cref="QueryAsync"/> returns at most <c>topK</c>
/// matches in descending similarity order, returns ALL available matches when fewer than
/// <c>topK</c> exist, and never emits empty or placeholder entries (Req 5 AC 4). Ties break on
/// chunk id so identical inputs yield an identical ordering (Req 5 AC 5).
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, VectorEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public Task UpsertAsync(IReadOnlyList<VectorEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_lock)
        {
            foreach (var entry in entries)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.ChunkId))
                {
                    continue;
                }

                _entries[entry.ChunkId] = entry;
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorMatch>> QueryAsync(float[] queryEmbedding, int topK, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (topK <= 0 || queryEmbedding is null || queryEmbedding.Length == 0 || _entries.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<VectorMatch>>(Array.Empty<VectorMatch>());
            }

            var matches = _entries.Values
                .Select(entry => new VectorMatch(
                    entry.ChunkId,
                    entry.DocumentId,
                    CosineSimilarity(queryEmbedding, entry.Embedding),
                    entry.Citation))
                .OrderByDescending(match => match.Similarity)
                .ThenBy(match => match.ChunkId, StringComparer.Ordinal)
                .Take(topK)
                .ToArray();

            return Task.FromResult<IReadOnlyList<VectorMatch>>(matches);
        }
    }

    public Task RemoveDocumentAsync(string documentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return Task.CompletedTask;
        }

        lock (_lock)
        {
            var keys = _entries
                .Where(kv => kv.Value.DocumentId.Equals(documentId, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToArray();
            foreach (var key in keys)
            {
                _entries.Remove(key);
            }
        }

        return Task.CompletedTask;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a is null || b is null || a.Length == 0 || b.Length == 0)
        {
            return 0d;
        }

        var length = Math.Min(a.Length, b.Length);
        double dot = 0d, magnitudeA = 0d, magnitudeB = 0d;
        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        if (magnitudeA <= 0d || magnitudeB <= 0d)
        {
            return 0d;
        }

        return dot / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }
}
