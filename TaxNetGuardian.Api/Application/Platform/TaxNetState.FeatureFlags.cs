namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    // Catalog of UI/feature toggles. Each is ON by default; operators override per-flag and the
    // override is persisted in the snapshot. The SPA reads these to show/hide features.
    private static readonly IReadOnlyList<(string Key, string Label, string Description, string Category, bool Default)> FeatureFlagCatalog =
    [
        ("aiAssistant", "AI Assistant", "Show the conversational AI assistant panel and its nav toggle.", "AI", true),
        ("cnicStreaming", "Streaming AI", "Stream AI narratives token-by-token instead of all-at-once.", "AI", true),
        ("regionalMap", "Regional Risk Map", "Show the geographic case-cluster map on the National Dashboard.", "Dashboard", true),
        ("ragAnimation", "RAG Flow Animation", "Animate the retrieval pipeline (graph view) on the RAG Policy page.", "RAG", true),
        ("quickAddPerson", "Quick Add Person", "Show the form-based sandbox seeding tool (no CSV needed).", "Sandbox", true),
        ("deepExplain", "Deep Explain", "Show the orchestrator deep-explanation action on investigations.", "Investigation", true),
        ("customModel", "Custom Model Training", "Show the Model Training workspace and enable the local knowledge-distillation model.", "AI", true),
        ("citizenPortal", "Citizen Portal", "Expose the citizen correction portal in navigation.", "Navigation", true)
    ];

    public IReadOnlyList<FeatureFlagView> GetFeatureFlags()
        => FeatureFlagCatalog
            .Select(f => new FeatureFlagView(
                f.Key,
                f.Label,
                f.Description,
                f.Category,
                FeatureFlags.TryGetValue(f.Key, out var v) ? v : f.Default))
            .ToArray();

    // Compact map (key -> enabled) for the SPA to consume directly.
    public IReadOnlyDictionary<string, bool> GetFeatureFlagMap()
        => GetFeatureFlags().ToDictionary(x => x.Key, x => x.Enabled, StringComparer.OrdinalIgnoreCase);

    public (bool Ok, FeatureFlagView? Flag) SetFeatureFlag(string key, bool enabled, string actor)
    {
        lock (_lock)
        {
            var def = FeatureFlagCatalog.FirstOrDefault(f => f.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (def.Key is null)
            {
                return (false, null);
            }

            FeatureFlags[def.Key] = enabled;
            AddAuditEvent(actor, "taxnet-admin", "FeatureFlagUpdated", def.Key, enabled ? "Enabled" : "Disabled", new Dictionary<string, object>
            {
                ["flag"] = def.Key,
                ["enabled"] = enabled
            });
            SaveSnapshot();
            return (true, new FeatureFlagView(def.Key, def.Label, def.Description, def.Category, enabled));
        }
    }
}
