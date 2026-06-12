namespace TaxNetGuardian.Api;

public sealed record VectorEntry(
    string ChunkId, string DocumentId, float[] Embedding, PolicyCitation Citation);

public sealed record VectorMatch(string ChunkId, string DocumentId, double Similarity, PolicyCitation Citation);

public enum RetrievalPath { Embedding, DeterministicFallback }
