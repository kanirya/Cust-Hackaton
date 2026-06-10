using System.Globalization;
using System.Text.Json;

namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    public IReadOnlyList<DatasetTemplate> GetDatasetTemplates()
    {
        return
        [
            new("identity", "Creates or updates synthetic identity profiles.", ["personId", "fullName", "urduName", "fatherName", "city", "province", "cnicMasked", "phoneMasked"],
                "personId,fullName,urduName,fatherName,city,province,cnicMasked,phoneMasked\nEXT001,Adnan Farooq,,Farooq Ahmed,Lahore,Punjab,42201-***91,+92-3**-***91"),
            new("tax", "Feeds FBR-style tax profile records.", ["personId", "ntn", "filerStatus", "declaredAnnualIncome", "taxPaid", "taxYear"],
                "personId,ntn,filerStatus,declaredAnnualIncome,taxPaid,taxYear\nEXT001,NTN-EXT001,Non-Filer,0,0,2025"),
            new("vehicle", "Feeds Excise-style vehicle ownership records.", ["personId", "registrationNumberMasked", "make", "model", "engineCc", "modelYear", "estimatedValue", "province"],
                "personId,registrationNumberMasked,make,model,engineCc,modelYear,estimatedValue,province\nEXT001,LE991-**1,Toyota,Prado,2700,2023,28000000,Punjab"),
            new("property", "Feeds land/property ownership records.", ["personId", "propertyToken", "city", "area", "propertyType", "estimatedValue"],
                "personId,propertyToken,city,area,propertyType,estimatedValue\nEXT001,PLOT-EXT001,Lahore,DHA Phase 6,Residential,45000000"),
            new("utility", "Feeds monthly utility consumption signals.", ["personId", "meterToken", "utilityType", "averageMonthlyBill", "latestBillAmount", "city"],
                "personId,meterToken,utilityType,averageMonthlyBill,latestBillAmount,city\nEXT001,METER-EXT001,Electricity,310000,305000,Lahore"),
            new("business", "Feeds SECP-style company relationship records.", ["personId", "companyRegistrationNumber", "companyName", "relationshipType", "status"],
                "personId,companyRegistrationNumber,companyName,relationshipType,status\nEXT001,SECP-EXT001,Farooq Holdings,Director,Active"),
            new("travel", "Feeds travel/lifestyle spending signals.", ["personId", "destination", "tripsInLast24Months", "estimatedSpend"],
                "personId,destination,tripsInLast24Months,estimatedSpend\nEXT001,UAE / Turkey / UK,5,3500000")
        ];
    }

    public DatasetBatch FeedDataset(DatasetFeedRequest request)
    {
        lock (_lock)
        {
            var warnings = new List<string>();
            var rows = ParseRows(request, warnings);
            var batch = new DatasetBatch(
                $"ds-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{DatasetBatches.Count + 1:000}",
                string.IsNullOrWhiteSpace(request.DatasetType) ? "identity" : request.DatasetType.Trim().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(request.Format) ? "csv" : request.Format.Trim().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(request.FileName) ? "inline-dataset" : request.FileName,
                rows.Count,
                "Received",
                DateTimeOffset.UtcNow,
                warnings);

            var job = ApplyDataset(batch, rows);
            DatasetBatches.Insert(0, batch with { Status = job.Status });
            ImportJobs.Insert(0, job);

            if (request.RunPipeline)
            {
                RebuildIntelligence();
            }

            SaveSnapshot();
            return batch with { Status = job.Status };
        }
    }

    private IReadOnlyList<Dictionary<string, string>> ParseRows(DatasetFeedRequest request, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            warnings.Add("Dataset content was empty.");
            return [];
        }

        var format = string.IsNullOrWhiteSpace(request.Format) ? "csv" : request.Format.Trim().ToLowerInvariant();
        if (format is "json")
        {
            try
            {
                using var doc = JsonDocument.Parse(request.Content);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var records))
                {
                    root = records;
                }

                if (root.ValueKind != JsonValueKind.Array)
                {
                    warnings.Add("JSON feed must be an array or an object with a records array.");
                    return [];
                }

                return root.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.Object)
                    .Select(element => element.EnumerateObject().ToDictionary(
                        p => p.Name,
                        p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString(),
                        StringComparer.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch (JsonException ex)
            {
                warnings.Add($"JSON parse failed: {ex.Message}");
                return [];
            }
        }

        var lines = request.Content
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length < 2)
        {
            warnings.Add("CSV feed must include a header row and at least one data row.");
            return [];
        }

        var headers = SplitCsvLine(lines[0]);
        var rows = new List<Dictionary<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            var values = SplitCsvLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
            {
                row[headers[i]] = i < values.Count ? values[i] : "";
            }
            rows.Add(row);
        }

        return rows;
    }

    private ImportJob ApplyDataset(DatasetBatch batch, IReadOnlyList<Dictionary<string, string>> rows)
    {
        var created = 0;
        var failed = 0;
        var messages = new List<string>();
        var started = DateTimeOffset.UtcNow;

        foreach (var row in rows)
        {
            try
            {
                ApplyRow(batch.DatasetType, row, batch.Id);
                created++;
            }
            catch (Exception ex)
            {
                failed++;
                messages.Add($"Row {created + failed}: {ex.Message}");
            }
        }

        var status = failed == 0 ? "Succeeded" : created > 0 ? "SucceededWithWarnings" : "Failed";
        messages.Insert(0, $"Applied {created} {batch.DatasetType} record(s) from {batch.FileName}.");

        return new ImportJob(
            $"job-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{ImportJobs.Count + 1:000}",
            $"Dataset:{batch.DatasetType}",
            status,
            batch.FileName,
            rows.Count,
            created,
            failed,
            started,
            DateTimeOffset.UtcNow,
            messages);
    }

    private void ApplyRow(string datasetType, IReadOnlyDictionary<string, string> row, string batchId)
    {
        var personId = Get(row, "personId", "id", "syntheticPersonId");
        if (string.IsNullOrWhiteSpace(personId))
        {
            throw new InvalidOperationException("Missing personId.");
        }

        var person = EnsurePerson(personId, row);
        var token = person.IdentityToken;
        var now = DateTimeOffset.UtcNow;

        switch (datasetType)
        {
            case "identity":
                return;
            case "tax":
                TaxProfiles.RemoveAll(x => x.ProviderRecordId == $"feed-tax-{personId}");
                TaxProfiles.Add(new TaxProfile(
                    $"feed-tax-{personId}",
                    token,
                    Get(row, "ntn") is { Length: > 0 } ntn ? ntn : $"NTN-{personId}",
                    Get(row, "filerStatus", "status") is { Length: > 0 } status ? status : "Active Filer",
                    GetDecimal(row, "declaredAnnualIncome", "income"),
                    GetDecimal(row, "taxPaid"),
                    GetInt(row, "taxYear", defaultValue: 2025),
                    now));
                return;
            case "vehicle":
                Vehicles.Add(new VehicleRecord(
                    $"feed-veh-{personId}-{Vehicles.Count(x => x.OwnerIdentityToken.Value == token.Value) + 1}",
                    token,
                    Get(row, "registrationNumberMasked", "registration") is { Length: > 0 } reg ? reg : $"FEED-{personId}",
                    Get(row, "make") is { Length: > 0 } make ? make : "Unknown",
                    Get(row, "model") is { Length: > 0 } model ? model : "Vehicle",
                    GetInt(row, "engineCc", "cc", defaultValue: 1300),
                    GetInt(row, "modelYear", defaultValue: 2024),
                    GetDecimal(row, "estimatedValue", "value"),
                    Get(row, "province") is { Length: > 0 } province ? province : person.Province,
                    now));
                return;
            case "property":
                Properties.Add(new PropertyRecord(
                    $"feed-prop-{personId}-{Properties.Count(x => x.OwnerIdentityToken.Value == token.Value) + 1}",
                    token,
                    Get(row, "propertyToken") is { Length: > 0 } propertyToken ? propertyToken : $"prop-{batchId}-{personId}",
                    Get(row, "city") is { Length: > 0 } city ? city : person.City,
                    Get(row, "area") is { Length: > 0 } area ? area : "Unspecified Area",
                    Get(row, "propertyType", "type") is { Length: > 0 } propertyType ? propertyType : "Residential",
                    GetDecimal(row, "estimatedValue", "value"),
                    now));
                return;
            case "utility":
                UtilityBills.Add(new UtilityBillRecord(
                    $"feed-util-{personId}-{UtilityBills.Count(x => x.OwnerIdentityToken.Value == token.Value) + 1}",
                    token,
                    Get(row, "meterToken", "meterNumber") is { Length: > 0 } meter ? meter : $"meter-{batchId}-{personId}",
                    Get(row, "utilityType") is { Length: > 0 } utilityType ? utilityType : "Electricity",
                    GetDecimal(row, "averageMonthlyBill", "avgMonthlyBill"),
                    GetDecimal(row, "latestBillAmount", "latestBill"),
                    Get(row, "city") is { Length: > 0 } utilityCity ? utilityCity : person.City,
                    now));
                return;
            case "business":
                Businesses.Add(new BusinessRecord(
                    $"feed-biz-{personId}-{Businesses.Count(x => x.RelatedIdentityToken.Value == token.Value) + 1}",
                    Get(row, "companyRegistrationNumber", "registration") is { Length: > 0 } companyReg ? companyReg : $"SECP-{batchId}-{personId}",
                    Get(row, "companyName") is { Length: > 0 } company ? company : $"{person.FullName} Enterprises",
                    Get(row, "relationshipType", "relation") is { Length: > 0 } relation ? relation : "Director",
                    token,
                    Get(row, "status") is { Length: > 0 } bizStatus ? bizStatus : "Active",
                    now));
                return;
            case "travel":
                Travel.Add(new TravelRecord(
                    $"feed-travel-{personId}-{Travel.Count(x => x.TravelerIdentityToken.Value == token.Value) + 1}",
                    token,
                    Get(row, "destination") is { Length: > 0 } destination ? destination : "International",
                    GetInt(row, "tripsInLast24Months", "trips", defaultValue: 1),
                    GetDecimal(row, "estimatedSpend", "spend"),
                    now));
                return;
            default:
                throw new InvalidOperationException($"Unsupported dataset type '{datasetType}'.");
        }
    }

    private SyntheticPerson EnsurePerson(string personId, IReadOnlyDictionary<string, string> row)
    {
        var existing = People.FirstOrDefault(x => x.Id.Equals(personId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var token = new IdentityToken($"idtk-{personId.ToLowerInvariant()}", "SyntheticCnicHash", "GovDataSandboxFeed", true);
        var suffix = personId.Length <= 2 ? personId : personId[^2..];
        var person = new SyntheticPerson(
            personId,
            Get(row, "fullName", "name") is { Length: > 0 } name ? name : $"Fed Entity {personId}",
            Get(row, "urduName"),
            Get(row, "fatherName") is { Length: > 0 } fatherName ? fatherName : "Unknown",
            Get(row, "city") is { Length: > 0 } city ? city : "Lahore",
            Get(row, "province") is { Length: > 0 } province ? province : "Punjab",
            Get(row, "cnicMasked") is { Length: > 0 } cnic ? cnic : $"42201-***{suffix}",
            Get(row, "phoneMasked") is { Length: > 0 } phone ? phone : "+92-3**-***00",
            token,
            Get(row, "expectedRiskBand") is { Length: > 0 } risk ? risk : "Unlabeled");

        People.Add(person);
        return person;
    }

    private static string Get(IReadOnlyDictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static decimal GetDecimal(IReadOnlyDictionary<string, string> row, params string[] keys)
    {
        var value = Get(row, keys);
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    private static int GetInt(IReadOnlyDictionary<string, string> row, string key, string? alternate = null, int defaultValue = 0)
    {
        var value = alternate is null ? Get(row, key) : Get(row, key, alternate);
        return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : defaultValue;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        values.Add(current.ToString().Trim());
        return values;
    }
}
