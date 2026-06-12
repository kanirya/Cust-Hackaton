using System.Text.Json;

namespace TaxNetGuardian.Worker.Shared;

public static class WorkerHost
{
    public static async Task<int> RunAsync(WorkerOptions options, IWorkerJobHandler handler, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient();
        IQueueClient queue = options.QueueMode.Equals("LocalStack", StringComparison.OrdinalIgnoreCase)
            ? new LocalStackSqsQueueClient(options, http)
            : new FileBackedQueueClient(options);
        IObjectStorageClient objects = options.ObjectStoreMode.Equals("LocalStack", StringComparison.OrdinalIgnoreCase)
            ? new LocalStackS3ObjectStorageClient(options, http)
            : new FileBackedObjectStorageClient(options);
        var context = new WorkerContext(options, queue, objects, http);

        Console.WriteLine($"{options.WorkerName} started. queue={options.QueueName} queueMode={options.QueueMode} objectStore={options.ObjectStoreMode}");
        var runId = $"worker-run-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        var startedAtUtc = DateTimeOffset.UtcNow;
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        do
        {
            var messages = await queue.ReceiveAsync(options.QueueName, options.MaxMessages, cancellationToken);
            if (messages.Count == 0)
            {
                Console.WriteLine($"{options.WorkerName}: no messages.");
            }

            foreach (var message in messages)
            {
                processed++;
                var messageStartedAtUtc = DateTimeOffset.UtcNow;
                try
                {
                    Console.WriteLine($"{options.WorkerName}: handling {message.Envelope.Type} {message.Envelope.Id}");
                    await handler.HandleAsync(message.Envelope, context, cancellationToken);
                    await queue.DeleteAsync(options.QueueName, message.ReceiptHandle, cancellationToken);
                    succeeded++;
                    await WriteReceiptAsync(objects, options, runId, message.Envelope, "Succeeded", messageStartedAtUtc, null, cancellationToken);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine($"{options.WorkerName}: failed {message.Envelope.Id}: {ex.Message}");
                    await objects.PutObjectAsync(
                        "taxnet-dev-worker-failures",
                        $"{options.WorkerName}/{message.Envelope.Id}.json",
                        "application/json",
                        JsonSerializer.Serialize(new { message.Envelope, error = ex.ToString(), failedAtUtc = DateTimeOffset.UtcNow }, context.JsonOptions),
                        cancellationToken);
                    await WriteReceiptAsync(objects, options, runId, message.Envelope, "Failed", messageStartedAtUtc, ex.ToString(), cancellationToken);
                }
            }

            if (!options.RunOnce)
            {
                await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), cancellationToken);
            }
        } while (!options.RunOnce && !cancellationToken.IsCancellationRequested);

        await objects.PutObjectAsync(
            "taxnet-dev-worker-artifacts",
            $"{options.WorkerName}/runs/{runId}.json",
            "application/json",
            JsonSerializer.Serialize(new
            {
                runId,
                options.WorkerName,
                options.QueueName,
                options.QueueMode,
                options.ObjectStoreMode,
                startedAtUtc,
                completedAtUtc = DateTimeOffset.UtcNow,
                processed,
                succeeded,
                failed
            }, context.JsonOptions),
            cancellationToken);

        return 0;
    }

    private static Task WriteReceiptAsync(
        IObjectStorageClient objects,
        WorkerOptions options,
        string runId,
        QueueEnvelope envelope,
        string outcome,
        DateTimeOffset startedAtUtc,
        string? error,
        CancellationToken cancellationToken)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        return objects.PutObjectAsync(
            "taxnet-dev-worker-artifacts",
            $"{options.WorkerName}/receipts/{completedAtUtc:yyyy/MM/dd}/{envelope.Id}.json",
            "application/json",
            JsonSerializer.Serialize(new
            {
                runId,
                options.WorkerName,
                options.QueueName,
                options.QueueMode,
                options.ObjectStoreMode,
                envelope.Id,
                envelope.Type,
                envelope.CorrelationId,
                envelope.Attempt,
                outcome,
                startedAtUtc,
                completedAtUtc,
                durationMs = decimal.Round((decimal)(completedAtUtc - startedAtUtc).TotalMilliseconds, 2),
                error
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }),
            cancellationToken);
    }
}
