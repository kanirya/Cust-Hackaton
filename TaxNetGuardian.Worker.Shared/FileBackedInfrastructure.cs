using System.Text.Json;

namespace TaxNetGuardian.Worker.Shared;

public sealed class FileBackedQueueClient(WorkerOptions options) : IQueueClient
{
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public Task SendAsync(string queueName, QueueEnvelope envelope, CancellationToken cancellationToken)
    {
        var directory = ReadyDirectory(queueName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
        return File.WriteAllTextAsync(path, JsonSerializer.Serialize(envelope, _jsonOptions), cancellationToken);
    }

    public async Task<IReadOnlyList<ReceivedQueueMessage>> ReceiveAsync(string queueName, int maxMessages, CancellationToken cancellationToken)
    {
        var ready = ReadyDirectory(queueName);
        var inflight = InflightDirectory(queueName);
        Directory.CreateDirectory(ready);
        Directory.CreateDirectory(inflight);

        var messages = new List<ReceivedQueueMessage>();
        foreach (var path in Directory.GetFiles(ready, "*.json").OrderBy(x => x).Take(maxMessages))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(inflight, Path.GetFileName(path));
            File.Move(path, target, overwrite: true);
            var json = await File.ReadAllTextAsync(target, cancellationToken);
            var envelope = JsonSerializer.Deserialize<QueueEnvelope>(json, _jsonOptions);
            if (envelope is not null)
            {
                messages.Add(new ReceivedQueueMessage(envelope, target));
            }
        }

        return messages;
    }

    public Task DeleteAsync(string queueName, string receiptHandle, CancellationToken cancellationToken)
    {
        if (File.Exists(receiptHandle))
        {
            File.Delete(receiptHandle);
        }

        return Task.CompletedTask;
    }

    private string ReadyDirectory(string queueName) => Path.Combine(options.DataRoot, "queues", queueName, "ready");
    private string InflightDirectory(string queueName) => Path.Combine(options.DataRoot, "queues", queueName, "inflight");
}

public sealed class FileBackedObjectStorageClient(WorkerOptions options) : IObjectStorageClient
{
    public async Task PutObjectAsync(string bucket, string key, string contentType, string content, CancellationToken cancellationToken)
    {
        var path = Path.Combine(options.DataRoot, "object-store", bucket, key.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content, cancellationToken);
        await File.WriteAllTextAsync(path + ".metadata.json", JsonSerializer.Serialize(new
        {
            bucket,
            key,
            contentType,
            storedAtUtc = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }), cancellationToken);
    }

    public async Task<string?> GetObjectAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        var path = Path.Combine(options.DataRoot, "object-store", bucket, key.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }
}
