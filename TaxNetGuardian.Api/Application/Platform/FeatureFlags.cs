namespace TaxNetGuardian.Api;

// Request body for the per-key feature-flag toggle route (PUT /api/system/feature-flags/{key}).
public sealed record FeatureFlagUpdate(bool Enabled);
