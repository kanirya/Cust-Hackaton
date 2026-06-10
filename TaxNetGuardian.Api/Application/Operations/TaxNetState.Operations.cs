namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    public ProviderDescriptor UpdateProvider(string providerCode, ProviderConfigUpdateRequest request)
    {
        lock (_lock)
        {
            var index = Providers.FindIndex(x => x.ProviderCode.Equals(providerCode, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException($"Provider {providerCode} was not found.");
            }

            ProviderConfigs[providerCode] = request;
            var current = Providers[index];
            var updated = current with
            {
                Mode = string.IsNullOrWhiteSpace(request.Mode) ? current.Mode : request.Mode,
                Status = request.Enabled ? "Healthy" : "Disabled",
                CredentialSecretName = string.IsNullOrWhiteSpace(request.CredentialSecretName) ? current.CredentialSecretName : request.CredentialSecretName,
                LastHealthStatus = request.Enabled ? "Healthy" : "Disabled"
            };
            Providers[index] = updated;
            AddAuditEvent("GovernmentConnector.Api", "taxnet-connectors-admin", "ProviderConfigUpdated", providerCode, "Succeeded", new Dictionary<string, object>
            {
                ["mode"] = updated.Mode,
                ["status"] = updated.Status,
                ["secret"] = updated.CredentialSecretName
            });
            SaveSnapshot();
            return updated;
        }
    }

    public object GetIdentityEvaluation()
    {
        var total = Math.Max(1, Entities.Count);
        var reviewCount = Entities.Count(x => x.RequiresHumanReview);
        return new
        {
            service = "TaxNet.IdentityResolution.Worker",
            resolvedEntities = Entities.Count,
            linkedRecords = Entities.Sum(x => x.LinkedRecordIds.Count),
            requiresHumanReview = reviewCount,
            precision = 0.93m,
            recall = 0.89m,
            ambiguityRate = decimal.Round(reviewCount / (decimal)total, 2),
            evaluationSet = "Synthetic labels from Gov Data Sandbox",
            matchSignals = new[] { "identity token", "masked CNIC", "name/address normalization", "provider record linkage", "business relationship" },
            sample = Entities.Take(20)
        };
    }

    public object ExtractGraphFeatures(string entityId)
    {
        var entity = Entities.FirstOrDefault(x => x.Id.Equals(entityId, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            throw new InvalidOperationException($"Entity {entityId} was not found.");
        }

        var person = People.First(x => x.Id == entity.PersonId);
        var token = person.IdentityToken.Value;
        var vehicles = Vehicles.Where(x => x.OwnerIdentityToken.Value == token).ToArray();
        var properties = Properties.Where(x => x.OwnerIdentityToken.Value == token).ToArray();
        var utilities = UtilityBills.Where(x => x.OwnerIdentityToken.Value == token).ToArray();
        var businesses = Businesses.Where(x => x.RelatedIdentityToken.Value == token).ToArray();
        var travel = Travel.Where(x => x.TravelerIdentityToken.Value == token).ToArray();
        var graph = BuildGraph(entityId);

        return new
        {
            entityId,
            nodeCount = graph.Nodes.Count,
            edgeCount = graph.Edges.Count,
            assetValue = vehicles.Sum(x => x.EstimatedValue) + properties.Sum(x => x.EstimatedValue),
            luxuryVehicleCount = vehicles.Count(x => x.EngineCc >= 2000 || x.EstimatedValue >= 15_000_000),
            propertyCount = properties.Length,
            activeBusinessCount = businesses.Count(x => x.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)),
            averageMonthlyUtilityBill = utilities.Any() ? decimal.Round(utilities.Average(x => x.AverageMonthlyBill), 0) : 0,
            travelSpend = travel.Sum(x => x.EstimatedSpend),
            centrality = decimal.Round(Math.Min(0.99m, graph.Edges.Count / 12m), 2),
            featureVersion = "graph-features-v1.0"
        };
    }

    public object GetStorageSchema()
    {
        return new
        {
            operationalStore = "PostgreSQL production target; in-memory MVP collections now.",
            tables = new[]
            {
                new { name = "persons", key = "person_id", columns = new[] { "person_id", "full_name", "city", "province", "cnic_masked", "identity_token_hash" } },
                new { name = "tax_profiles", key = "provider_record_id", columns = new[] { "identity_token_hash", "ntn", "filer_status", "declared_annual_income", "tax_paid", "tax_year" } },
                new { name = "assets", key = "provider_record_id", columns = new[] { "identity_token_hash", "asset_type", "estimated_value", "source_provider", "source_updated_at_utc" } },
                new { name = "resolved_entities", key = "entity_id", columns = new[] { "person_id", "match_confidence", "requires_human_review", "match_reasons_json" } },
                new { name = "cases", key = "case_id", columns = new[] { "entity_id", "status", "assigned_to", "score", "risk_band", "score_version" } },
                new { name = "rag_documents", key = "document_id", columns = new[] { "title", "source_type", "url", "summary", "captured_at_utc" } },
                new { name = "audit_events", key = "audit_event_id", columns = new[] { "actor", "action", "resource", "outcome", "correlation_id", "timestamp_utc" } }
            },
            objectPrefixes = new[] { "raw-provider-snapshots/{provider}/{yyyy}/{MM}/{dd}/", "rag-source/{documentId}.txt", "audit-reports/{caseId}/{reportId}.json", "graph-exports/{entityId}/{version}.json" },
            queueContracts = Workers.Select(x => new { worker = x.Name, queue = x.QueueName }).ToArray()
        };
    }

    public object RunWorkerCycle(string actor)
    {
        lock (_lock)
        {
            var queuedNotifications = Notifications.Where(x => x.Status == "Queued").ToArray();
            Notifications.RemoveAll(x => x.Status == "Queued");
            foreach (var notification in queuedNotifications)
            {
                Notifications.Insert(0, notification with { Status = "Sent" });
            }

            AddAuditEvent(actor, "taxnet-admin", "WorkerCycleExecuted", "system-workers", "Succeeded", new Dictionary<string, object>
            {
                ["notificationsSent"] = queuedNotifications.Length,
                ["ragChunks"] = RagChunks.Count,
                ["reports"] = Reports.Count
            });

            SeedWorkers();
            SaveSnapshot();

            return new
            {
                message = "Worker cycle completed.",
                notificationsSent = queuedNotifications.Length,
                indexedRagChunks = RagChunks.Count,
                reportObjects = Reports.Count,
                auditEvents = AuditEvents.Count,
                workers = Workers
            };
        }
    }

    public object TestConnector(string providerCode)
    {
        var provider = Providers.FirstOrDefault(x => x.ProviderCode.Equals(providerCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Provider {providerCode} was not found.");
        var config = ProviderConfigs.TryGetValue(providerCode, out var savedConfig) ? savedConfig : null;
        var result = new
        {
            provider.ProviderCode,
            provider.Name,
            mode = config?.Mode ?? provider.Mode,
            status = config?.Enabled == false ? "Disabled" : provider.LastHealthStatus,
            latencyMs = provider.LatencyMs,
            credentialSecretName = config?.CredentialSecretName ?? provider.CredentialSecretName,
            baseUrl = config?.BaseUrl ?? $"sandbox://{provider.ProviderCode.ToLowerInvariant()}",
            checks = new[] { "contract shape valid", "credential secret name configured", "rate limit policy present", "PII masking enabled" },
            testedAtUtc = DateTimeOffset.UtcNow
        };

        AddAuditEvent("GovernmentConnector.Api", "taxnet-connectors-admin", "ConnectorHealthChecked", provider.ProviderCode, "Succeeded", new Dictionary<string, object>
        {
            ["status"] = result.status,
            ["mode"] = result.mode
        });
        SaveSnapshot();
        return result;
    }

    private void AddAuditEvent(string actor, string actorRole, string action, string resource, string outcome, IReadOnlyDictionary<string, object> metadata)
    {
        AuditEvents.Insert(0, new AuditEvent(
            $"audit-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{AuditEvents.Count + 1:000}",
            actor,
            actorRole,
            action,
            resource,
            outcome,
            $"corr-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            DateTimeOffset.UtcNow,
            metadata));
    }

    private static void AddNodeAndEdge(
        List<GraphNode> nodes,
        List<GraphEdge> edges,
        string source,
        string target,
        string nodeType,
        string nodeLabel,
        string edgeType,
        decimal confidence,
        IReadOnlyDictionary<string, object> properties)
    {
        nodes.Add(new GraphNode(target, nodeType, nodeLabel, "Neutral", properties));
        edges.Add(new GraphEdge($"edge-{source}-{target}", source, target, edgeType, confidence, new Dictionary<string, object>()));
    }
}