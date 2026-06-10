using TaxNetGuardian.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;
});
builder.Services.AddSingleton(sp => new TaxNetState(sp.GetRequiredService<IWebHostEnvironment>()));
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

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

app.MapGet("/api/me", (HttpContext context) => Results.Ok(AuthorizationCatalog.CurrentUser(context)));

app.MapGet("/api/authz", () => Results.Ok(new
{
    mode = "Development header auth. Production target: Cognito User Pools for users, OAuth client credentials for services.",
    headerRole = "X-Demo-Role",
    headerUser = "X-Demo-User",
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

app.MapGet("/api/system/infra", (TaxNetState state) => Results.Ok(new
{
    mode = "Hackathon modular monolith with production service boundaries",
    stores = new[]
    {
        new { name = "PostgreSQL", mvp = "In-memory normalized collections", status = "SimulatedReady", replacement = "RDS PostgreSQL" },
        new { name = "Graph DB", mvp = "In-memory graph neighborhood builder", status = "SimulatedReady", replacement = "Neo4j/Memgraph/Neptune" },
        new { name = "Vector DB", mvp = $"Lexical RAG index with {state.RagChunks.Count} chunks", status = "SimulatedReady", replacement = "Qdrant or pgvector" },
        new { name = "S3", mvp = $"{state.ObjectStore.Count} object metadata records", status = "SimulatedReady", replacement = "S3 buckets" },
        new { name = "SQS", mvp = $"{state.Workers.Count} queue-backed worker contracts", status = "SimulatedReady", replacement = "SQS queues + DLQs" },
        new { name = "Secrets Manager", mvp = $"{state.Providers.Count} provider secret names configured", status = "ConfigReady", replacement = "AWS Secrets Manager" },
        new { name = "CloudWatch", mvp = $"{state.AuditEvents.Count} structured audit events", status = "LogReady", replacement = "CloudWatch Logs/Metrics/Alarms" }
    },
    environment = new
    {
        taxnetEnv = Environment.GetEnvironmentVariable("TAXNET_ENV") ?? "dev",
        awsRegion = Environment.GetEnvironmentVariable("AWS_REGION") ?? "ap-south-1",
        rawBucket = Environment.GetEnvironmentVariable("S3_BUCKET_RAW") ?? "taxnet-dev-raw-source-snapshots",
        reportBucket = Environment.GetEnvironmentVariable("S3_BUCKET_REPORTS") ?? "taxnet-dev-audit-reports",
        modelDefault = Environment.GetEnvironmentVariable("MODEL_GATEWAY_DEFAULT_PROVIDER") ?? "deterministic-template"
    }
}));

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

app.MapGet("/api/system/model-gateway", () => Results.Ok(new
{
    service = "TaxNet.AI.ModelGateway",
    routing = new[]
    {
        new { task = "AuditExplanation", route = "external-frontier-llm or redacted local model", reason = "quality and reasoning" },
        new { task = "CitizenExplanation", route = "local model or external with redaction", reason = "privacy-safe plain language" },
        new { task = "ReportDraft", route = "local model first", reason = "repeatable formatting" },
        new { task = "PolicyQuestion", route = "RAG + selected LLM", reason = "grounded citations" }
    },
    guardrails = new[] { "PII redaction", "evidence ID validation", "citation validation", "no fraud-proven language", "human review warning" },
    providers = new[] { "OpenAI", "Claude", "Gemini", "Local Ollama/vLLM", "Deterministic template fallback" }
}));

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

app.MapGet("/sandbox/nadra/identity/{identityToken}", (TaxNetState state, string identityToken) =>
{
    var person = FindByIdentityToken(state, identityToken);
    return person is null ? Results.NotFound() : Results.Ok(new
    {
        provider = "NADRA Sandbox",
        profile = new { person.FullName, person.UrduName, person.FatherName, person.CnicMasked, person.City, person.Province, person.IdentityToken }
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

app.MapGet("/sandbox/excise/vehicles", (TaxNetState state, string identityToken) =>
    Results.Ok(new { provider = "Excise Sandbox", items = state.Vehicles.Where(x => x.OwnerIdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase)) }));

app.MapGet("/sandbox/secp/companies", (TaxNetState state, string identityToken) =>
    Results.Ok(new { provider = "SECP Sandbox", items = state.Businesses.Where(x => x.RelatedIdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase)) }));

app.MapGet("/sandbox/property/ownership", (TaxNetState state, string identityToken) =>
    Results.Ok(new { provider = "Property Sandbox", items = state.Properties.Where(x => x.OwnerIdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase)) }));

app.MapGet("/sandbox/utilities/bills", (TaxNetState state, string identityToken) =>
    Results.Ok(new { provider = "Utility Sandbox", items = state.UtilityBills.Where(x => x.OwnerIdentityToken.Value.Equals(identityToken, StringComparison.OrdinalIgnoreCase)) }));

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
