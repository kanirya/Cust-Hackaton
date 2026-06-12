namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    private void SeedProviders()
    {
        Providers.AddRange([
            new("NADRA", "Identity Profile Provider", "Sandbox", "Healthy", 142, false, "/taxnet/dev/providers/nadra/credentials", "Healthy"),
            new("FBR", "Tax Profile Provider", "Sandbox", "Healthy", 188, true, "/taxnet/dev/providers/fbr/credentials", "Healthy"),
            new("EXCISE-PB", "Punjab Excise Vehicle Provider", "Sandbox", "Healthy", 220, false, "/taxnet/dev/providers/excise-pb/credentials", "Healthy"),
            new("SECP", "Company Registry Provider", "Public/Sandbox", "Healthy", 260, false, "/taxnet/dev/providers/secp/credentials", "Healthy"),
            new("PROPERTY", "Land and Property Provider", "Sandbox", "Degraded", 480, false, "/taxnet/dev/providers/property/credentials", "Degraded"),
            new("UTILITY", "Utility Consumption Provider", "Sandbox", "Healthy", 175, true, "/taxnet/dev/providers/utility/credentials", "Healthy"),
            new("TRAVEL", "Travel Signal Provider", "Sandbox", "Healthy", 205, false, "/taxnet/dev/providers/travel/credentials", "Healthy")
        ]);
    }

    private void SeedPolicyDocuments()
    {
        var seedDocs = new (RagDocument Document, string Content)[]
        {
            (new("rag-001", "Tax compliance review SOP", "AuditSop", "sandbox://policy/tax-compliance-review-sop", "Explains that AI-generated compliance deviations are prioritization signals requiring human review.", DateTimeOffset.UtcNow.AddDays(-2), ["audit", "human-review", "xai"]),
                "Audit officers must treat AI-generated compliance deviations as prioritization signals only. A case requires human review, source verification, evidence quality checks, and a citizen correction window before escalation. Reports must include a watermark, evidence identifiers, policy citations, and a warning that the score does not prove fraud."),
            (new("rag-002", "Vehicle and asset signal guidance", "ExciseSchedule", "sandbox://policy/vehicle-asset-signal", "Defines luxury/high-value vehicles and assets as review signals when declared income is materially lower.", DateTimeOffset.UtcNow.AddDays(-3), ["vehicle", "asset", "income-mismatch"]),
                "Luxury vehicles, high engine capacity registrations, and high-value residential or commercial property can be reviewed when materially inconsistent with declared income. Asset signals must be correlated with ownership dates, filing behavior, utility consumption, and business relationships before any enforcement recommendation."),
            (new("rag-003", "Citizen correction and appeal policy", "CitizenRights", "sandbox://policy/citizen-correction", "Requires a transparent correction path for mismatched or stale civic records.", DateTimeOffset.UtcNow.AddDays(-1), ["citizen", "correction", "fairness"]),
                "Citizens must receive a safe explanation that avoids investigative tactics and does not expose peer comparisons. Correction submissions should allow stale ownership, outdated utility records, incorrect business links, and already-declared income evidence. Human reviewers must consider correction evidence before closure."),
            (new("rag-004", "Business ownership income review note", "TaxNotice", "sandbox://policy/business-ownership-income", "Business directorship or ownership should be compared with filing behavior and declared income.", DateTimeOffset.UtcNow.AddDays(-5), ["business", "income", "filer-status"]),
                "Business directorship, shareholder, and partnership relationships should be compared with declared income, tax paid, filer status, and asset purchases. A business relationship alone is not a violation; it is a review signal when paired with undeclared or materially inconsistent economic activity.")
        };

        foreach (var (document, content) in seedDocs)
        {
            RagDocuments.Add(document);
            IndexRagDocument(document, content);
            StoreObject("taxnet-dev-raw-source-snapshots", $"rag-source/{document.Id}.txt", "text/plain", content);
        }
    }

    private void SeedKnownDemoProfiles()
    {
        AddProfile("P001", "Muhammad Ali Khan", "محمد علی خان", "Rashid Khan", "Lahore", "Punjab", "Critical", 0, 0, "Non-Filer", [("Toyota", "Prado", 2700, 28_000_000m)], [("DHA Phase 6", "Residential", 45_000_000m)], 310_000m, [("ABC Traders Pvt Ltd", "Director")], 5, 3_500_000m);
        AddProfile("P002", "Ayesha Siddiqui", "عائشہ صدیقی", "Hameed Siddiqui", "Islamabad", "ICT", "Low", 18_000_000m, 2_400_000m, "Active Filer", [("Honda", "Civic", 1800, 8_500_000m)], [], 68_000m, [], 1, 450_000m);
        AddProfile("P003", "Bilal Ahmed", "بلال احمد", "Nadeem Ahmed", "Karachi", "Sindh", "High", 900_000m, 24_000m, "Active Filer", [("BMW", "X5", 3000, 38_000_000m)], [("Clifton Block 8", "Apartment", 62_000_000m)], 180_000m, [("North Star Imports", "Director")], 3, 1_900_000m);
        AddProfile("P004", "M. Ali Khan", "ایم علی خان", "Rashid Khan", "Lahore", "Punjab", "Critical", 0, 0, "Zero Return", [("Mercedes", "E200", 2000, 29_000_000m)], [("Gulberg", "Commercial", 88_000_000m)], 260_000m, [("Khan Holdings", "Shareholder")], 2, 1_400_000m);
        AddProfile("P005", "Fatima Noor", "فاطمہ نور", "Tariq Noor", "Faisalabad", "Punjab", "Medium", 1_800_000m, 90_000m, "Active Filer", [("Toyota", "Fortuner", 2700, 19_000_000m)], [], 72_000m, [], 0, 0m);
        AddProfile("P006", "Usman Qureshi", "عثمان قریشی", "Javed Qureshi", "Multan", "Punjab", "High", 600_000m, 12_000m, "Late Filer", [], [("Bosan Road", "Residential", 31_000_000m)], 145_000m, [("Qureshi Textiles", "Director")], 4, 2_100_000m);
        AddProfile("P007", "Sara Iqbal", "سارہ اقبال", "Iqbal Hussain", "Peshawar", "KPK", "Low", 7_500_000m, 890_000m, "Active Filer", [("Kia", "Sportage", 2000, 9_000_000m)], [("University Road", "Residential", 18_000_000m)], 55_000m, [], 1, 300_000m);
        AddProfile("P008", "Hassan Raza", "حسن رضا", "Raza Ali", "Quetta", "Balochistan", "Medium", 1_200_000m, 45_000m, "Active Filer", [("Audi", "A4", 2000, 17_500_000m)], [], 92_000m, [("Raza Logistics", "Partner")], 2, 700_000m);
    }

    private void SeedGeneratedProfiles(int count, int suspiciousPercent, int noisePercent)
    {
        var firstNames = new[] { "Ahmed", "Zain", "Hamza", "Danish", "Shahbaz", "Mariam", "Hira", "Sana", "Nimra", "Kashif", "Taimoor", "Rabia", "Noman", "Sadia" };
        var lastNames = new[] { "Khan", "Malik", "Sheikh", "Butt", "Rana", "Abbasi", "Javed", "Akhtar", "Mirza", "Chaudhry", "Gill", "Dar" };
        var cities = new[] { ("Lahore", "Punjab"), ("Karachi", "Sindh"), ("Islamabad", "ICT"), ("Faisalabad", "Punjab"), ("Rawalpindi", "Punjab"), ("Peshawar", "KPK"), ("Multan", "Punjab"), ("Hyderabad", "Sindh") };
        var vehicles = new[] { ("Suzuki", "Alto", 660, 2_900_000m), ("Toyota", "Corolla", 1800, 6_700_000m), ("Honda", "Civic", 1800, 8_700_000m), ("Toyota", "Fortuner", 2700, 18_500_000m), ("Mercedes", "C180", 1600, 23_000_000m), ("Toyota", "Land Cruiser", 4600, 90_000_000m) };

        for (var i = 0; i < count; i++)
        {
            var suspicious = i < count * suspiciousPercent / 100;
            var noisy = _random.Next(100) < noisePercent;
            var id = $"G{(i + 1):000}";
            var name = $"{firstNames[_random.Next(firstNames.Length)]} {lastNames[_random.Next(lastNames.Length)]}";
            if (noisy && _random.Next(2) == 0)
            {
                name = name.Replace("Ahmed", "A.").Replace("Muhammad", "M.");
            }

            var city = cities[_random.Next(cities.Length)];
            var income = suspicious ? _random.Next(0, 1_200_000) : _random.Next(1_800_000, 16_000_000);
            var tax = suspicious ? income * 0.02m : income * 0.11m;
            var filer = suspicious && _random.Next(2) == 0 ? "Non-Filer" : "Active Filer";
            var selectedVehicle = vehicles[_random.Next(vehicles.Length)];
            var highAsset = suspicious && _random.Next(100) < 70;
            var profileVehicles = highAsset ? [selectedVehicle] : _random.Next(100) < 55 ? [vehicles[_random.Next(0, 3)]] : Array.Empty<(string Make, string Model, int EngineCc, decimal Value)>();
            var profileProperties = suspicious && _random.Next(100) < 45
                ? [($"{city.Item1} Prime Zone", "Residential", _random.Next(18, 75) * 1_000_000m)]
                : Array.Empty<(string Area, string Type, decimal Value)>();
            var utility = suspicious ? _random.Next(80_000, 260_000) : _random.Next(18_000, 85_000);
            var businesses = suspicious && _random.Next(100) < 35
                ? [($"{lastNames[_random.Next(lastNames.Length)]} Enterprises", "Director")]
                : Array.Empty<(string CompanyName, string Relation)>();
            var trips = suspicious ? _random.Next(1, 6) : _random.Next(0, 2);
            var travelSpend = trips * _random.Next(180_000, 850_000);

            AddProfile(id, name, "", $"{lastNames[_random.Next(lastNames.Length)]} Sr.", city.Item1, city.Item2, suspicious ? "High" : "Low", income, tax, filer, profileVehicles, profileProperties, utility, businesses, trips, travelSpend);
        }
    }

    private void AddProfile(
        string id,
        string name,
        string urduName,
        string fatherName,
        string city,
        string province,
        string expectedRisk,
        decimal declaredIncome,
        decimal taxPaid,
        string filerStatus,
        IReadOnlyList<(string Make, string Model, int EngineCc, decimal Value)> vehicles,
        IReadOnlyList<(string Area, string Type, decimal Value)> properties,
        decimal averageUtilityBill,
        IReadOnlyList<(string CompanyName, string Relation)> businesses,
        int trips,
        decimal travelSpend)
    {
        var token = new IdentityToken($"idtk-{id.ToLowerInvariant()}", "SyntheticCnicHash", "GovDataSandbox", true);
        var now = DateTimeOffset.UtcNow;

        People.Add(new SyntheticPerson(id, name, urduName, fatherName, city, province, $"42201-***{id[^2..]}", $"+92-3**-***{id[^2..]}", token, expectedRisk));
        TaxProfiles.Add(new TaxProfile($"tax-{id}", token, $"NTN-{id}", filerStatus, declaredIncome, taxPaid, 2025, now.AddDays(-_random.Next(1, 120))));

        var index = 1;
        foreach (var vehicle in vehicles)
        {
            Vehicles.Add(new VehicleRecord($"veh-{id}-{index}", token, $"LE{_random.Next(100, 999)}-**{index}", vehicle.Make, vehicle.Model, vehicle.EngineCc, _random.Next(2018, 2026), vehicle.Value, province, now.AddDays(-_random.Next(1, 180))));
            index++;
        }

        index = 1;
        foreach (var property in properties)
        {
            Properties.Add(new PropertyRecord($"prop-{id}-{index}", token, $"plot-{id}-{index}", city, property.Area, property.Type, property.Value, now.AddDays(-_random.Next(1, 240))));
            index++;
        }

        UtilityBills.Add(new UtilityBillRecord($"util-{id}-1", token, $"meter-{id}", "Electricity", averageUtilityBill, averageUtilityBill + _random.Next(-12_000, 18_000), city, now.AddDays(-_random.Next(1, 30))));

        index = 1;
        foreach (var business in businesses)
        {
            Businesses.Add(new BusinessRecord($"biz-{id}-{index}", $"SECP-{id}-{index}", business.CompanyName, business.Relation, token, "Active", now.AddDays(-_random.Next(1, 365))));
            index++;
        }

        if (trips > 0)
        {
            Travel.Add(new TravelRecord($"travel-{id}-1", token, "UAE / Turkey / UK", trips, travelSpend, now.AddDays(-_random.Next(1, 300))));
        }
    }

    private RiskScore CalculateRiskScore(SyntheticPerson person, ResolvedEntity entity)
    {
        var tax = TaxProfiles.FirstOrDefault(x => x.IdentityToken.Value == person.IdentityToken.Value)
            ?? new TaxProfile($"synthetic-tax-{person.Id}", person.IdentityToken, $"NTN-{person.Id}", "Unknown", 0m, 0m, DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow);
        var vehicles = Vehicles.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value).ToArray();
        var properties = Properties.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value).ToArray();
        var utility = UtilityBills.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value).ToArray();
        var businesses = Businesses.Where(x => x.RelatedIdentityToken.Value == person.IdentityToken.Value).ToArray();
        var travel = Travel.Where(x => x.TravelerIdentityToken.Value == person.IdentityToken.Value).ToArray();

        var income = Math.Max(tax.DeclaredAnnualIncome, 1);
        var totalAssets = vehicles.Sum(x => x.EstimatedValue) + properties.Sum(x => x.EstimatedValue);
        var avgUtility = utility.Any() ? utility.Average(x => x.AverageMonthlyBill) : 0m;
        var maxEngineCc = vehicles.Any() ? vehicles.Max(x => x.EngineCc) : 0;
        var travelSpend = travel.Sum(x => x.EstimatedSpend);

        var assetScore = ClampScore((totalAssets / income) switch
        {
            > 40 => 25,
            > 20 => 21,
            > 10 => 16,
            > 5 => 9,
            _ => 0
        }, 25);

        var utilityScore = ClampScore((avgUtility / (income / 12m)) switch
        {
            > 2.5m => 20,
            > 1.5m => 16,
            > 0.8m => 10,
            _ => 0
        }, 20);

        var vehicleScore = ClampScore(maxEngineCc switch
        {
            >= 2700 => 15,
            >= 2000 => 11,
            >= 1800 => 7,
            _ => 0
        }, 15);

        var propertyScore = ClampScore((properties.Sum(x => x.EstimatedValue) / income) switch
        {
            > 35 => 15,
            > 18 => 11,
            > 7 => 6,
            _ => 0
        }, 15);

        var businessScore = ClampScore(businesses.Length switch
        {
            >= 2 when tax.DeclaredAnnualIncome < 2_000_000 => 10,
            >= 1 when tax.DeclaredAnnualIncome < 1_500_000 => 8,
            >= 1 => 4,
            _ => 0
        }, 10);

        var travelScore = ClampScore((travelSpend / income) switch
        {
            > 3 => 10,
            > 1.2m => 7,
            > 0.6m => 4,
            _ => 0
        }, 10);

        var filingScore = ClampScore(tax.FilerStatus switch
        {
            "Non-Filer" => 5,
            "Zero Return" => 5,
            "Late Filer" => 3,
            _ => 0
        }, 5);

        var components = new List<RiskScoreComponent>
        {
            new("AssetIncomeMismatch", assetScore, 25, EvidenceIds(person, "vehicle", "property"), $"Observed assets total PKR {totalAssets:N0} against declared annual income PKR {tax.DeclaredAnnualIncome:N0}."),
            new("UtilityIncomeMismatch", utilityScore, 20, EvidenceIds(person, "utility"), $"Average monthly utility bill is PKR {avgUtility:N0}."),
            new("VehicleLuxurySignal", vehicleScore, 15, EvidenceIds(person, "vehicle"), maxEngineCc > 0 ? $"Largest linked vehicle engine capacity is {maxEngineCc}cc." : "No vehicle signal."),
            new("PropertyOwnershipMismatch", propertyScore, 15, EvidenceIds(person, "property"), $"Linked property value is PKR {properties.Sum(x => x.EstimatedValue):N0}."),
            new("BusinessOwnershipMismatch", businessScore, 10, EvidenceIds(person, "business"), $"{businesses.Length} active business relationship(s) linked."),
            new("TravelLifestyleMismatch", travelScore, 10, EvidenceIds(person, "travel"), $"Travel spend signal is PKR {travelSpend:N0}."),
            new("FilingBehaviorSignal", filingScore, 5, EvidenceIds(person, "tax"), $"Latest filing status is {tax.FilerStatus}.")
        };

        var total = Math.Clamp(components.Sum(x => x.Score), 0, 100);
        var band = total >= 81 ? "Critical" : total >= 61 ? "High" : total >= 31 ? "Medium" : "Low";
        var confidence = Math.Min(0.98m, entity.MatchConfidence - (entity.RequiresHumanReview ? 0.08m : 0m) + (components.Count(x => x.Score > 0) * 0.01m));

        return new RiskScore(
            $"case-{person.Id}",
            entity.Id,
            total,
            band,
            decimal.Round(confidence, 2),
            components,
            total >= 81 ? "HumanReviewUrgent" : total >= 61 ? "HumanReview" : total >= 31 ? "MonitorAndRequestClarification" : "NoAction",
            "risk-score-v1.0");
    }

    private IReadOnlyList<EvidenceItem> BuildEvidence(SyntheticPerson person)
    {
        var evidence = new List<EvidenceItem>();
        var tax = TaxProfiles.FirstOrDefault(x => x.IdentityToken.Value == person.IdentityToken.Value);
        if (tax is not null)
        {
            evidence.Add(new EvidenceItem($"ev-tax-{person.Id}", "TaxReturn", "Latest tax profile", $"{tax.FilerStatus}; declared annual income PKR {tax.DeclaredAnnualIncome:N0}; tax paid PKR {tax.TaxPaid:N0}.", tax.DeclaredAnnualIncome, "FBR Sandbox", tax.SourceUpdatedAtUtc));
        }

        evidence.AddRange(Vehicles.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value)
            .Select(x => new EvidenceItem($"ev-{x.ProviderRecordId}", "Vehicle", $"{x.Make} {x.Model}", $"{x.EngineCc}cc vehicle estimated at PKR {x.EstimatedValue:N0}.", x.EstimatedValue, "Excise Sandbox", x.SourceUpdatedAtUtc)));

        evidence.AddRange(Properties.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value)
            .Select(x => new EvidenceItem($"ev-{x.ProviderRecordId}", "Property", $"{x.PropertyType} property", $"{x.Area}, {x.City}; estimated value PKR {x.EstimatedValue:N0}.", x.EstimatedValue, "Property Sandbox", x.SourceUpdatedAtUtc)));

        evidence.AddRange(UtilityBills.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value)
            .Select(x => new EvidenceItem($"ev-{x.ProviderRecordId}", "Utility", $"{x.UtilityType} usage", $"Average monthly bill PKR {x.AverageMonthlyBill:N0}; latest bill PKR {x.LatestBillAmount:N0}.", x.AverageMonthlyBill, "Utility Sandbox", x.SourceUpdatedAtUtc)));

        evidence.AddRange(Businesses.Where(x => x.RelatedIdentityToken.Value == person.IdentityToken.Value)
            .Select(x => new EvidenceItem($"ev-{x.ProviderRecordId}", "Business", x.CompanyName, $"{x.RelationshipType} relationship; status {x.Status}.", null, "SECP Sandbox", x.SourceUpdatedAtUtc)));

        evidence.AddRange(Travel.Where(x => x.TravelerIdentityToken.Value == person.IdentityToken.Value)
            .Select(x => new EvidenceItem($"ev-{x.ProviderRecordId}", "Travel", x.Destination, $"{x.TripsInLast24Months} trip(s) in last 24 months; estimated spend PKR {x.EstimatedSpend:N0}.", x.EstimatedSpend, "Travel Sandbox", x.SourceUpdatedAtUtc)));

        return evidence;
    }

    private IReadOnlyList<PolicyCitation> BuildCitations(IReadOnlyList<string> componentNames)
    {
        var citations = new List<PolicyCitation>();

        if (componentNames.Any(x => x.Contains("Vehicle", StringComparison.OrdinalIgnoreCase) || x.Contains("Asset", StringComparison.OrdinalIgnoreCase)))
        {
            citations.Add(ToCitation(RagDocuments.First(x => x.Id == "rag-002"), "chunk-vehicle-001"));
        }

        if (componentNames.Any(x => x.Contains("Business", StringComparison.OrdinalIgnoreCase)))
        {
            citations.Add(ToCitation(RagDocuments.First(x => x.Id == "rag-004"), "chunk-business-001"));
        }

        citations.Add(ToCitation(RagDocuments.First(x => x.Id == "rag-001"), "chunk-review-001"));
        citations.Add(ToCitation(RagDocuments.First(x => x.Id == "rag-003"), "chunk-citizen-001"));
        return citations.DistinctBy(x => x.Title).ToArray();
    }

    private IReadOnlyList<string> BuildMatchReasons(SyntheticPerson person, int recordCount, decimal confidence)
    {
        var reasons = new List<string>
        {
            "Synthetic identity token matched across provider records.",
            $"{recordCount} records linked from tax, asset, utility, business, or travel domains.",
            $"Name/address normalization matched profile in {person.City}.",
            $"Entity resolution confidence: {confidence:P0}."
        };

        if (person.FullName.Contains('.') || !string.IsNullOrWhiteSpace(person.UrduName))
        {
            reasons.Add("Alias/Urdu-English name variant handled by normalization layer.");
        }

        return reasons;
    }

    private IReadOnlyList<string> EvidenceIds(SyntheticPerson person, string type1, string? type2 = null)
    {
        return BuildEvidence(person)
            .Where(x => x.Type.Contains(type1, StringComparison.OrdinalIgnoreCase) ||
                        (type2 is not null && x.Type.Contains(type2, StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.Id)
            .ToArray();
    }

    private void SeedWorkers()
    {
        Workers.Clear();
        Workers.AddRange([
            new("Ingestion.Worker", "taxnet-dev-ingestion-jobs", 0, 9, 0, "Healthy", DateTimeOffset.UtcNow.AddMinutes(-6)),
            new("IdentityResolution.Worker", "taxnet-dev-identity-resolution-jobs", 0, People.Count, 1, "Healthy", DateTimeOffset.UtcNow.AddMinutes(-5)),
            new("GraphIntelligence.Worker", "taxnet-dev-graph-build-jobs", 0, Entities.Count, 0, "Healthy", DateTimeOffset.UtcNow.AddMinutes(-4)),
            new("RiskScoring.Worker", "taxnet-dev-risk-score-jobs", 0, Entities.Count, 0, "Healthy", DateTimeOffset.UtcNow.AddMinutes(-3)),
            new("RagPolicy.Worker", "taxnet-dev-rag-index-jobs", 0, RagDocuments.Count, 0, "Healthy", DateTimeOffset.UtcNow.AddMinutes(-2)),
            new("PublicDataConnector.Worker", "taxnet-dev-public-data-connector-jobs", 0, 0, 0, "Idle", DateTimeOffset.UtcNow),
            new("Notification.Worker", "taxnet-dev-notification-jobs", 0, 0, 0, "Idle", DateTimeOffset.UtcNow),
            new("Report.Worker", "taxnet-dev-report-jobs", Math.Min(1, Reports.Count), 4 + Reports.Count, 0, Reports.Count > 0 ? "Healthy" : "Idle", DateTimeOffset.UtcNow.AddMinutes(-20)),
            new("AuditLog.Worker", "taxnet-dev-audit-log-jobs", 0, 480 + TimelineEvents.Count, 0, "Healthy", DateTimeOffset.UtcNow.AddMinutes(-1))
        ]);
    }
}