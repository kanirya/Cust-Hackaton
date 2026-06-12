namespace TaxNetGuardian.Api;

/// <summary>
/// Offline, dependency-free <see cref="IEmbeddingProvider"/>. Produces a fixed-dimension unit
/// vector from a stable FNV-1a hash of the normalized tokens in the text, so identical text always
/// yields an identical vector and cosine similarity tracks token overlap (supports Req 5 AC 5
/// determinism for the embedding path). <see cref="IsConfigured"/> is <c>true</c> only when the
/// embedding-backed vector store is explicitly selected; when the lexical store is selected the
/// provider reports <c>false</c> so retrieval uses the deterministic lexical fallback (Req 5 AC 3).
/// </summary>
public sealed class DeterministicEmbeddingProvider : IEmbeddingProvider
{
    private const int Dimensions = 256;
    private readonly bool _isConfigured;

    public DeterministicEmbeddingProvider(bool isConfigured) => _isConfigured = isConfigured;

    public bool IsConfigured => _isConfigured;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var vector = new float[Dimensions];
        if (!string.IsNullOrWhiteSpace(text))
        {
            var normalized = new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
            foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Length < 2)
                {
                    continue;
                }

                var index = (int)(StableHash(token) % Dimensions);
                vector[index] += 1f;
            }
        }

        var magnitude = MathF.Sqrt(vector.Sum(value => value * value));
        if (magnitude > 0f)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= magnitude;
            }
        }

        return Task.FromResult(vector);
    }

    private static uint StableHash(string value)
    {
        // FNV-1a: deterministic across runs and platforms (unlike string.GetHashCode()).
        uint hash = 2166136261;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return hash;
    }
}
