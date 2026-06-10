using System.Text.Json;

namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    private ObjectStoreItem StoreObject(string bucket, string key, string contentType, string content)
    {
        var path = Path.Combine(_objectRoot, bucket, key.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content ?? "");
        var item = new ObjectStoreItem(
            $"s3://{bucket}/{key}",
            bucket,
            key,
            contentType,
            System.Text.Encoding.UTF8.GetByteCount(content ?? ""),
            DateTimeOffset.UtcNow);
        ObjectStore.Insert(0, item);
        return item;
    }

    private bool LoadSnapshot()
    {
        if (!File.Exists(_statePath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(_statePath);
            var snapshot = JsonSerializer.Deserialize<TaxNetSnapshot>(json, _jsonOptions);
            if (snapshot is null)
            {
                return false;
            }

            People.AddRange(snapshot.People);
            TaxProfiles.AddRange(snapshot.TaxProfiles);
            Vehicles.AddRange(snapshot.Vehicles);
            Properties.AddRange(snapshot.Properties);
            UtilityBills.AddRange(snapshot.UtilityBills);
            Businesses.AddRange(snapshot.Businesses);
            Travel.AddRange(snapshot.Travel);
            Entities.AddRange(snapshot.Entities);
            Cases.AddRange(snapshot.Cases);
            Workers.AddRange(snapshot.Workers);
            Providers.AddRange(snapshot.Providers);
            RagDocuments.AddRange(snapshot.RagDocuments);
            RagChunks.AddRange(snapshot.RagChunks);
            DatasetBatches.AddRange(snapshot.DatasetBatches);
            ImportJobs.AddRange(snapshot.ImportJobs);
            TimelineEvents.AddRange(snapshot.TimelineEvents);
            Reports.AddRange(snapshot.Reports);
            ModelInvocations.AddRange(snapshot.ModelInvocations);
            AuditEvents.AddRange(snapshot.AuditEvents);
            Notifications.AddRange(snapshot.Notifications);
            ObjectStore.AddRange(snapshot.ObjectStore);
            _corrections.AddRange(snapshot.Corrections);
            foreach (var item in snapshot.ProviderConfigs)
            {
                ProviderConfigs[item.Key] = item.Value;
            }

            if (Workers.Count == 0)
            {
                SeedWorkers();
            }

            return People.Count > 0 && Cases.Count > 0;
        }
        catch (Exception ex)
        {
            var corruptPath = Path.Combine(_dataRoot, $"taxnet-state.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
            File.Move(_statePath, corruptPath, overwrite: true);
            File.WriteAllText(Path.Combine(_dataRoot, "last-load-error.txt"), ex.ToString());
            return false;
        }
    }

    private void SaveSnapshot()
    {
        Directory.CreateDirectory(_dataRoot);
        var snapshot = new TaxNetSnapshot
        {
            Version = 1,
            SavedAtUtc = DateTimeOffset.UtcNow,
            People = People.ToList(),
            TaxProfiles = TaxProfiles.ToList(),
            Vehicles = Vehicles.ToList(),
            Properties = Properties.ToList(),
            UtilityBills = UtilityBills.ToList(),
            Businesses = Businesses.ToList(),
            Travel = Travel.ToList(),
            Entities = Entities.ToList(),
            Cases = Cases.ToList(),
            Workers = Workers.ToList(),
            Providers = Providers.ToList(),
            RagDocuments = RagDocuments.ToList(),
            RagChunks = RagChunks.ToList(),
            DatasetBatches = DatasetBatches.ToList(),
            ImportJobs = ImportJobs.ToList(),
            TimelineEvents = TimelineEvents.ToList(),
            Reports = Reports.ToList(),
            ModelInvocations = ModelInvocations.ToList(),
            AuditEvents = AuditEvents.ToList(),
            Notifications = Notifications.ToList(),
            ObjectStore = ObjectStore.ToList(),
            Corrections = _corrections.ToList(),
            ProviderConfigs = new Dictionary<string, ProviderConfigUpdateRequest>(ProviderConfigs, StringComparer.OrdinalIgnoreCase)
        };

        var tempPath = _statePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
        File.Move(tempPath, _statePath, overwrite: true);
    }

    public object GetPersistenceStatus()
    {
        var stateInfo = File.Exists(_statePath) ? new FileInfo(_statePath) : null;
        return new
        {
            dataRoot = _dataRoot,
            statePath = _statePath,
            stateExists = stateInfo is not null,
            stateBytes = stateInfo?.Length ?? 0,
            objectRoot = _objectRoot,
            objectFiles = Directory.Exists(_objectRoot) ? Directory.GetFiles(_objectRoot, "*", SearchOption.AllDirectories).Length : 0,
            snapshotCollections = new
            {
                people = People.Count,
                cases = Cases.Count,
                ragDocuments = RagDocuments.Count,
                ragChunks = RagChunks.Count,
                reports = Reports.Count,
                auditEvents = AuditEvents.Count,
                objectMetadata = ObjectStore.Count
            }
        };
    }
}