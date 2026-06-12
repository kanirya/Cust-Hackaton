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
    private readonly TaxNetPlatformOptions _platformOptions;
    private readonly PostgresSnapshotStore _postgresSnapshots;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public TaxNetState(IWebHostEnvironment environment, IConfiguration configuration, TaxNetPlatformOptions? platformOptions = null)
    {
        _platformOptions = platformOptions ?? new TaxNetPlatformOptions();
        _dataRoot = Path.Combine(environment.ContentRootPath, "App_Data");
        _statePath = Path.Combine(_dataRoot, "taxnet-state.json");
        _objectRoot = Path.Combine(_dataRoot, "object-store");
        _postgresSnapshots = new PostgresSnapshotStore(ResolvePostgresConnectionString(configuration));

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

            SeedProviders();
            SeedPolicyDocuments();
            SeedKnownDemoProfiles();
            SeedGeneratedProfiles(Math.Max(20, count), Math.Clamp(suspiciousPercent, 0, 70), Math.Clamp(noisePercent, 0, 80));
            RebuildIntelligence();
            SaveSnapshot();
        }
    }

    public void RebuildIntelligence()
    {
        Entities.Clear();
        Cases.Clear();

        foreach (var person in People)
        {
            var linkedRecords = new List<string>();
            linkedRecords.AddRange(TaxProfiles.Where(x => x.IdentityToken.Value == person.IdentityToken.Value).Select(x => x.ProviderRecordId));
            linkedRecords.AddRange(Vehicles.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value).Select(x => x.ProviderRecordId));
            linkedRecords.AddRange(Properties.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value).Select(x => x.ProviderRecordId));
            linkedRecords.AddRange(UtilityBills.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value).Select(x => x.ProviderRecordId));
            linkedRecords.AddRange(Businesses.Where(x => x.RelatedIdentityToken.Value == person.IdentityToken.Value).Select(x => x.ProviderRecordId));
            linkedRecords.AddRange(Travel.Where(x => x.TravelerIdentityToken.Value == person.IdentityToken.Value).Select(x => x.ProviderRecordId));

            // Use real weighted Jaro-Winkler identity resolution algorithm (§14.3)
            var matchResult = IdentityResolutionEngine.CalculateMatchScore(person, linkedRecords);
            var entity = new ResolvedEntity(
                $"entity-{person.Id}",
                person.Id,
                matchResult.MatchConfidence,
                linkedRecords,
                matchResult.MatchReasons,
                matchResult.RequiresHumanReview);

            Entities.Add(entity);

            var score = CalculateRiskScore(person, entity);
            if (score.Score < 31)
            {
                continue;
            }

            var evidence = BuildEvidence(person);
            Cases.Add(new CaseItem(
                $"case-{person.Id}",
                entity.Id,
                person.Id,
                score.Score >= 81 ? "FlaggedForReview" : "Scored",
                score.Score >= 81 ? "Unassigned" : "Regional Queue",
                person.City,
                person.Province,
                score,
                evidence,
                DateTimeOffset.UtcNow.AddHours(-_random.Next(2, 72)),
                DateTimeOffset.UtcNow));

            AddTimeline($"case-{person.Id}", "CaseCreated", "RiskScoring.Worker", $"{score.RiskBand} case created with score {score.Score}/100.");
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

        return new DashboardSummary(
            People.Count,
            Cases.Count,
            Cases.Count(x => x.Score.RiskBand == "Critical"),
            Cases.Count(x => x.Score.RiskBand == "High"),
            Cases.Count(x => x.Score.RiskBand == "Medium"),
            Cases.Sum(EstimateRecoverableTax),
            0.93m,
            0.89m,
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

        var person = People.First(x => x.Id == entity.PersonId);
        var caseItem = Cases.FirstOrDefault(x => x.EntityId == entityId);
        var riskBand = caseItem?.Score.RiskBand ?? "Low";
        var nodes = new List<GraphNode>
        {
            new(entity.Id, "Person", person.FullName, riskBand, new Dictionary<string, object>
            {
                ["city"] = person.City,
                ["province"] = person.Province,
                ["cnicMasked"] = person.CnicMasked,
                ["matchConfidence"] = entity.MatchConfidence
            })
        };
        var edges = new List<GraphEdge>();

        foreach (var vehicle in Vehicles.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value))
        {
            AddNodeAndEdge(nodes, edges, entity.Id, $"vehicle-{vehicle.ProviderRecordId}", "Vehicle", $"{vehicle.Make} {vehicle.Model}", "OWNS_VEHICLE", 0.96m, new Dictionary<string, object>
            {
                ["engineCc"] = vehicle.EngineCc,
                ["estimatedValue"] = vehicle.EstimatedValue,
                ["registration"] = vehicle.RegistrationNumberMasked
            });
        }

        foreach (var property in Properties.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value))
        {
            AddNodeAndEdge(nodes, edges, entity.Id, $"property-{property.ProviderRecordId}", "Property", $"{property.Area}, {property.City}", "OWNS_PROPERTY", 0.92m, new Dictionary<string, object>
            {
                ["propertyType"] = property.PropertyType,
                ["estimatedValue"] = property.EstimatedValue
            });
        }

        foreach (var bill in UtilityBills.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value))
        {
            AddNodeAndEdge(nodes, edges, entity.Id, $"utility-{bill.ProviderRecordId}", "UtilityMeter", $"{bill.UtilityType} bill", "PAYS_UTILITY_BILL", 0.88m, new Dictionary<string, object>
            {
                ["averageMonthlyBill"] = bill.AverageMonthlyBill,
                ["latestBillAmount"] = bill.LatestBillAmount
            });
        }

        foreach (var business in Businesses.Where(x => x.RelatedIdentityToken.Value == person.IdentityToken.Value))
        {
            AddNodeAndEdge(nodes, edges, entity.Id, $"business-{business.ProviderRecordId}", "Business", business.CompanyName, "DIRECTOR_OF", 0.90m, new Dictionary<string, object>
            {
                ["registration"] = business.CompanyRegistrationNumber,
                ["status"] = business.Status
            });
        }

        foreach (var trip in Travel.Where(x => x.TravelerIdentityToken.Value == person.IdentityToken.Value))
        {
            AddNodeAndEdge(nodes, edges, entity.Id, $"travel-{trip.ProviderRecordId}", "TravelEvent", trip.Destination, "TRAVELLED_TO", 0.84m, new Dictionary<string, object>
            {
                ["tripsInLast24Months"] = trip.TripsInLast24Months,
                ["estimatedSpend"] = trip.EstimatedSpend
            });
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

    private string ResolvePostgresConnectionString(IConfiguration configuration)
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING"),
            Environment.GetEnvironmentVariable("ConnectionStrings__taxnet"),
            Environment.GetEnvironmentVariable("ConnectionStrings__postgres"),
            _platformOptions.Storage.PostgresConnectionString,
            configuration.GetConnectionString("taxnet"),
            configuration.GetConnectionString("postgres")
        };

        return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)) ?? "";
    }

}
