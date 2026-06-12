namespace TaxNetGuardian.Api;

/// <summary>
/// Produces embedding vectors for query and chunk text (Req 5 AC 2/3/8).
/// <see cref="IsConfigured"/> is <c>true</c> only when an embedding-backed vector store
/// is explicitly selected; otherwise retrieval uses the deterministic lexical fallback.
/// </summary>
public interface IEmbeddingProvider
{
    bool IsConfigured { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// Abstracts the underlying embedding storage implementation behind which RAG context is
/// retrieved (Req 5 AC 1). The default offline implementation is <see cref="InMemoryVectorStore"/>;
/// pgvector/Qdrant/OpenSearch are future implementations behind the same contract.
/// </summary>
public interface IVectorStore
{
    Task UpsertAsync(IReadOnlyList<VectorEntry> entries, CancellationToken cancellationToken);

    Task<IReadOnlyList<VectorMatch>> QueryAsync(float[] queryEmbedding, int topK, CancellationToken cancellationToken);

    Task RemoveDocumentAsync(string documentId, CancellationToken cancellationToken);
}
