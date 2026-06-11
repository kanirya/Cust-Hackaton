using System.Text.Json;
using TaxNetGuardian.Worker.Shared;

var command = args.FirstOrDefault() ?? "seed-demo";
var options = WorkerOptions.FromEnvironment("Worker.Cli", "taxnet-dev-worker-cli", args);
using var http = new HttpClient();
IQueueClient queue = options.QueueMode.Equals("LocalStack", StringComparison.OrdinalIgnoreCase)
    ? new LocalStackSqsQueueClient(options, http)
    : new FileBackedQueueClient(options);

if (command.Equals("send", StringComparison.OrdinalIgnoreCase))
{
    var queueName = ReadArg("--queue", "taxnet-dev-audit-log-jobs");
    var type = ReadArg("--type", "AuditEventCaptured");
    var payload = ReadArg("--payload", """{"action":"ManualWorkerMessage","resource":"worker-cli"}""");
    await queue.SendAsync(queueName, NewEnvelope(type, payload), CancellationToken.None);
    Console.WriteLine($"Sent {type} to {queueName}");
    return 0;
}

if (command.Equals("seed-demo", StringComparison.OrdinalIgnoreCase))
{
    var demoMessages = new (string Queue, string Type, object Payload)[]
    {
        ("taxnet-dev-ingestion-jobs", "RunIngestionPipeline", new { requestedBy = "worker-cli" }),
        ("taxnet-dev-identity-resolution-jobs", "IdentityResolutionRequested", new { batchId = "demo-batch" }),
        ("taxnet-dev-graph-build-jobs", "GraphFeaturesRequested", new { entityId = "entity-P001" }),
        ("taxnet-dev-risk-score-jobs", "RiskScoringRequested", new { batchId = "demo-batch" }),
        ("taxnet-dev-rag-index-jobs", "RagDocumentCaptured", new
        {
            title = "Worker seeded audit SOP",
            sourceType = "AuditSop",
            url = "sandbox://worker-seeded/audit-sop",
            content = "Worker-ingested policy text requires human review, evidence validation, and citizen correction before escalation.",
            tags = new[] { "worker", "audit", "human-review" }
        }),
        ("taxnet-dev-report-jobs", "ReportRequested", new { caseId = "case-P001" }),
        ("taxnet-dev-audit-log-jobs", "AuditEventCaptured", new { action = "WorkerDemoSeeded", resource = "worker-cli", outcome = "Succeeded" })
    };

    foreach (var message in demoMessages)
    {
        await queue.SendAsync(message.Queue, NewEnvelope(message.Type, JsonSerializer.Serialize(message.Payload)), CancellationToken.None);
        Console.WriteLine($"Seeded {message.Type} -> {message.Queue}");
    }

    return 0;
}

Console.Error.WriteLine("Unknown command. Use seed-demo or send --queue <name> --type <type> --payload <json>.");
return 2;

string ReadArg(string name, string fallback)
{
    var index = Array.FindIndex(args, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

static QueueEnvelope NewEnvelope(string type, string payload)
    => new($"msg-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}", type, $"corr-{Guid.NewGuid():N}", payload, 0, DateTimeOffset.UtcNow);
