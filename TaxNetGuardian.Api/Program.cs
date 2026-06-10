using TaxNetGuardian.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;
});
builder.Services.AddSingleton<TaxNetState>();
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
        corrections = state.Corrections.Where(x => x.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase))
    });
});

app.MapGet("/api/graph/entities/{entityId}/neighborhood", (TaxNetState state, string entityId) =>
{
    var graph = state.BuildGraph(entityId);
    return graph.Nodes.Count == 0 ? Results.NotFound(new { message = $"Entity {entityId} was not found." }) : Results.Ok(graph);
});

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
    }
}));

app.MapGet("/api/system/rag", (TaxNetState state) => Results.Ok(new
{
    service = "TaxNet.RagPolicy",
    purpose = "Current policy memory for explanations, citations, and audit guidance.",
    documents = state.RagDocuments,
    queryPipeline = new[] { "query rewrite", "hybrid retrieval", "rerank", "date/jurisdiction filter", "context pack", "citations" }
}));

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
    state.AddCorrection(request);
    return Results.Ok(new
    {
        message = "Correction submitted for human review.",
        correctionId = $"corr-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
        request.CaseId,
        status = "Submitted"
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
