using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using TaxNetGuardian.Api;
using TaxNetGuardian.Worker.Shared;

var builder = WebApplication.CreateBuilder(args);
var platformOptions = new TaxNetPlatformOptions();
builder.Configuration.GetSection(TaxNetPlatformOptions.SectionName).Bind(platformOptions);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;
});
builder.Services.AddSingleton(platformOptions);
builder.Services.AddSingleton(sp => new TaxNetState(sp.GetRequiredService<IWebHostEnvironment>()));
builder.Services.AddSingleton<ISecretProvider, LocalStackSecretsManagerSecretProvider>();
builder.Services.AddSingleton<ModelGatewayClient>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});
builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var key = AuthorizationCatalog.GetCurrentActor(context);
        if (string.IsNullOrWhiteSpace(key) || key.Equals("taxnet-admin", StringComparison.OrdinalIgnoreCase))
        {
            key = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        }

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, platformOptions.RateLimits.PermitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, platformOptions.RateLimits.WindowSeconds)),
            QueueLimit = Math.Max(0, platformOptions.RateLimits.QueueLimit),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });
});

var app = builder.Build();

app.UseCors();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestAuditMiddleware>();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
if (platformOptions.Auth.RequireJwt)
{
    app.UseMiddleware<JwtClaimsProjectionMiddleware>();
}
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    var isProtectedApi = path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
                         path.StartsWith("/sandbox", StringComparison.OrdinalIgnoreCase);
    var isHealth = path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/health", StringComparison.OrdinalIgnoreCase);

    if (platformOptions.Auth.RequireJwt && isProtectedApi && !isHealth && context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Authentication required. Configure Cognito JWT bearer token for production mode.",
            authMode = platformOptions.Auth.Mode,
            correlationId = context.TraceIdentifier
        });
        return;
    }

    await next();
});
app.Use(async (context, next) =>
{
    if (AuthorizationCatalog.TryGetAccessDecision(context, out var decision) && !decision.Allowed)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Forbidden by TaxNet development authorization policy.",
            role = decision.Role,
            requiredRoles = decision.RequiredRoles,
            path = context.Request.Path.Value
        });
        return;
    }

    await next();
});

app.MapGet("/", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));

app.MapGet("/sandbox", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));

app.MapGet("/citizen", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));

app.MapGet("/system", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));

app.MapGet("/rag", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));

app.MapGet("/backend", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));

app.MapGet("/cases", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));

app.MapGet("/graph", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "TaxNetGuardian.Api",
    version = "0.1.0-hackathon",
    timestampUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "Live",
    service = platformOptions.Observability.ServiceName,
    timestampUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/health/ready", async (TaxNetState state, ModelGatewayClient modelGatewayClient, TaxNetPlatformOptions options, CancellationToken cancellationToken) =>
{
    var modelConfig = await modelGatewayClient.GetConfigAsync(cancellationToken);
    var diagnostics = await modelGatewayClient.GetSecretDiagnosticsAsync(cancellationToken);
    var report = ProductionReadiness.Build(state, options, modelConfig, diagnostics);
    var httpStatus = report.Status == "ProductionReady" || options.Auth.AllowDevelopmentHeaders
        ? StatusCodes.Status200OK
        : StatusCodes.Status503ServiceUnavailable;
    return Results.Json(report, statusCode: httpStatus);
});

app.MapGet("/api/me", (HttpContext context) => Results.Ok(AuthorizationCatalog.CurrentUser(context)));

app.MapGet("/api/authz", (TaxNetPlatformOptions options) => Results.Ok(new
{
    mode = options.Auth.Mode,
    productionTarget = "Cognito User Pools for users, OAuth client credentials for services.",
    headerRole = "X-Demo-Role",
    headerUser = "X-Demo-User",
    jwt = new
    {
        options.Auth.Authority,
        options.Auth.Audience,
        options.Auth.RoleClaim,
        options.Auth.ScopeClaim,
        configured = options.Auth.RequireJwt && !string.IsNullOrWhiteSpace(options.Auth.Authority) && !string.IsNullOrWhiteSpace(options.Auth.Audience)
    },
    roles = AuthorizationCatalog.Roles,
    internalServiceScopes = new[]
    {
        "taxnet/internal.ingestion.write",
        "taxnet/internal.identity.resolve",
        "taxnet/internal.graph.write",
        "taxnet/internal.graph.read",
        "taxnet/internal.risk.score",
        "taxnet/internal.case.write",
        "taxnet/internal.rag.query",
        "taxnet/internal.model.invoke",
        "taxnet/internal.sandbox.read",
        "taxnet/internal.audit.write"
    }
}));

app.MapGet("/api/dashboard/summary", (TaxNetState state) => Results.Ok(state.GetDashboardSummary()));

app.MapGet("/api/cases", (TaxNetState state, string? riskBand, string? status, string? city) =>
{
    var query = state.Cases.AsEnumerable();

    if (!string.IsNullOrWhiteSpace(riskBand))
    {
        query = query.Where(x => x.Score.RiskBand.Equals(riskBand, StringComparison.OrdinalIgnoreCase));
    }

    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
    }

    if (!string.IsNullOrWhiteSpace(city))
    {
        query = query.Where(x => x.City.Equals(city, StringComparison.OrdinalIgnoreCase));
    }

    var items = query
        .OrderByDescending(x => x.Score.Score)
        .Select(x => new
        {
            x.Id,
            x.EntityId,
            x.PersonId,
            x.Status,
            x.AssignedTo,
            x.City,
            x.Province,
            score = x.Score.Score,
            riskBand = x.Score.RiskBand,
            confidence = x.Score.Confidence,
            topReasons = x.Score.Components.Where(c => c.Score > 0).OrderByDescending(c => c.Score).Take(3).Select(c => c.Name),
            evidenceCount = x.Evidence.Count,
            x.UpdatedAtUtc
        })
        .ToArray();

    return Results.Ok(new { items, total = items.Length });
});

