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
        do
        {
            var messages = await queue.ReceiveAsync(options.QueueName, options.MaxMessages, cancellationToken);
            if (messages.Count == 0)
            {
                Console.WriteLine($"{options.WorkerName}: no messages.");
            }

            foreach (var message in messages)
            {
                try
                {
                    Console.WriteLine($"{options.WorkerName}: handling {message.Envelope.Type} {message.Envelope.Id}");
                    await handler.HandleAsync(message.Envelope, context, cancellationToken);
                    await queue.DeleteAsync(options.QueueName, message.ReceiptHandle, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"{options.WorkerName}: failed {message.Envelope.Id}: {ex.Message}");
                    await objects.PutObjectAsync(
                        "taxnet-dev-worker-failures",
                        $"{options.WorkerName}/{message.Envelope.Id}.json",
                        "application/json",
                        JsonSerializer.Serialize(new { message.Envelope, error = ex.ToString(), failedAtUtc = DateTimeOffset.UtcNow }, context.JsonOptions),
                        cancellationToken);
                }
            }

            if (!options.RunOnce)
            {
                await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), cancellationToken);
            }
        } while (!options.RunOnce && !cancellationToken.IsCancellationRequested);

        return 0;
    }
}
