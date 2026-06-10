namespace TaxNetGuardian.Api;

public sealed record GraphNode(
    string Id,
    string Type,
    string Label,
    string RiskBand,
    IReadOnlyDictionary<string, object> Properties);

public sealed record GraphEdge(
    string Id,
    string Source,
    string Target,
    string Type,
    decimal Confidence,
    IReadOnlyDictionary<string, object> Properties);

public sealed record GraphNeighborhood(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);