app.MapGet("/api/cases/{caseId}", (TaxNetState state, string caseId) =>
{
    var caseItem = state.Cases.FirstOrDefault(x => x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase));
    if (caseItem is null)
    {
        return Results.NotFound(new { message = $"Case {caseId} was not found." });
    }

    var person = state.People.First(x => x.Id == caseItem.PersonId);
    var entity = state.Entities.First(x => x.Id == caseItem.EntityId);
    return Results.Ok(new
    {
        caseItem,
        person,
        entity,
        explanation = state.BuildExplanation(caseId),
        corrections = state.Corrections.Where(x => x.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase)),
        timeline = state.GetTimeline(caseId),
        reports = state.Reports.Where(x => x.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase))
    });
});

app.MapGet("/api/cases/{caseId}/timeline", (TaxNetState state, string caseId) =>
{
    if (state.Cases.All(x => !x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.NotFound(new { message = $"Case {caseId} was not found." });
    }

    return Results.Ok(new { items = state.GetTimeline(caseId) });
});

app.MapPost("/api/cases/{caseId}/assign", (TaxNetState state, HttpContext context, string caseId, CaseAssignmentRequest request) =>
{
    if (!AuthorizationCatalog.HasRole(context, "taxnet-supervisor", "taxnet-senior-auditor"))
    {
        return Results.Json(new { message = "Forbidden. Requires supervisor, senior auditor, or admin role." }, statusCode: StatusCodes.Status403Forbidden);
    }

    try
    {
        return Results.Ok(state.AssignCase(caseId, request, AuthorizationCatalog.GetCurrentActor(context)));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});

app.MapPost("/api/cases/{caseId}/request-citizen-clarification", (TaxNetState state, HttpContext context, string caseId) =>
{
    if (!AuthorizationCatalog.HasRole(context, "taxnet-auditor", "taxnet-senior-auditor", "taxnet-supervisor"))
    {
        return Results.Json(new { message = "Forbidden. Requires auditor, supervisor, or admin role." }, statusCode: StatusCodes.Status403Forbidden);
    }

    try
    {
        return Results.Ok(state.RequestCitizenClarification(caseId, AuthorizationCatalog.GetCurrentActor(context)));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});

app.MapPost("/api/cases/{caseId}/decision", (TaxNetState state, HttpContext context, string caseId, CaseDecisionRequest request) =>
{
    if (!AuthorizationCatalog.HasRole(context, "taxnet-auditor", "taxnet-senior-auditor"))
    {
        return Results.Json(new { message = "Forbidden. Requires auditor, senior auditor, or admin role." }, statusCode: StatusCodes.Status403Forbidden);
    }

    try
    {
        return Results.Ok(state.RecordDecision(caseId, request, AuthorizationCatalog.GetCurrentActor(context)));
    }
    catch (InvalidOperationException ex)
    {
        return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
});

app.MapGet("/api/graph/entities/{entityId}/neighborhood", (TaxNetState state, string entityId) =>
{
    var graph = state.BuildGraph(entityId);
    return graph.Nodes.Count == 0 ? Results.NotFound(new { message = $"Entity {entityId} was not found." }) : Results.Ok(graph);
});

app.MapGet("/api/graph/entities/{entityId}/features", (TaxNetState state, string entityId) =>
{
    try
    {
        return Results.Ok(state.ExtractGraphFeatures(entityId));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});

app.MapGet("/api/identity/entities", (TaxNetState state) => Results.Ok(new
{
    items = state.Entities.OrderByDescending(x => x.MatchConfidence).Take(200),
    total = state.Entities.Count
}));

app.MapGet("/api/identity/evaluation", (TaxNetState state) => Results.Ok(state.GetIdentityEvaluation()));

app.MapPost("/api/assistant/cases/{caseId}/ask", (TaxNetState state, string caseId, AssistantRequest request) =>
{
    if (state.Cases.All(x => !x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.NotFound(new { message = $"Case {caseId} was not found." });
    }

    return Results.Ok(state.AskAssistant(caseId, request.Question));
});

app.MapPost("/api/reports/cases/{caseId}", (TaxNetState state, string caseId) =>
{
    if (state.Cases.All(x => !x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.NotFound(new { message = $"Case {caseId} was not found." });
    }

    return Results.Ok(state.BuildReport(caseId));
});

app.MapGet("/api/reports", (TaxNetState state, string? caseId) =>
{
    var query = state.Reports.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(caseId))
    {
        query = query.Where(x => x.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase));
    }

    var items = query.Take(100).ToArray();
    return Results.Ok(new { items, total = items.Length });
});

app.MapGet("/api/reports/{reportId}", (TaxNetState state, string reportId) =>
{
    var report = state.Reports.FirstOrDefault(x => x.Id.Equals(reportId, StringComparison.OrdinalIgnoreCase));
    return report is null ? Results.NotFound(new { message = $"Report {reportId} was not found." }) : Results.Ok(report);
});

app.MapPost("/api/ingestion/run", (TaxNetState state) =>
{
    state.RebuildIntelligence();
    return Results.Ok(new
    {
        message = "Synthetic import pipeline completed.",
        importedProfiles = state.People.Count,
        resolvedEntities = state.Entities.Count,
        cases = state.Cases.Count,
        timestampUtc = DateTimeOffset.UtcNow
    });
});

app.MapGet("/api/system/workers", (TaxNetState state) => Results.Ok(new
{
    queues = new[]
    {
        "taxnet-dev-ingestion-jobs",
        "taxnet-dev-identity-resolution-jobs",
        "taxnet-dev-graph-build-jobs",
        "taxnet-dev-risk-score-jobs",
        "taxnet-dev-rag-index-jobs",
        "taxnet-dev-report-jobs",
        "taxnet-dev-audit-log-jobs"
    },
    workers = state.Workers,
    retryPolicy = new
    {
        maxReceiveCount = 5,
        backoff = "exponential",
        deadLetterQueues = true,
        idempotency = "batchId/jobId/scoreVersion/reportId"
    },
    jobs = state.ImportJobs.Take(20),
    recentAuditEvents = state.TimelineEvents.Take(20),
    reportExports = state.Reports.Take(10)
}));

app.MapPost("/api/system/workers/run", (TaxNetState state, HttpContext context) =>
    Results.Ok(state.RunWorkerCycle(AuthorizationCatalog.GetCurrentActor(context))));

app.MapPost("/api/system/workers/enqueue", async (IConfiguration configuration, WorkerEnqueueRequest request, CancellationToken cancellationToken) =>
{
    var options = BuildWorkerOptions(configuration);
    using var http = new HttpClient();
    var queue = BuildQueueClient(options, http);
    var payload = string.IsNullOrWhiteSpace(request.PayloadJson) ? "{}" : request.PayloadJson;
    var envelope = NewQueueEnvelope(request.Type, request.CorrelationId, payload);
    await queue.SendAsync(request.QueueName, envelope, cancellationToken);
    return Results.Ok(new
    {
        message = "Worker message enqueued.",
        request.QueueName,
        envelope.Id,
        envelope.Type,
        envelope.CorrelationId,
        options.QueueMode
    });
});

app.MapPost("/api/system/workers/enqueue-demo", async (IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var options = BuildWorkerOptions(configuration);
    using var http = new HttpClient();
    var queue = BuildQueueClient(options, http);
    var messages = DemoWorkerMessages();
    foreach (var message in messages)
    {
        await queue.SendAsync(message.QueueName, message.Envelope, cancellationToken);
    }

    return Results.Ok(new
    {
        message = "Demo worker messages enqueued.",
        options.QueueMode,
        options.ObjectStoreMode,
        messages = messages.Select(x => new { x.QueueName, x.Envelope.Id, x.Envelope.Type, x.Envelope.CorrelationId })
    });
});

app.MapGet("/api/system/audit", (TaxNetState state, string? action, string? resource) =>
{
    var query = state.AuditEvents.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(action))
    {
        query = query.Where(x => x.Action.Contains(action, StringComparison.OrdinalIgnoreCase));
    }

    if (!string.IsNullOrWhiteSpace(resource))
    {
        query = query.Where(x => x.Resource.Contains(resource, StringComparison.OrdinalIgnoreCase));
    }

    var items = query.Take(100).ToArray();
    return Results.Ok(new { items, total = items.Length });
});

app.MapGet("/api/system/object-store", (TaxNetState state) => Results.Ok(new
{
    buckets = state.ObjectStore.GroupBy(x => x.Bucket).Select(x => new { bucket = x.Key, objects = x.Count(), bytes = x.Sum(o => o.SizeBytes) }),
    objects = state.ObjectStore.Take(100)
}));

app.MapGet("/api/system/persistence", (TaxNetState state) => Results.Ok(state.GetPersistenceStatus()));

app.MapGet("/api/system/notifications", (TaxNetState state) => Results.Ok(new
{
    items = state.Notifications.Take(100),
    queued = state.Notifications.Count(x => x.Status == "Queued"),
    sent = state.Notifications.Count(x => x.Status == "Sent")
}));

app.MapGet("/api/system/infra", (TaxNetState state, TaxNetPlatformOptions options) => Results.Ok(new
{
    mode = options.Storage.OperationalStore.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase)
        ? "Production modular monolith with deployable service boundaries"
        : "Hackathon modular monolith with production service boundaries",
    stores = new[]
    {
        new { name = "Operational DB", configured = options.Storage.OperationalStore, current = $"{state.People.Count} people / {state.Cases.Count} cases", productionTarget = "RDS PostgreSQL" },
        new { name = "Graph DB", configured = options.Storage.GraphStore, current = "In-memory graph neighborhood builder", productionTarget = "Neo4j/Memgraph/Neptune" },
        new { name = "Vector DB", configured = options.Storage.VectorStore, current = $"Lexical RAG index with {state.RagChunks.Count} chunks", productionTarget = "Qdrant or pgvector" },
        new { name = "Object Store", configured = options.Storage.ObjectStore, current = $"{state.ObjectStore.Count} object metadata records", productionTarget = "S3 buckets" },
        new { name = "Queue Bus", configured = Environment.GetEnvironmentVariable("TAXNET_QUEUE_MODE") ?? "File", current = $"{state.Workers.Count} queue-backed worker contracts", productionTarget = "SQS queues + DLQs" },
        new { name = "Secrets Manager", configured = Environment.GetEnvironmentVariable("TAXNET_SECRET_PROVIDER") ?? "LocalStack", current = $"{state.Providers.Count} provider secret names configured", productionTarget = "AWS Secrets Manager" },
        new { name = "Observability", configured = options.Observability.ServiceName, current = $"{state.AuditEvents.Count} structured audit events", productionTarget = "CloudWatch + OpenTelemetry" }
    },
    environment = new
    {
        taxnetEnv = options.Environment,
        awsRegion = Environment.GetEnvironmentVariable("AWS_REGION") ?? "ap-south-1",
        rawBucket = Environment.GetEnvironmentVariable("S3_BUCKET_RAW") ?? "taxnet-dev-raw-source-snapshots",
        reportBucket = Environment.GetEnvironmentVariable("S3_BUCKET_REPORTS") ?? "taxnet-dev-audit-reports",
        modelDefault = Environment.GetEnvironmentVariable("MODEL_GATEWAY_DEFAULT_PROVIDER") ?? "deterministic-template",
        authMode = options.Auth.Mode
    }
}));

app.MapGet("/api/system/readiness", async (TaxNetState state, ModelGatewayClient modelGatewayClient, TaxNetPlatformOptions options, CancellationToken cancellationToken) =>
{
    var config = await modelGatewayClient.GetConfigAsync(cancellationToken);
    var diagnostics = await modelGatewayClient.GetSecretDiagnosticsAsync(cancellationToken);
    return Results.Ok(ProductionReadiness.Build(state, options, config, diagnostics));
});

app.MapGet("/api/system/storage/schema", (TaxNetState state) => Results.Ok(state.GetStorageSchema()));

app.MapGet("/api/connectors/readiness", (TaxNetState state) => Results.Ok(new
{
    providers = state.Providers.Select(provider => new
    {
        provider.ProviderCode,
        provider.Name,
        provider.Mode,
        provider.Status,
        provider.CredentialSecretName,
        officialConfig = state.ProviderConfigs.TryGetValue(provider.ProviderCode, out var config) ? config : null
    }),
    replacementRule = "Main TaxNet services consume canonical contracts; sandbox-specific DTOs stop at connector boundaries."
}));

app.MapPost("/api/connectors/{providerCode}/test", (TaxNetState state, string providerCode) =>
{
    try
    {
        return Results.Ok(state.TestConnector(providerCode));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});

app.MapGet("/api/system/rag", (TaxNetState state) => Results.Ok(new
{
    service = "TaxNet.RagPolicy",
    purpose = "Current policy memory for explanations, citations, and audit guidance.",
    documents = state.RagDocuments,
    chunks = state.RagChunks.Take(50),
    chunkCount = state.RagChunks.Count,
    queryPipeline = new[] { "query rewrite", "hybrid retrieval", "rerank", "date/jurisdiction filter", "context pack", "citations" }
}));

app.MapPost("/api/system/rag/query", (TaxNetState state, RagQueryRequest request) =>
    Results.Ok(state.QueryRag(request)));

app.MapPost("/api/system/rag/documents", (TaxNetState state, RagFeedRequest request) =>
{
    var job = state.FeedRagDocument(request);
    return Results.Ok(new
    {
        message = "RAG document fed and indexed.",
        job,
        documents = state.RagDocuments.Take(20)
    });
});

app.MapGet("/api/system/model-gateway", async (ModelGatewayClient client, CancellationToken cancellationToken) =>
{
    var config = await client.GetConfigAsync(cancellationToken);
    return Results.Ok(new
    {
        service = "TaxNet.AI.ModelGateway",
        defaultProvider = config.DefaultProvider,
        providerStatus = config.Providers,
        secretDiagnostics = await client.GetSecretDiagnosticsAsync(cancellationToken),
        routing = new[]
        {
            new { task = "AuditExplanation", route = "OpenAI/Claude/Gemini/DeepSeek when allowed and key exists; deterministic fallback otherwise", reason = "quality and reasoning" },
            new { task = "CitizenExplanation", route = "local deterministic or redacted external provider", reason = "privacy-safe plain language" },
            new { task = "ReportDraft", route = "local template or external provider", reason = "repeatable formatting" },
            new { task = "PolicyQuestion", route = "RAG + selected provider", reason = "grounded citations" }
        },
        guardrails = new[] { "PII redaction", "evidence ID validation", "citation validation", "no fraud-proven language", "human review warning", "RAG citation context" },
        providerEnv = new
        {
            openAi = new[] { "OPENAI_API_KEY", "OPENAI_MODEL", "OPENAI_API_BASE_URL" },
            deepSeek = new[] { "DEEPSEEK_API_KEY", "DEEPSEEK_MODEL", "DEEPSEEK_API_BASE_URL" },
            gemini = new[] { "GEMINI_API_KEY", "GEMINI_MODEL", "GEMINI_API_BASE_URL" },
            claude = new[] { "CLAUDE_API_KEY", "CLAUDE_MODEL", "CLAUDE_API_BASE_URL", "CLAUDE_API_VERSION" },
            routing = new[] { "MODEL_GATEWAY_DEFAULT_PROVIDER" }
        }
    });
});

app.MapPost("/api/system/model-gateway/invoke", (TaxNetState state, ModelInvocationRequest request) =>
    Results.Ok(state.InvokeModelGateway(request)));

app.MapGet("/api/system/model-gateway/invocations", (TaxNetState state) => Results.Ok(new
{
    items = state.ModelInvocations.Take(50),
    total = state.ModelInvocations.Count,
    estimatedCostUsd = state.ModelInvocations.Sum(x => x.EstimatedCostUsd)
}));

app.MapPost("/api/orchestrator/cases/{caseId}/explain", (TaxNetState state, string caseId) =>
{
    if (state.Cases.All(x => !x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.NotFound(new { message = $"Case {caseId} was not found." });
    }

    var invocation = state.InvokeModelGateway(new ModelInvocationRequest(
        "AuditExplanation",
        $"Generate grounded audit explanation for {caseId}",
        caseId,
        "auto",
        false));
    return Results.Ok(new
    {
        orchestrator = "TaxNet.AI.Orchestrator",
        caseId,
        modelInvocation = invocation,
        explanation = state.BuildExplanation(caseId),
        graph = state.BuildGraph(state.Cases.First(x => x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase)).EntityId)
    });
});

app.MapGet("/api/system/architecture", () => Results.Ok(new
{
    product = "TaxNet Guardian",
    deployableMvp = new[] { "TaxNet.Main.Api", "TaxNet.GovDataSandbox.Api", "TaxNet.Worker", "Auditor Dashboard", "Sandbox Admin UI", "Citizen Portal" },
    workerRuntime = new
    {
        sharedProject = "TaxNetGuardian.Worker.Shared",
        queueModes = new[] { "File", "LocalStack" },
        objectStoreModes = new[] { "File", "LocalStack" },
        enqueueEndpoints = new[] { "POST /api/system/workers/enqueue", "POST /api/system/workers/enqueue-demo" },
        localStackEndpoint = "http://localhost:4566"
    },
    productionServices = new[]
    {
        "ApiGateway/BFF",
        "CaseManagement",
        "Ingestion",
        "IdentityResolution",
        "GraphIntelligence",
        "RiskScoring",
        "Explainability",
        "AI.Orchestrator",
        "AI.ModelGateway",
        "RagPolicy",
        "Report",
        "AuditLog",
        "Notification",
        "GovDataSandbox",
        "GovernmentConnector"
    },
    workerProjects = new[]
    {
        new { project = "TaxNetGuardian.Workers.Ingestion", queue = "taxnet-dev-ingestion-jobs", purpose = "Import connector/sandbox records and persist raw snapshots" },
        new { project = "TaxNetGuardian.Workers.IdentityResolution", queue = "taxnet-dev-identity-resolution-jobs", purpose = "Evaluate entity resolution and write artifacts" },
        new { project = "TaxNetGuardian.Workers.GraphIntelligence", queue = "taxnet-dev-graph-build-jobs", purpose = "Build graph features for scoring and investigation" },
        new { project = "TaxNetGuardian.Workers.RiskScoring", queue = "taxnet-dev-risk-score-jobs", purpose = "Refresh deterministic risk scoring" },
        new { project = "TaxNetGuardian.Workers.RagPolicy", queue = "taxnet-dev-rag-index-jobs", purpose = "Capture policy docs, snapshot source, index RAG chunks" },
        new { project = "TaxNetGuardian.Workers.Report", queue = "taxnet-dev-report-jobs", purpose = "Generate audit reports and store report artifacts" },
        new { project = "TaxNetGuardian.Workers.AuditLog", queue = "taxnet-dev-audit-log-jobs", purpose = "Persist immutable audit event artifacts" }
    },
    auth = new
    {
        externalAuth = "Amazon Cognito User Pools + JWT scopes",
        internalAuth = "OAuth client credentials with Cognito resource server scopes",
        devMode = "X-Demo-Role / X-Demo-User headers"
    },
    secrets = new[]
    {
        "/taxnet/dev/cognito/service-clients/{serviceName}",
        "/taxnet/dev/providers/fbr/credentials",
        "/taxnet/dev/providers/nadra/credentials",
        "/taxnet/dev/ai/openai/api-key",
        "/taxnet/dev/database/postgres",
        "/taxnet/dev/graph/neo4j"
    },
    stores = new[] { "PostgreSQL", "Graph DB", "Qdrant/pgvector", "S3", "SQS", "Redis", "CloudWatch" }
}));

app.MapGet("/api/sandbox/providers", (TaxNetState state) => Results.Ok(state.Providers));

app.MapPatch("/api/sandbox/providers/{providerCode}", (TaxNetState state, string providerCode, ProviderConfigUpdateRequest request) =>
{
    try
    {
        return Results.Ok(state.UpdateProvider(providerCode, request));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});

app.MapGet("/api/sandbox/datasets", (TaxNetState state) => Results.Ok(new
{
    batches = state.DatasetBatches,
    jobs = state.ImportJobs.Where(x => x.Type.StartsWith("Dataset:", StringComparison.OrdinalIgnoreCase)).Take(30),
    totals = new
    {
        batches = state.DatasetBatches.Count,
        records = state.DatasetBatches.Sum(x => x.RecordCount),
        succeeded = state.ImportJobs.Count(x => x.Status == "Succeeded" || x.Status == "SucceededWithWarnings"),
        failed = state.ImportJobs.Count(x => x.Status == "Failed")
    }
}));

app.MapGet("/api/sandbox/datasets/templates", (TaxNetState state) => Results.Ok(state.GetDatasetTemplates()));

app.MapPost("/api/sandbox/datasets/feed", (TaxNetState state, DatasetFeedRequest request) =>
{
    var batch = state.FeedDataset(request);
    return Results.Ok(new
    {
        message = "Dataset received and applied to Gov Data Sandbox.",
        batch,
        summary = state.GetDashboardSummary(),
        latestJobs = state.ImportJobs.Take(5)
    });
});

app.MapGet("/api/sandbox/profiles", (TaxNetState state, int? limit) =>
{
    var items = state.People
        .Take(Math.Clamp(limit ?? 50, 1, 500))
        .Select(person => new
        {
            person.Id,
            person.FullName,
            person.City,
            person.Province,
            person.CnicMasked,
            person.ExpectedRiskBand,
            tax = state.TaxProfiles.FirstOrDefault(x => x.IdentityToken.Value == person.IdentityToken.Value),
            vehicleCount = state.Vehicles.Count(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value),
            propertyCount = state.Properties.Count(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value),
            businessCount = state.Businesses.Count(x => x.RelatedIdentityToken.Value == person.IdentityToken.Value)
        });
    return Results.Ok(new { items, total = state.People.Count });
});

app.MapGet("/api/sandbox/profiles/{id}", (TaxNetState state, string id) =>
{
    var person = state.People.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    if (person is null)
    {
        return Results.NotFound(new { message = $"Synthetic profile {id} was not found." });
    }

    return Results.Ok(BuildSandboxProfile(state, person));
});

app.MapPost("/api/sandbox/admin/generate", (TaxNetState state, HttpContext context, SandboxGenerateRequest request) =>
{
    if (!AuthorizationCatalog.HasRole(context, "taxnet-sandbox-admin", "taxnet-data-engineer"))
    {
        return Results.Json(new { message = "Forbidden. Requires sandbox admin or data engineer role." }, statusCode: StatusCodes.Status403Forbidden);
    }

    state.GenerateSyntheticData(request.Count, request.SuspiciousPercent, request.NoisePercent);
    return Results.Ok(new
    {
        message = "Gov Data Sandbox regenerated.",
        request.Count,
        request.SuspiciousPercent,
        request.NoisePercent,
        profiles = state.People.Count,
        cases = state.Cases.Count
    });
});

app.MapPost("/sandbox/admin/seed", (TaxNetState state, HttpContext context, SandboxGenerateRequest? request) =>
{
    if (!AuthorizationCatalog.HasRole(context, "taxnet-sandbox-admin", "taxnet-data-engineer"))
    {
        return Results.Json(new { message = "Forbidden. Requires sandbox admin or data engineer role." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var seed = request ?? new SandboxGenerateRequest(120, 24, 18);
    state.GenerateSyntheticData(seed.Count, seed.SuspiciousPercent, seed.NoisePercent);
    return Results.Ok(new { message = "Sandbox seeded.", profiles = state.People.Count, cases = state.Cases.Count });
});

app.MapPost("/sandbox/admin/reset", (TaxNetState state, HttpContext context) =>
{
    if (!AuthorizationCatalog.HasRole(context, "taxnet-sandbox-admin", "taxnet-data-engineer"))
    {
        return Results.Json(new { message = "Forbidden. Requires sandbox admin or data engineer role." }, statusCode: StatusCodes.Status403Forbidden);
    }

    state.GenerateSyntheticData(120, 24, 18);
    return Results.Ok(new { message = "Sandbox reset to deterministic seed.", profiles = state.People.Count, cases = state.Cases.Count });
});

app.MapPost("/sandbox/admin/generate-citizens", (TaxNetState state, HttpContext context, SandboxGenerateRequest request) =>
{
    if (!AuthorizationCatalog.HasRole(context, "taxnet-sandbox-admin", "taxnet-data-engineer"))
    {
        return Results.Json(new { message = "Forbidden. Requires sandbox admin or data engineer role." }, statusCode: StatusCodes.Status403Forbidden);
    }

    state.GenerateSyntheticData(request.Count, request.SuspiciousPercent, request.NoisePercent);
    return Results.Ok(new { message = "Synthetic citizens generated.", profiles = state.People.Count, cases = state.Cases.Count });
});

app.MapGet("/sandbox/admin/seed-presets", () => Results.Ok(new
{
    presets = new[]
    {
        new { name = "balanced-demo", count = 120, suspiciousPercent = 24, noisePercent = 18, purpose = "General judging demo with mixed risk bands." },
        new { name = "high-risk-demo", count = 180, suspiciousPercent = 45, noisePercent = 20, purpose = "Stress case queue, reports, and graph investigation." },
        new { name = "clean-baseline", count = 80, suspiciousPercent = 8, noisePercent = 10, purpose = "False-positive and precision story." },
        new { name = "noisy-connectors", count = 160, suspiciousPercent = 28, noisePercent = 55, purpose = "Identity resolution ambiguity and correction workflow." }
    }
}));

app.MapPost("/sandbox/admin/seed-preset/{presetName}", (TaxNetState state, HttpContext context, string presetName) =>
{
    if (!AuthorizationCatalog.HasRole(context, "taxnet-sandbox-admin", "taxnet-data-engineer"))
    {
        return Results.Json(new { message = "Forbidden. Requires sandbox admin or data engineer role." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var preset = presetName.ToLowerInvariant() switch
    {
        "high-risk-demo" => new SandboxGenerateRequest(180, 45, 20),
        "clean-baseline" => new SandboxGenerateRequest(80, 8, 10),
        "noisy-connectors" => new SandboxGenerateRequest(160, 28, 55),
        _ => new SandboxGenerateRequest(120, 24, 18)
    };

    state.GenerateSyntheticData(preset.Count, preset.SuspiciousPercent, preset.NoisePercent);
    return Results.Ok(new
    {
        message = $"Seed preset '{presetName}' applied.",
        preset.Count,
        preset.SuspiciousPercent,
        preset.NoisePercent,
        profiles = state.People.Count,
        cases = state.Cases.Count
    });
});

app.MapPost("/api/demo/bootstrap", async (TaxNetState state, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    state.GenerateSyntheticData(140, 32, 22);
    state.FeedRagDocument(new RagFeedRequest(
        "Demo bootstrap audit safeguards",
        "AuditSop",
        "sandbox://demo/bootstrap-audit-safeguards",
        "Audit escalation requires human review, source verification, evidence identifiers, citizen correction windows, and citation-backed report language. AI scores prioritize review but never prove fraud.",
        ["demo", "audit", "human-review", "citizen"]));

    var options = BuildWorkerOptions(configuration);
    using var http = new HttpClient();
    var queue = BuildQueueClient(options, http);
    foreach (var message in DemoWorkerMessages())
    {
        await queue.SendAsync(message.QueueName, message.Envelope, cancellationToken);
    }

    var modelConfig = new ModelGatewayClient().GetConfig();
    return Results.Ok(new
    {
        message = "Demo bootstrap completed: sandbox seeded, RAG policy indexed, worker messages enqueued.",
        profiles = state.People.Count,
        cases = state.Cases.Count,
        ragDocuments = state.RagDocuments.Count,
        workers = state.Workers,
        workerQueueMode = options.QueueMode,
        modelProviders = modelConfig.Providers
    });
});

app.MapGet("/sandbox/admin/providers", (TaxNetState state) => Results.Ok(state.Providers));

app.MapPatch("/sandbox/admin/providers/{providerCode}", (TaxNetState state, string providerCode, ProviderConfigUpdateRequest request) =>
    Results.Ok(state.UpdateProvider(providerCode, request)));

app.MapGet("/sandbox/admin/profiles", (TaxNetState state, int? limit) =>
    Results.Ok(new { items = state.People.Take(Math.Clamp(limit ?? 100, 1, 500)).Select(person => BuildSandboxProfile(state, person)), total = state.People.Count }));

app.MapGet("/sandbox/admin/profiles/{syntheticPersonId}", (TaxNetState state, string syntheticPersonId) =>
{
    var person = state.People.FirstOrDefault(x => x.Id.Equals(syntheticPersonId, StringComparison.OrdinalIgnoreCase));
    return person is null ? Results.NotFound(new { message = $"Synthetic profile {syntheticPersonId} was not found." }) : Results.Ok(BuildSandboxProfile(state, person));
});

app.MapGet("/sandbox/nadra/identity/{identityToken}", (TaxNetState state, string identityToken) =>
{
    var person = FindByIdentityToken(state, identityToken);
    return person is null ? Results.NotFound() : Results.Ok(new
    {
        provider = "NADRA Sandbox",
        profile = new { person.FullName, person.UrduName, person.FatherName, person.CnicMasked, person.City, person.Province, person.IdentityToken }
    });
});

app.MapGet("/sandbox/nadra/family-links/{identityToken}", (TaxNetState state, string identityToken) =>
{
    var person = FindByIdentityToken(state, identityToken);
    if (person is null) return Results.NotFound();
    var related = state.People
        .Where(x => x.City == person.City && x.Id != person.Id)
        .OrderBy(x => x.Id)
        .Take(3)
        .Select((x, index) => new
        {
            relationType = index == 0 ? "Household" : "PossibleRelative",
            confidence = index == 0 ? 0.82m : 0.68m,
            identityToken = x.IdentityToken,
            x.FullName,
            x.CnicMasked
        });
    return Results.Ok(new { provider = "NADRA Sandbox", identityToken, items = related });
});

app.MapGet("/sandbox/nadra/address-history/{identityToken}", (TaxNetState state, string identityToken) =>
{
    var person = FindByIdentityToken(state, identityToken);
    if (person is null) return Results.NotFound();
    return Results.Ok(new
    {
        provider = "NADRA Sandbox",
        identityToken,
        items = new[]
        {
            new { city = person.City, province = person.Province, addressMasked = $"{person.City} sector ***", fromYear = 2021, toYear = 2026 },
            new { city = person.Province == "Punjab" ? "Lahore" : "Karachi", province = person.Province, addressMasked = "prior residence ***", fromYear = 2017, toYear = 2021 }
        }
    });
});

app.MapGet("/sandbox/fbr/taxpayer/{identityToken}", (TaxNetState state, string identityToken) =>
{
    var tax = state.TaxProfiles.FirstOrDefault(x => x.IdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase));
    return tax is null ? Results.NotFound() : Results.Ok(new { provider = "FBR Sandbox", tax });
});

app.MapGet("/sandbox/fbr/atl-status/{ntn}", (TaxNetState state, string ntn) =>
{
    var tax = state.TaxProfiles.FirstOrDefault(x => x.Ntn.Equals(ntn, StringComparison.OrdinalIgnoreCase));
    return tax is null ? Results.NotFound() : Results.Ok(new { provider = "FBR Sandbox", tax.Ntn, tax.FilerStatus, isActive = tax.FilerStatus == "Active Filer" });
});

app.MapGet("/sandbox/fbr/returns/{identityToken}", (TaxNetState state, string identityToken) =>
{
    var tax = state.TaxProfiles.FirstOrDefault(x => x.IdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase));
    if (tax is null) return Results.NotFound();
    var years = Enumerable.Range(Math.Max(2021, tax.TaxYear - 3), 4).Select(year => new
    {
        taxYear = year,
        filingStatus = year == tax.TaxYear ? tax.FilerStatus : tax.FilerStatus == "Non-Filer" ? "No Return" : "Filed",
        declaredAnnualIncome = year == tax.TaxYear ? tax.DeclaredAnnualIncome : decimal.Round(tax.DeclaredAnnualIncome * (0.82m + ((year % 4) * 0.05m)), 0),
        taxPaid = year == tax.TaxYear ? tax.TaxPaid : decimal.Round(tax.TaxPaid * (0.80m + ((year % 4) * 0.04m)), 0)
    });
    return Results.Ok(new { provider = "FBR Sandbox", identityToken, tax.Ntn, items = years });
});

app.MapGet("/sandbox/fbr/withholding/{identityToken}", (TaxNetState state, string identityToken) =>
{
    var tax = state.TaxProfiles.FirstOrDefault(x => x.IdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase));
    if (tax is null) return Results.NotFound();
    var withholding = new[]
    {
        new { category = "SalaryOrBusiness", amount = decimal.Round(tax.TaxPaid * 0.45m, 0), taxYear = tax.TaxYear },
        new { category = "Banking", amount = decimal.Round(tax.TaxPaid * 0.18m, 0), taxYear = tax.TaxYear },
        new { category = "PropertyVehicle", amount = decimal.Round(tax.TaxPaid * 0.12m, 0), taxYear = tax.TaxYear }
    };
    return Results.Ok(new { provider = "FBR Sandbox", identityToken, tax.Ntn, items = withholding });
});

app.MapGet("/sandbox/excise/vehicles", (TaxNetState state, string identityToken) =>
    Results.Ok(new { provider = "Excise Sandbox", items = state.Vehicles.Where(x => x.OwnerIdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase)) }));

app.MapGet("/sandbox/excise/vehicle/{registrationNumber}", (TaxNetState state, string registrationNumber) =>
{
    var vehicle = state.Vehicles.FirstOrDefault(x => x.RegistrationNumberMasked.Equals(registrationNumber, StringComparison.OrdinalIgnoreCase) || x.ProviderRecordId.Equals(registrationNumber, StringComparison.OrdinalIgnoreCase));
    return vehicle is null ? Results.NotFound() : Results.Ok(new { provider = "Excise Sandbox", vehicle });
});

app.MapGet("/sandbox/secp/companies", (TaxNetState state, string identityToken) =>
    Results.Ok(new { provider = "SECP Sandbox", items = state.Businesses.Where(x => x.RelatedIdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase)) }));

app.MapGet("/sandbox/secp/company/{companyRegistrationNumber}", (TaxNetState state, string companyRegistrationNumber) =>
{
    var company = state.Businesses.FirstOrDefault(x => x.CompanyRegistrationNumber.Equals(companyRegistrationNumber, StringComparison.OrdinalIgnoreCase) || x.ProviderRecordId.Equals(companyRegistrationNumber, StringComparison.OrdinalIgnoreCase));
    return company is null ? Results.NotFound() : Results.Ok(new { provider = "SECP Sandbox", company });
});

app.MapGet("/sandbox/property/ownership", (TaxNetState state, string identityToken) =>
    Results.Ok(new { provider = "Property Sandbox", items = state.Properties.Where(x => x.OwnerIdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase)) }));

app.MapGet("/sandbox/property/transactions", (TaxNetState state, string identityToken) =>
{
    var properties = state.Properties.Where(x => x.OwnerIdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase)).ToArray();
    var transactions = properties.SelectMany((property, index) => new[]
    {
        new { property.PropertyToken, transactionType = "Acquisition", declaredValue = decimal.Round(property.EstimatedValue * 0.82m, 0), transactionYear = 2020 + (index % 5), city = property.City },
        new { property.PropertyToken, transactionType = "ValuationUpdate", declaredValue = property.EstimatedValue, transactionYear = 2025, city = property.City }
    });
    return Results.Ok(new { provider = "Property Sandbox", identityToken, items = transactions });
});

app.MapGet("/sandbox/utilities/bills", (TaxNetState state, string identityToken) =>
    Results.Ok(new { provider = "Utility Sandbox", items = state.UtilityBills.Where(x => x.OwnerIdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase)) }));

app.MapGet("/sandbox/utilities/meters/{meterNumber}", (TaxNetState state, string meterNumber) =>
{
    var meter = state.UtilityBills.FirstOrDefault(x => x.MeterToken.Equals(meterNumber, StringComparison.OrdinalIgnoreCase) || x.ProviderRecordId.Equals(meterNumber, StringComparison.OrdinalIgnoreCase));
    return meter is null ? Results.NotFound() : Results.Ok(new { provider = "Utility Sandbox", meter });
});

app.MapGet("/sandbox/travel/history", (TaxNetState state, string identityToken) =>
    Results.Ok(new { provider = "Travel Sandbox", items = state.Travel.Where(x => x.TravelerIdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase)) }));

app.MapGet("/api/citizen/me", (TaxNetState state) =>
{
    var caseItem = state.Cases.OrderByDescending(x => x.Score.Score).First();
    var person = state.People.First(x => x.Id == caseItem.PersonId);
    return Results.Ok(new
    {
        person = new { person.FullName, person.City, person.Province, person.CnicMasked },
        safeSummary = $"Some records linked to your profile may require review. Current status: {caseItem.Status}.",
        riskBand = caseItem.Score.RiskBand,
        correctionOptions = new[] { "AssetOwnershipDispute", "OutdatedUtilityRecord", "IncorrectBusinessLink", "IncomeAlreadyDeclared", "Other" },
        corrections = state.Corrections.Where(x => x.CaseId.Equals(caseItem.Id, StringComparison.OrdinalIgnoreCase))
    });
});

app.MapPost("/api/citizen/corrections", (TaxNetState state, CitizenCorrectionRequest request) =>
{
    var correction = state.AddCorrection(request);
    return Results.Ok(new
    {
        message = "Correction submitted for human review.",
        correctionId = correction.Id,
        request.CaseId,
        correction.Status
    });
});

app.MapFallback((IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));

app.Run();

static WorkerOptions BuildWorkerOptions(IConfiguration configuration)
{
    var dataRoot = Environment.GetEnvironmentVariable("TAXNET_WORKER_DATA_ROOT")
        ?? configuration["Workers:DataRoot"]
        ?? Path.Combine(Directory.GetCurrentDirectory(), ".appdata", "workers");

    return new WorkerOptions
    {
        WorkerName = "TaxNetGuardian.Api",
        QueueMode = Environment.GetEnvironmentVariable("TAXNET_QUEUE_MODE") ?? configuration["Workers:QueueMode"] ?? "File",
        ObjectStoreMode = Environment.GetEnvironmentVariable("TAXNET_OBJECT_STORE_MODE") ?? configuration["Workers:ObjectStoreMode"] ?? "File",
        LocalStackEndpoint = Environment.GetEnvironmentVariable("LOCALSTACK_ENDPOINT") ?? configuration["Workers:LocalStackEndpoint"] ?? "http://localhost:4566",
        DataRoot = dataRoot,
        ApiBaseUrl = Environment.GetEnvironmentVariable("TAXNET_API_BASE_URL") ?? configuration["Workers:ApiBaseUrl"] ?? "http://localhost:5191",
        DemoRole = "taxnet-admin"
    };
}

static IQueueClient BuildQueueClient(WorkerOptions options, HttpClient http)
    => options.QueueMode.Equals("LocalStack", StringComparison.OrdinalIgnoreCase)
        ? new LocalStackSqsQueueClient(options, http)
        : new FileBackedQueueClient(options);

static QueueEnvelope NewQueueEnvelope(string type, string? correlationId, string payloadJson)
    => new($"msg-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}", type, string.IsNullOrWhiteSpace(correlationId) ? $"corr-{Guid.NewGuid():N}" : correlationId, payloadJson, 0, DateTimeOffset.UtcNow);

static IReadOnlyList<QueuedWorkerMessage> DemoWorkerMessages()
{
    QueueEnvelope Envelope(string type, object payload)
        => NewQueueEnvelope(type, null, System.Text.Json.JsonSerializer.Serialize(payload));

    return
    [
        new("taxnet-dev-ingestion-jobs", Envelope("RunIngestionPipeline", new { requestedBy = "api-control-plane" })),
        new("taxnet-dev-identity-resolution-jobs", Envelope("IdentityResolutionRequested", new { batchId = "api-demo" })),
        new("taxnet-dev-graph-build-jobs", Envelope("GraphFeaturesRequested", new { entityId = "entity-P001" })),
        new("taxnet-dev-risk-score-jobs", Envelope("RiskScoringRequested", new { batchId = "api-demo" })),
        new("taxnet-dev-rag-index-jobs", Envelope("RagDocumentCaptured", new
        {
            title = "API seeded worker policy",
            sourceType = "AuditSop",
            url = "sandbox://api-worker-seed/policy",
            content = "API seeded worker policy confirms human review, evidence validation, and citizen correction windows before escalation.",
            tags = new[] { "api", "worker", "human-review" }
        })),
        new("taxnet-dev-report-jobs", Envelope("ReportRequested", new { caseId = "case-P001" })),
        new("taxnet-dev-audit-log-jobs", Envelope("AuditEventCaptured", new { action = "ApiWorkerDemoEnqueued", resource = "worker-control-plane", outcome = "Succeeded" }))
    ];
}

static SyntheticPerson? FindByIdentityToken(TaxNetState state, string identityToken)
    => state.People.FirstOrDefault(x => x.IdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase));

static object BuildSandboxProfile(TaxNetState state, SyntheticPerson person)
{
    return new
    {
        person,
        identity = new
        {
            provider = "NADRA Sandbox",
            person.FullName,
            person.UrduName,
            person.FatherName,
            person.CnicMasked,
            person.PhoneMasked,
            person.IdentityToken
        },
        tax = state.TaxProfiles.Where(x => x.IdentityToken.Value == person.IdentityToken.Value),
        vehicles = state.Vehicles.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value),
        properties = state.Properties.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value),
        utilities = state.UtilityBills.Where(x => x.OwnerIdentityToken.Value == person.IdentityToken.Value),
        businesses = state.Businesses.Where(x => x.RelatedIdentityToken.Value == person.IdentityToken.Value),
        travel = state.Travel.Where(x => x.TravelerIdentityToken.Value == person.IdentityToken.Value)
    };
}
