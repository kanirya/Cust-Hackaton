using System.Text.Json;

namespace TaxNetGuardian.Worker.Shared;

public sealed record QueueEnvelope(
    string Id,
    string Type,
    string CorrelationId,
    string PayloadJson,
    int Attempt,
    DateTimeOffset CreatedAtUtc);

public sealed record ReceivedQueueMessage(
    QueueEnvelope Envelope,
    string ReceiptHandle);

public sealed record WorkerOptions
{
    public string WorkerName { get; init; } = "TaxNet.Worker";
    public string QueueName { get; init; } = "taxnet-dev-worker-jobs";
    public string QueueMode { get; init; } = "File";
    public string ObjectStoreMode { get; init; } = "File";
    public string AwsRegion { get; init; } = "us-east-1";
    public string LocalStackEndpoint { get; init; } = "http://localhost:4566";
    public string DataRoot { get; init; } = Path.Combine(AppContext.BaseDirectory, "App_Data");
    public string ApiBaseUrl { get; init; } = "http://localhost:5191";
    public string DemoRole { get; init; } = "taxnet-admin";
    public int MaxMessages { get; init; } = 5;
    public bool RunOnce { get; init; } = true;
    public int PollSeconds { get; init; } = 5;

    public static WorkerOptions FromEnvironment(string workerName, string queueName, string[] args)
    {
        var runForever = args.Any(x => x.Equals("--watch", StringComparison.OrdinalIgnoreCase));
        return new WorkerOptions
        {
            WorkerName = Environment.GetEnvironmentVariable("TAXNET_WORKER_NAME") ?? workerName,
            QueueName = Environment.GetEnvironmentVariable("TAXNET_QUEUE_NAME") ?? queueName,
            QueueMode = Environment.GetEnvironmentVariable("TAXNET_QUEUE_MODE") ?? "File",
            ObjectStoreMode = Environment.GetEnvironmentVariable("TAXNET_OBJECT_STORE_MODE") ?? "File",
            AwsRegion = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
            LocalStackEndpoint = Environment.GetEnvironmentVariable("LOCALSTACK_ENDPOINT") ?? "http://localhost:4566",
            DataRoot = Environment.GetEnvironmentVariable("TAXNET_WORKER_DATA_ROOT") ?? Path.Combine(Directory.GetCurrentDirectory(), ".appdata", "workers"),
            ApiBaseUrl = Environment.GetEnvironmentVariable("TAXNET_API_BASE_URL") ?? "http://localhost:5191",
            DemoRole = Environment.GetEnvironmentVariable("TAXNET_DEMO_ROLE") ?? "taxnet-admin",
            MaxMessages = int.TryParse(Environment.GetEnvironmentVariable("TAXNET_MAX_MESSAGES"), out var max) ? Math.Max(1, max) : 5,
            RunOnce = !runForever,
            PollSeconds = int.TryParse(Environment.GetEnvironmentVariable("TAXNET_POLL_SECONDS"), out var poll) ? Math.Max(1, poll) : 5
        };
    }
}

public interface IQueueClient
{
    Task SendAsync(string queueName, QueueEnvelope envelope, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReceivedQueueMessage>> ReceiveAsync(string queueName, int maxMessages, CancellationToken cancellationToken);
    Task DeleteAsync(string queueName, string receiptHandle, CancellationToken cancellationToken);
}

public interface IObjectStorageClient
{
    Task PutObjectAsync(string bucket, string key, string contentType, string content, CancellationToken cancellationToken);
    Task<string?> GetObjectAsync(string bucket, string key, CancellationToken cancellationToken);
}

public interface IWorkerJobHandler
{
    Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken);
}

public sealed class WorkerContext(
    WorkerOptions options,
    IQueueClient queue,
    IObjectStorageClient objects,
    HttpClient http)
{
    public WorkerOptions Options { get; } = options;
    public IQueueClient Queue { get; } = queue;
    public IObjectStorageClient Objects { get; } = objects;
    public HttpClient Http { get; } = http;

    public JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<HttpResponseMessage> PostApiJsonAsync(string path, object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(Options.ApiBaseUrl), path));
        request.Headers.Add("X-Demo-Role", Options.DemoRole);
        request.Headers.Add("X-Demo-User", Options.WorkerName);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), System.Text.Encoding.UTF8, "application/json");
        return await Http.SendAsync(request, cancellationToken);
    }

    public async Task<HttpResponseMessage> GetApiAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(Options.ApiBaseUrl), path));
        request.Headers.Add("X-Demo-Role", Options.DemoRole);
        request.Headers.Add("X-Demo-User", Options.WorkerName);
        return await Http.SendAsync(request, cancellationToken);
    }
}
