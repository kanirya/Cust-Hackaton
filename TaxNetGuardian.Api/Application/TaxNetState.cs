using System.Globalization;
using System.Text.Json;

namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    private readonly object _lock = new();
    private readonly Random _random = new(42);
    private readonly List<CitizenCorrection> _corrections = [];
    private readonly string _dataRoot;
    private readonly string _statePath;
    private readonly string _objectRoot;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public TaxNetState(IWebHostEnvironment environment)
    {
        _dataRoot = Path.Combine(environment.ContentRootPath, "App_Data");
        _statePath = Path.Combine(_dataRoot, "taxnet-state.json");
        _objectRoot = Path.Combine(_dataRoot, "object-store");

        Directory.CreateDirectory(_dataRoot);
        Directory.CreateDirectory(_objectRoot);

        if (!LoadSnapshot())
        {
            GenerateSyntheticData(120, 24, 18);
        }
    }

    public List<SyntheticPerson> People { get; } = [];
    public List<TaxProfile> TaxProfiles { get; } = [];
    public List<VehicleRecord> Vehicles { get; } = [];
    public List<PropertyRecord> Properties { get; } = [];
    public List<UtilityBillRecord> UtilityBills { get; } = [];
    public List<BusinessRecord> Businesses { get; } = [];
    public List<TravelRecord> Travel { get; } = [];
    public List<ResolvedEntity> Entities { get; } = [];
    public List<CaseItem> Cases { get; } = [];
    public List<WorkerStatus> Workers { get; } = [];
    public List<ProviderDescriptor> Providers { get; } = [];
    public List<RagDocument> RagDocuments { get; } = [];
    public List<RagChunk> RagChunks { get; } = [];
    public List<DatasetBatch> DatasetBatches { get; } = [];
    public List<ImportJob> ImportJobs { get; } = [];
    public List<CaseTimelineEvent> TimelineEvents { get; } = [];
    public List<GeneratedReport> Reports { get; } = [];
    public List<ModelInvocationResponse> ModelInvocations { get; } = [];
    public List<AuditEvent> AuditEvents { get; } = [];
    public List<NotificationItem> Notifications { get; } = [];
    public List<ObjectStoreItem> ObjectStore { get; } = [];
    public Dictionary<string, ProviderConfigUpdateRequest> ProviderConfigs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<CitizenCorrection> Corrections => _corrections;

    public void GenerateSyntheticData(int count, int suspiciousPercent, int noisePercent)
    {
        lock (_lock)
        {
            People.Clear();
            TaxProfiles.Clear();
            Vehicles.Clear();
            Properties.Clear();
            UtilityBills.Clear();
            Businesses.Clear();
            Travel.Clear();
            Entities.Clear();
            Cases.Clear();
            Workers.Clear();
            Providers.Clear();
            RagDocuments.Clear();
            RagChunks.Clear();
            DatasetBatches.Clear();
            ImportJobs.Clear();
            TimelineEvents.Clear();
            Reports.Clear();
            ModelInvocations.Clear();
            AuditEvents.Clear();
            Notifications.Clear();
            ObjectStore.Clear();
            ProviderConfigs.Clear();
            _corrections.Clear();

            // Entity-resolution corpus: provider fragments + ground-truth labels.
            IdentityRecords.Clear();
            GroundTruth.Clear();
            IdentityClusters.Clear();
            DuplicateCandidates.Clear();
            _tokenCounter = 0;

            SeedProviders();
            SeedPolicyDocuments();
            SeedKnownDemoProfiles();
            SeedGeneratedProfiles(Math.Max(20, count), Math.Clamp(suspiciousPercent, 0, 70), Math.Clamp(noisePercent, 0, 80));
            RebuildIntelligence();
            SaveSnapshot();
        }
    }

    /// <summary>
    /// The intelligence pipeline:
    ///   1. ResolveEntities() clusters provider identity fragments WITHOUT the
    ///      ground-truth key (deterministic identifiers, then probabilistic
    ///      Jaro-Winkler matching with Urdu transliteration).
    ///   2. Pass A scores every resolved cluster from the records the resolver
    ///      actually linked.
    ///   3. Relationship discovery derives person-to-person edges (shared phone,
    ///      shared address, co-directorships, possible duplicates).
    ///   4. Pass B adds a NetworkRiskSignal component (max 5 points) based on
    ///      high-risk neighbours in the relationship graph.
    /// </summary>
    public void RebuildIntelligence()
    {
        Cases.Clear();

        ResolveEntities();

        // ---- Pass A: base risk score per resolved cluster ----
        var baseScores = new Dictionary<string, RiskScore>(StringComparer.OrdinalIgnoreCase);
        var clusterByEntity = new Dictionary<string, IdentityClusterInfo>(StringComparer.OrdinalIgnoreCase);
        var personByEntity = new Dictionary<string, SyntheticPerson>(StringComparer.OrdinalIgnoreCase);

        foreach (var cluster in IdentityClusters)
        {
            var person = People.FirstOrDefault(p => p.Id == cluster.PrimaryPersonId);
            if (person is null)
            {
                continue;
            }

            clusterByEntity[cluster.EntityId] = cluster;
            personByEntity[cluster.EntityId] = person;
            baseScores[cluster.EntityId] = CalculateRiskScore(person, cluster.FragmentTokenValues, cluster.EntityId, cluster.Confidence, cluster.RequiresHumanReview);
        }

        // ---- Relationship discovery over the resolved graph ----
        var relationships = ComputeEntityRelationships();
        var neighbours = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var duplicateInvolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relation in relationships)
        {
            if (!neighbours.TryGetValue(relation.EntityIdA, out var setA))
            {
                neighbours[relation.EntityIdA] = setA = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (!neighbours.TryGetValue(relation.EntityIdB, out var setB))
            {
                neighbours[relation.EntityIdB] = setB = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            setA.Add(relation.EntityIdB);
            setB.Add(relation.EntityIdA);

            if (relation.Type == "POSSIBLE_DUPLICATE_OF")
            {
                duplicateInvolved.Add(relation.EntityIdA);
                duplicateInvolved.Add(relation.EntityIdB);
            }
        }

        // ---- Pass B: network risk signal + case creation ----
        foreach (var (entityId, baseScore) in baseScores)
        {
            var person = personByEntity[entityId];
            var cluster = clusterByEntity[entityId];

            HashSet<string> neighbourIds = neighbours.TryGetValue(entityId, out var set) ? set : [];
            var highRiskNeighbours = neighbourIds
                .Where(n => baseScores.TryGetValue(n, out var s) && s.Score >= 61)
                .ToArray();
            var isDuplicateInvolved = duplicateInvolved.Contains(entityId);

            var networkPoints = Math.Min(5, highRiskNeighbours.Length * 2 + (isDuplicateInvolved ? 1 : 0));
            var networkExplanation = networkPoints == 0
                ? "No high-risk relationships detected in the 1-hop entity network."
                : $"Connected to {highRiskNeighbours.Length} high/critical-risk entit{(highRiskNeighbours.Length == 1 ? "y" : "ies")} via shared phone, address or directorship{(isDuplicateInvolved ? "; entity is part of a possible-duplicate pair under review" : "")}.";

            var components = baseScore.Components
                .Append(new RiskScoreComponent(
                    "NetworkRiskSignal",
                    networkPoints,
                    5,
                    highRiskNeighbours,
                    networkExplanation))
                .ToList();

            var total = Math.Clamp(components.Sum(x => x.Score), 0, 100);
            var band = total >= 81 ? "Critical" : total >= 61 ? "High" : total >= 31 ? "Medium" : "Low";
            var finalScore = baseScore with
            {
                Score = total,
                RiskBand = band,
                Components = components,
                RecommendedAction = total >= 81 ? "HumanReviewUrgent" : total >= 61 ? "HumanReview" : total >= 31 ? "MonitorAndRequestClarification" : "NoAction"
            };

            if (finalScore.Score < 31)
            {
                continue;
            }

            var evidence = BuildEvidence(cluster.FragmentTokenValues);
            Cases.Add(new CaseItem(
                finalScore.CaseId,
                entityId,
                person.Id,
                finalScore.Score >= 81 ? "FlaggedForReview" : "Scored",
                finalScore.Score >= 81 ? "Unassigned" : "Regional Queue",
                person.City,
                person.Province,
                finalScore,
                evidence,
                DateTimeOffset.UtcNow.AddHours(-_random.Next(2, 72)),
                DateTimeOffset.UtcNow));

            AddTimeline(finalScore.CaseId, "CaseCreated", "RiskScoring.Worker", $"{finalScore.RiskBand} case created with score {finalScore.Score}/100 from {cluster.FragmentTokenValues.Count} resolved identity fragment(s).");
            if (networkPoints > 0)
            {
                AddTimeline(finalScore.CaseId, "NetworkSignal", "GraphIntelligence.Worker", networkExplanation);
            }
        }

        SeedWorkers();
        SaveSnapshot();
    }

    public DashboardSummary GetDashboardSummary()
    {
        var casesByCity = Cases
            .GroupBy(x => x.City)
            .OrderByDescending(x => x.Count())
            .Take(8)
            .ToDictionary(x => x.Key, x => x.Count());

        var casesByReason = Cases
            .SelectMany(x => x.Score.Components.Where(c => c.Score > 0).Select(c => c.Name))
            .GroupBy(x => x)
            .OrderByDescending(x => x.Count())
            .ToDictionary(x => x.Key, x => x.Count());

        // Real pairwise precision/recall computed against generator ground truth.
        var evaluation = ComputeIdentityEvaluation();

        return new DashboardSummary(
            People.Count,
            Cases.Count,
            Cases.Count(x => x.Score.RiskBand == "Critical"),
            Cases.Count(x => x.Score.RiskBand == "High"),
            Cases.Count(x => x.Score.RiskBand == "Medium"),
            Cases.Sum(EstimateRecoverableTax),
            evaluation.Precision,
            evaluation.Recall,
            casesByCity,
            casesByReason);
    }

    public GraphNeighborhood BuildGraph(string entityId)
    {
        var entity = Entities.FirstOrDefault(x => x.Id.Equals(entityId, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            return new GraphNeighborhood([], []);
        }

        var person = People.FirstOrDefault(x => x.Id == entity.PersonId);
        var cluster = IdentityClusters.FirstOrDefault(x => x.EntityId.Equals(entity.Id, StringComparison.OrdinalIgnoreCase));
        var tokenSet = cluster is not null
            ? cluster.FragmentTokenValues.ToHashSet(StringComparer.Ordinal)
            : (person is not null ? new HashSet<string>(StringComparer.Ordinal) { person.IdentityToken.Value } : new HashSet<string>(StringComparer.Ordinal));

        var caseItem = Cases.FirstOrDefault(x => x.EntityId.Equals(entity.Id, StringComparison.OrdinalIgnoreCase));
        var riskBand = caseItem?.Score.RiskBand ?? "Low";

        var nodes = new List<GraphNode>();
        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<GraphEdge>();

        void AddNodeOnce(GraphNode node)
        {
            if (nodeIds.Add(node.Id))
            {
                nodes.Add(node);
            }
        }

        AddNodeOnce(new GraphNode(entity.Id, "Person", person?.FullName ?? entity.PersonId, riskBand, new Dictionary<string, object>
        {
            ["city"] = person?.City ?? "",
            ["province"] = person?.Province ?? "",
            ["cnicMasked"] = person?.CnicMasked ?? "",
            ["matchConfidence"] = entity.MatchConfidence,
            ["fragmentCount"] = tokenSet.Count,
            ["providers"] = cluster is not null ? string.Join(", ", cluster.ProviderCodes) : "NADRA"
        }));

        // ---- Identity fragments this entity was resolved from ----
        foreach (var fragment in IdentityRecords.Where(x => tokenSet.Contains(x.FragmentToken.Value)))
        {
            var fragmentNodeId = $"fragment-{fragment.RecordId}";
            AddNodeOnce(new GraphNode(fragmentNodeId, "IdentityFragment", $"{fragment.ProviderCode}: {DisplayName(fragment)}", "Neutral", new Dictionary<string, object>
            {
                ["provider"] = fragment.ProviderCode,
                ["recordedName"] = DisplayName(fragment),
                ["hasNtn"] = !string.IsNullOrWhiteSpace(fragment.Ntn),
                ["hasCnic"] = !string.IsNullOrWhiteSpace(fragment.CnicHash),
                ["hasPhone"] = !string.IsNullOrWhiteSpace(fragment.PhoneHash)
            }));
            edges.Add(new GraphEdge($"edge-resolved-{entity.Id}-{fragmentNodeId}", entity.Id, fragmentNodeId, "RESOLVED_FROM", entity.MatchConfidence, new Dictionary<string, object>()));
        }

        // ---- Assets linked through ANY fragment token in the cluster ----
        foreach (var vehicle in Vehicles.Where(x => tokenSet.Contains(x.OwnerIdentityToken.Value)))
        {
            AddNodeAndEdge(nodes, edges, entity.Id, $"vehicle-{vehicle.ProviderRecordId}", "Vehicle", $"{vehicle.Make} {vehicle.Model}", "OWNS_VEHICLE", 0.96m, new Dictionary<string, object>
            {
                ["engineCc"] = vehicle.EngineCc,
                ["estimatedValue"] = vehicle.EstimatedValue,
                ["registration"] = vehicle.RegistrationNumberMasked
            });
            nodeIds.Add($"vehicle-{vehicle.ProviderRecordId}");
        }

        foreach (var property in Properties.Where(x => tokenSet.Contains(x.OwnerIdentityToken.Value)))
        {
            AddNodeAndEdge(nodes, edges, entity.Id, $"property-{property.ProviderRecordId}", "Property", $"{property.Area}, {property.City}", "OWNS_PROPERTY", 0.92m, new Dictionary<string, object>
            {
                ["propertyType"] = property.PropertyType,
                ["estimatedValue"] = property.EstimatedValue
            });
            nodeIds.Add($"property-{property.ProviderRecordId}");
        }

        foreach (var bill in UtilityBills.Where(x => tokenSet.Contains(x.OwnerIdentityToken.Value)))
        {
            AddNodeAndEdge(nodes, edges, entity.Id, $"utility-{bill.ProviderRecordId}", "UtilityMeter", $"{bill.UtilityType} bill", "PAYS_UTILITY_BILL", 0.88m, new Dictionary<string, object>
            {
                ["averageMonthlyBill"] = bill.AverageMonthlyBill,
                ["latestBillAmount"] = bill.LatestBillAmount
            });
            nodeIds.Add($"utility-{bill.ProviderRecordId}");
        }

        foreach (var business in Businesses.Where(x => tokenSet.Contains(x.RelatedIdentityToken.Value)))
        {
            AddNodeAndEdge(nodes, edges, entity.Id, $"business-{business.ProviderRecordId}", "Business", business.CompanyName, "DIRECTOR_OF", 0.90m, new Dictionary<string, object>
            {
                ["registration"] = business.CompanyRegistrationNumber,
                ["status"] = business.Status
            });
            nodeIds.Add($"business-{business.ProviderRecordId}");
        }

        foreach (var trip in Travel.Where(x => tokenSet.Contains(x.TravelerIdentityToken.Value)))
        {
            AddNodeAndEdge(nodes, edges, entity.Id, $"travel-{trip.ProviderRecordId}", "TravelEvent", trip.Destination, "TRAVELLED_TO", 0.84m, new Dictionary<string, object>
            {
                ["tripsInLast24Months"] = trip.TripsInLast24Months,
                ["estimatedSpend"] = trip.EstimatedSpend
            });
            nodeIds.Add($"travel-{trip.ProviderRecordId}");
        }

        // ---- Person-to-person relationship edges (the actual network) ----
        foreach (var relation in ComputeEntityRelationships())
        {
            string otherId;
            if (relation.EntityIdA.Equals(entity.Id, StringComparison.OrdinalIgnoreCase))
            {
                otherId = relation.EntityIdB;
            }
            else if (relation.EntityIdB.Equals(entity.Id, StringComparison.OrdinalIgnoreCase))
            {
                otherId = relation.EntityIdA;
            }
            else
            {
                continue;
            }

            var otherEntity = Entities.FirstOrDefault(x => x.Id.Equals(otherId, StringComparison.OrdinalIgnoreCase));
            if (otherEntity is null)
            {
                continue;
            }

            var otherPerson = People.FirstOrDefault(x => x.Id == otherEntity.PersonId);
            var otherCase = Cases.FirstOrDefault(x => x.EntityId.Equals(otherId, StringComparison.OrdinalIgnoreCase));
            AddNodeOnce(new GraphNode(otherId, "Person", otherPerson?.FullName ?? otherEntity.PersonId, otherCase?.Score.RiskBand ?? "Low", new Dictionary<string, object>
            {
                ["city"] = otherPerson?.City ?? "",
                ["matchConfidence"] = otherEntity.MatchConfidence
            }));

            edges.Add(new GraphEdge(
                $"edge-{relation.Type.ToLowerInvariant()}-{entity.Id}-{otherId}",
                entity.Id,
                otherId,
                relation.Type,
                relation.Confidence,
                new Dictionary<string, object> { ["detail"] = relation.Detail }));
        }

        if (caseItem is not null)
        {
            AddNodeAndEdge(nodes, edges, entity.Id, caseItem.Id, "Case", $"{caseItem.Score.RiskBand} deviation case", "FLAGGED_IN_CASE", 1.0m, new Dictionary<string, object>
            {
                ["score"] = caseItem.Score.Score,
                ["status"] = caseItem.Status
            });
        }

        return new GraphNeighborhood(nodes, edges);
    }

    /// <summary>
    /// Derives person-to-person relationships from resolved entity data:
    ///   SHARES_PHONE_WITH      - same phone hash registered on fragments of two entities
    ///   HOUSEHOLD_MEMBER_OF    - same normalized residential address
    ///   CO_DIRECTOR_OF         - two entities related to one company registration
    ///   POSSIBLE_DUPLICATE_OF  - resolver review-band pair (0.75 - 0.92 score)
    /// Recomputed on demand from current state; cheap at sandbox scale.
    /// </summary>
    private sealed record EntityRelationship(string EntityIdA, string EntityIdB, string Type, decimal Confidence, string Detail);

    private List<EntityRelationship> ComputeEntityRelationships()
    {
        var relationships = new List<EntityRelationship>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var entityByToken = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var cluster in IdentityClusters)
        {
            foreach (var token in cluster.FragmentTokenValues)
            {
                entityByToken[token] = cluster.EntityId;
            }
        }

        void AddRelation(string a, string b, string type, decimal confidence, string detail)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || a.Equals(b, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var first = string.CompareOrdinal(a, b) <= 0 ? a : b;
            var second = string.CompareOrdinal(a, b) <= 0 ? b : a;
            if (seen.Add($"{type}|{first}|{second}"))
            {
                relationships.Add(new EntityRelationship(first, second, type, confidence, detail));
            }
        }

        // Shared phone hash (classic fraud signal: one phone across many identities).
        foreach (var group in IdentityRecords.Where(x => !string.IsNullOrWhiteSpace(x.PhoneHash)).GroupBy(x => x.PhoneHash, StringComparer.Ordinal))
        {
            var entities = group
                .Select(x => entityByToken.GetValueOrDefault(x.FragmentToken.Value, ""))
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (var i = 0; i < entities.Length; i++)
            {
                for (var j = i + 1; j < entities.Length; j++)
                {
                    AddRelation(entities[i], entities[j], "SHARES_PHONE_WITH", 0.95m, $"Same registered phone hash ({group.Key}).");
                }
            }
        }

        // Shared normalized residential address.
        foreach (var group in IdentityRecords
                     .Where(x => !string.IsNullOrWhiteSpace(x.Address))
                     .GroupBy(x => $"{NormalizeAddress(x.Address)}|{x.City.ToLowerInvariant()}", StringComparer.Ordinal))
        {
            var entities = group
                .Select(x => entityByToken.GetValueOrDefault(x.FragmentToken.Value, ""))
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (var i = 0; i < entities.Length; i++)
            {
                for (var j = i + 1; j < entities.Length; j++)
                {
                    AddRelation(entities[i], entities[j], "HOUSEHOLD_MEMBER_OF", 0.85m, "Same normalized residential address across provider records.");
                }
            }
        }

        // Co-directorship: two entities related to one company registration number.
        foreach (var group in Businesses.GroupBy(x => x.CompanyRegistrationNumber, StringComparer.OrdinalIgnoreCase))
        {
            var entities = group
                .Select(x => entityByToken.GetValueOrDefault(x.RelatedIdentityToken.Value, ""))
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var companyName = group.First().CompanyName;
            for (var i = 0; i < entities.Length; i++)
            {
                for (var j = i + 1; j < entities.Length; j++)
                {
                    AddRelation(entities[i], entities[j], "CO_DIRECTOR_OF", 0.92m, $"Co-directors of {companyName} ({group.Key}).");
                }
            }
        }

        // Possible duplicates from the resolver's human-review band.
        foreach (var duplicate in DuplicateCandidates)
        {
            AddRelation(
                duplicate.EntityIdA,
                duplicate.EntityIdB,
                "POSSIBLE_DUPLICATE_OF",
                duplicate.Score,
                duplicate.Reasons.Count > 0 ? duplicate.Reasons[0] : "Pairwise match score in human-review band.");
        }

        return relationships;
    }

    private static (string Provider, string Route) SelectModelRoute(string taskType, string preferredProvider, bool allowExternal)
    {
        if (!string.IsNullOrWhiteSpace(preferredProvider) && !preferredProvider.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return (preferredProvider, allowExternal ? "explicit-provider-route" : "explicit-local-only-route");
        }

        return taskType switch
        {
            "AuditExplanation" when allowExternal => ("OpenAI-compatible external provider", "external-frontier-llm with redacted case packet"),
            "PolicyQuestion" when allowExternal => ("OpenAI-compatible external provider", "rag-grounded policy route"),
            "ReportDraft" => ("Local deterministic report model", "local-template-first route"),
            "CitizenExplanation" => ("Local privacy model", "local-redacted-citizen-safe route"),
            _ => ("Deterministic template fallback", "offline-template route")
        };
    }

    private static string BuildReportDraft(string caseId, AuditExplanation explanation, RagQueryResult rag)
    {
        return $"Report draft for {caseId}: {explanation.Summary} Include evidence IDs {string.Join(", ", explanation.EvidenceIds.Take(8))}. Policy context: {string.Join(" | ", rag.Chunks.Take(2).Select(x => x.Text))}. {explanation.HumanReviewWarning}";
    }

    private static PolicyCitation ToCitation(RagDocument document, string chunkId)
        => new(document.Title, document.Url, document.SourceType, chunkId, document.CapturedAtUtc);

    private static int ClampScore(int value, int max) => Math.Clamp(value, 0, max);

}