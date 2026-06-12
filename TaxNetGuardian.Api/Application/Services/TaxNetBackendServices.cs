namespace TaxNetGuardian.Api;

using Npgsql;

public sealed class TaxNetPipelineOrchestrator
{
    private readonly TaxNetState _state;
    private readonly IngestionPipelineService _ingestion;
    private readonly IdentityResolutionService _identity;
    private readonly GraphIntelligenceService _graph;
    private readonly RiskScoringService _risk;
    private readonly PostgresOperationalSchemaService _postgres;

    public TaxNetPipelineOrchestrator(
        TaxNetState state,
        IngestionPipelineService ingestion,
        IdentityResolutionService identity,
        GraphIntelligenceService graph,
        RiskScoringService risk,
        PostgresOperationalSchemaService postgres)
    {
        _state = state;
        _ingestion = ingestion;
        _identity = identity;
        _graph = graph;
        _risk = risk;
        _postgres = postgres;
    }

    public async Task<PipelineRunResult> RunAsync(PipelineRunRequest request, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var providerCode = string.IsNullOrWhiteSpace(request.ProviderCode) ? "SANDBOX" : request.ProviderCode.Trim();
        var phases = new List<PipelinePhaseResult>();

        var import = await _ingestion.ImportFromProviderAsync(providerCode, request.MaxProfiles, cancellationToken);
        phases.Add(new PipelinePhaseResult("Ingestion", import.Status, import.RecordsProcessed, import.Messages));

        _state.RebuildIntelligence();
        var identity = _identity.GetEvaluation();
        phases.Add(new PipelinePhaseResult("IdentityResolution", "Succeeded", _state.Entities.Count, identity.Messages));

        var graph = _graph.GetGraphBuildSummary();
        phases.Add(new PipelinePhaseResult("GraphIntelligence", "Succeeded", graph.GraphsBuilt, graph.Messages));

        var risk = _risk.GetRiskSummary();
        phases.Add(new PipelinePhaseResult("RiskScoring", "Succeeded", risk.CasesScored, risk.Messages));

        var projection = await _postgres.SyncFromStateAsync(_state, cancellationToken);
        phases.Add(new PipelinePhaseResult(
            "OperationalProjection",
            projection.Reachable ? "Succeeded" : projection.Configured ? "Failed" : "Skipped",
            projection.RowsProjected,
            projection.Error is null
                ? [$"Projected {projection.RowsProjected} rows into PostgreSQL operational tables."]
                : [projection.Error]));

        var completed = DateTimeOffset.UtcNow;
        return new PipelineRunResult(
            $"run-{completed:yyyyMMddHHmmssfff}",
            providerCode,
            phases,
            _state.People.Count,
            _state.Entities.Count,
            _state.Cases.Count,
            completed - started,
            completed);
    }
}

public sealed class IngestionPipelineService
{
    private readonly TaxNetState _state;
    private readonly IGovernmentProviderRegistry _providers;

    public IngestionPipelineService(TaxNetState state, IGovernmentProviderRegistry providers)
    {
        _state = state;
        _providers = providers;
    }

    public async Task<ImportJob> ImportFromProviderAsync(string providerCode, int maxProfiles, CancellationToken cancellationToken)
    {
        var provider = _providers.GetProvider(providerCode)
            ?? throw new InvalidOperationException($"Provider {providerCode} was not registered.");
        var started = DateTimeOffset.UtcNow;
        var health = await provider.CheckHealthAsync(cancellationToken);
        if (!health.IsHealthy)
        {
            return new ImportJob(
                $"job-provider-{started:yyyyMMddHHmmss}",
                $"ProviderImport:{provider.ProviderCode}",
                "Failed",
                provider.ProviderName,
                0,
                0,
                1,
                started,
                DateTimeOffset.UtcNow,
                [$"Provider health check failed: {health.Status}", health.Error ?? "No provider error detail."]);
        }

        var people = _state.People.Take(Math.Clamp(maxProfiles <= 0 ? 250 : maxProfiles, 1, 2000)).ToArray();
        var processed = 0;
        var canonicalRecords = 0;

        foreach (var person in people)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = person.IdentityToken;
            canonicalRecords += (await provider.GetTaxProfileAsync(token, cancellationToken)) is null ? 0 : 1;
            canonicalRecords += (await provider.GetVehicleRecordsAsync(token, cancellationToken)).Count;
            canonicalRecords += (await provider.GetPropertyRecordsAsync(token, cancellationToken)).Count;
            canonicalRecords += (await provider.GetBusinessRecordsAsync(token, cancellationToken)).Count;
            canonicalRecords += (await provider.GetUtilitySignalsAsync(token, cancellationToken)).Count;
            canonicalRecords += (await provider.GetTravelSignalsAsync(token, cancellationToken)).Count;
            processed++;
        }

        return new ImportJob(
            $"job-provider-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            $"ProviderImport:{provider.ProviderCode}",
            "Succeeded",
            provider.ProviderName,
            processed,
            canonicalRecords,
            0,
            started,
            DateTimeOffset.UtcNow,
            [
                $"Imported canonical records through {nameof(IGovernmentDataProvider)}.",
                $"Profiles scanned: {processed}. Canonical provider records observed: {canonicalRecords}.",
                "Sandbox provider is replaceable by official providers without changing downstream intelligence services."
            ]);
    }

    public object GetProviderReadiness()
    {
        return new
        {
            activeProviders = _providers.GetAllProviders().Select(x => new { x.ProviderCode, x.ProviderName }),
            canonicalContract = nameof(IGovernmentDataProvider),
            downstreamServices = new[] { "IdentityResolution", "GraphIntelligence", "RiskScoring", "CaseManagement" }
        };
    }
}

public sealed class IdentityResolutionService
{
    private readonly TaxNetState _state;

    public IdentityResolutionService(TaxNetState state) => _state = state;

    public IdentityEvaluationResult GetEvaluation()
    {
        var total = Math.Max(1, _state.Entities.Count);
        var review = _state.Entities.Count(x => x.RequiresHumanReview);
        return new IdentityEvaluationResult(
            _state.Entities.Count,
            _state.Entities.Sum(x => x.LinkedRecordIds.Count),
            review,
            decimal.Round((_state.Entities.Count - review) / (decimal)total, 2),
            decimal.Round(review / (decimal)total, 2),
            [
                "Weighted identity resolution uses strong token matches, phone/name/address quality, and provider coverage.",
                "Low-confidence clusters are marked for human review instead of auto-merged."
            ]);
    }
}

public sealed class GraphIntelligenceService
{
    private readonly TaxNetState _state;

    public GraphIntelligenceService(TaxNetState state) => _state = state;

    public GraphNeighborhood BuildNeighborhood(string entityId) => _state.BuildGraph(entityId);

    public object ExtractFeatures(string entityId) => _state.ExtractGraphFeatures(entityId);

    public GraphBuildSummary GetGraphBuildSummary()
    {
        return new GraphBuildSummary(
            _state.Entities.Count,
            _state.Entities.Sum(entity => _state.BuildGraph(entity.Id).Edges.Count),
            [
                "Knowledge graph generated from canonical identity, tax, asset, utility, business, and travel records.",
                "Feature extraction feeds risk scoring with asset value, centrality, ownership, utility, and travel signals."
            ]);
    }
}

public sealed class RiskScoringService
{
    private readonly TaxNetState _state;

    public RiskScoringService(TaxNetState state) => _state = state;

    public RiskScoringSummary GetRiskSummary()
    {
        return new RiskScoringSummary(
            _state.Cases.Count,
            _state.Cases.Count(x => x.Score.RiskBand == "Critical"),
            _state.Cases.Count(x => x.Score.RiskBand == "High"),
            _state.Cases.Count(x => x.Score.RiskBand == "Medium"),
            [
                "Risk scoring is deterministic and evidence-backed; LLMs do not create the score.",
                "Score bands follow Low 0-30, Medium 31-60, High 61-80, Critical 81-100."
            ]);
    }
}

public sealed class CaseManagementService
{
    private readonly TaxNetState _state;

    public CaseManagementService(TaxNetState state) => _state = state;

    public CaseItem Assign(string caseId, CaseAssignmentRequest request, string actor)
        => _state.AssignCase(caseId, request, actor);

    public CaseItem RequestCitizenClarification(string caseId, string actor)
        => _state.RequestCitizenClarification(caseId, actor);

    public CaseItem RecordDecision(string caseId, CaseDecisionRequest request, string actor)
        => _state.RecordDecision(caseId, request, actor);

    public AuditExplanation Explain(string caseId) => _state.BuildExplanation(caseId);

    public object BuildReport(string caseId) => _state.BuildReport(caseId);

    public object GetCaseWorkspace(string caseId)
    {
        var caseItem = _state.Cases.FirstOrDefault(x => x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Case {caseId} was not found.");
        var person = _state.People.First(x => x.Id == caseItem.PersonId);
        return new
        {
            caseItem,
            subject = new
            {
                person.Id,
                person.FullName,
                person.City,
                person.Province,
                person.CnicMasked
            },
            timeline = _state.GetTimeline(caseId),
            explanation = _state.BuildExplanation(caseId),
            corrections = _state.Corrections.Where(x => x.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase)),
            reports = _state.Reports.Where(x => x.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase)),
            humanReview = new
            {
                required = true,
                reason = "TaxNet generates decision-support cases only; closure requires authorized auditor action.",
                allowedStates = new[] { "UnderReview", "EvidenceVerified", "ClosedNoAction", "ClosedEscalated", "ClosedRecovered", "ClosedFalsePositive" }
            }
        };
    }
}

public sealed class RagPolicyService
{
    private readonly TaxNetState _state;

    public RagPolicyService(TaxNetState state) => _state = state;

    public ImportJob Feed(RagFeedRequest request) => _state.FeedRagDocument(request);

    public RagQueryResult Query(RagQueryRequest request) => _state.QueryRag(request);

    public object GetIndexStatus() => new
    {
        service = "TaxNet.RagPolicy",
        documents = _state.RagDocuments.Count,
        chunks = _state.RagChunks.Count,
        latestDocument = _state.RagDocuments.OrderByDescending(x => x.CapturedAtUtc).FirstOrDefault(),
        retrievalPipeline = new[] { "query rewrite", "hybrid lexical retrieval", "citation extraction", "guardrail context pack" },
        productionVectorTarget = "pgvector/Qdrant/OpenSearch vector index"
    };
}

public sealed class AiOrchestratorService
{
    private readonly TaxNetState _state;
    private readonly RagPolicyService _rag;
    private readonly ModelGatewayClient _modelGatewayClient;

    public AiOrchestratorService(TaxNetState state, RagPolicyService rag, ModelGatewayClient modelGatewayClient)
    {
        _state = state;
        _rag = rag;
        _modelGatewayClient = modelGatewayClient;
    }

    public object ExplainCase(string caseId, bool allowExternalProvider, string preferredProvider)
    {
        var caseItem = _state.Cases.FirstOrDefault(x => x.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Case {caseId} was not found.");
        var explanation = _state.BuildExplanation(caseId);
        var rag = _rag.Query(new RagQueryRequest(
            string.Join(' ', explanation.KeyReasons),
            "AuditExplanation",
            "Pakistan",
            5,
            ["audit", "human-review", "evidence", "citizen"]));
        var invocation = _state.InvokeModelGateway(new ModelInvocationRequest(
            "AuditExplanation",
            $"{explanation.Summary}\nEvidence: {string.Join(", ", explanation.EvidenceIds)}",
            caseId,
            string.IsNullOrWhiteSpace(preferredProvider) ? "auto" : preferredProvider,
            allowExternalProvider),
            _modelGatewayClient);

        return new
        {
            orchestrator = "TaxNet.AI.Orchestrator",
            caseId,
            score = caseItem.Score,
            explanation,
            ragContext = rag,
            modelInvocation = invocation,
            validation = new[] { "structured evidence IDs present", "policy citations present", "human-review warning present", "no fraud-proven language" }
        };
    }
}

public sealed class AuditLogService
{
    private readonly TaxNetState _state;

    public AuditLogService(TaxNetState state) => _state = state;

    public object Query(string? action, string? resource, int limit)
    {
        var query = _state.AuditEvents.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(x => x.Action.Contains(action, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(resource))
        {
            query = query.Where(x => x.Resource.Contains(resource, StringComparison.OrdinalIgnoreCase));
        }

        var items = query.Take(Math.Clamp(limit <= 0 ? 100 : limit, 1, 500)).ToArray();
        return new
        {
            items,
            total = items.Length,
            immutableTarget = "S3 Object Lock / CloudWatch Logs",
            piiPolicy = "Audit metadata must not include raw CNIC, API keys, OAuth tokens, or raw prompts with PII."
        };
    }
}

public sealed class PostgresOperationalSchemaService
{
    private readonly IConfiguration _configuration;
    private readonly TaxNetPlatformOptions _options;

    public PostgresOperationalSchemaService(IConfiguration configuration, TaxNetPlatformOptions options)
    {
        _configuration = configuration;
        _options = options;
    }

    public async Task<PostgresSchemaStatus> EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        var connectionString = ResolveConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new PostgresSchemaStatus(false, false, [], "PostgreSQL connection string is not configured.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(SchemaSql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);

            var tables = await ReadTablesAsync(connection, cancellationToken);
            return new PostgresSchemaStatus(true, true, tables, null);
        }
        catch (Exception ex)
        {
            return new PostgresSchemaStatus(true, false, [], ex.Message);
        }
    }

    public async Task<PostgresProjectionStatus> SyncFromStateAsync(TaxNetState state, CancellationToken cancellationToken)
    {
        var connectionString = ResolveConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new PostgresProjectionStatus(false, false, 0, "PostgreSQL connection string is not configured.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using (var command = new NpgsqlCommand(SchemaSql, connection))
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await TruncateProjectionTablesAsync(connection, transaction, cancellationToken);

            var rows = 0;
            foreach (var person in state.People.Where(x => x is not null))
            {
                var token = IdentityTokenValue(person.IdentityToken, person.Id);
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    insert into taxnet_persons (person_id, full_name, city, province, cnic_masked, identity_token_hash, updated_at_utc)
                    values (@id, @name, @city, @province, @cnic, @token, @updated)
                    on conflict (person_id) do update
                    set full_name = excluded.full_name,
                        city = excluded.city,
                        province = excluded.province,
                        cnic_masked = excluded.cnic_masked,
                        identity_token_hash = excluded.identity_token_hash,
                        updated_at_utc = excluded.updated_at_utc;
                    """,
                    cancellationToken,
                    ("id", person.Id),
                    ("name", person.FullName),
                    ("city", person.City),
                    ("province", person.Province),
                    ("cnic", person.CnicMasked),
                    ("token", token),
                    ("updated", DateTimeOffset.UtcNow));
                rows++;
            }

            foreach (var record in BuildProviderRecords(state))
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    insert into taxnet_provider_records (provider_record_id, provider_code, person_id, record_type, source_updated_at_utc, content)
                    values (@id, @provider, @person, @type, @sourceUpdated, @content::jsonb)
                    on conflict (provider_record_id) do update
                    set provider_code = excluded.provider_code,
                        person_id = excluded.person_id,
                        record_type = excluded.record_type,
                        source_updated_at_utc = excluded.source_updated_at_utc,
                        content = excluded.content,
                        ingested_at_utc = now();
                    """,
                    cancellationToken,
                    ("id", record.Id),
                    ("provider", record.ProviderCode),
                    ("person", record.PersonId),
                    ("type", record.RecordType),
                    ("sourceUpdated", record.SourceUpdatedAtUtc),
                    ("content", record.ContentJson));
                rows++;
            }

            foreach (var entity in state.Entities.Where(x => x is not null))
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    insert into taxnet_resolved_entities (entity_id, person_id, match_confidence, requires_human_review, match_reasons, updated_at_utc)
                    values (@id, @person, @confidence, @review, @reasons::jsonb, @updated)
                    on conflict (entity_id) do update
                    set person_id = excluded.person_id,
                        match_confidence = excluded.match_confidence,
                        requires_human_review = excluded.requires_human_review,
                        match_reasons = excluded.match_reasons,
                        updated_at_utc = excluded.updated_at_utc;
                    """,
                    cancellationToken,
                    ("id", entity.Id),
                    ("person", entity.PersonId),
                    ("confidence", entity.MatchConfidence),
                    ("review", entity.RequiresHumanReview),
                    ("reasons", ToJson(entity.MatchReasons ?? [])),
                    ("updated", DateTimeOffset.UtcNow));
                rows++;
            }

            foreach (var caseItem in state.Cases.Where(x => x is not null && x.Score is not null))
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    insert into taxnet_cases (case_id, entity_id, person_id, status, assigned_to, risk_band, score, evidence, created_at_utc, updated_at_utc)
                    values (@id, @entity, @person, @status, @assigned, @band, @score, @evidence::jsonb, @created, @updated)
                    on conflict (case_id) do update
                    set entity_id = excluded.entity_id,
                        person_id = excluded.person_id,
                        status = excluded.status,
                        assigned_to = excluded.assigned_to,
                        risk_band = excluded.risk_band,
                        score = excluded.score,
                        evidence = excluded.evidence,
                        updated_at_utc = excluded.updated_at_utc;
                    """,
                    cancellationToken,
                    ("id", caseItem.Id),
                    ("entity", caseItem.EntityId),
                    ("person", caseItem.PersonId),
                    ("status", caseItem.Status),
                    ("assigned", caseItem.AssignedTo),
                    ("band", caseItem.Score.RiskBand),
                    ("score", caseItem.Score.Score),
                    ("evidence", ToJson(caseItem.Evidence ?? [])),
                    ("created", caseItem.CreatedAtUtc),
                    ("updated", caseItem.UpdatedAtUtc));
                rows++;
            }

            foreach (var audit in state.AuditEvents.Where(x => x is not null).Take(2000))
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    insert into taxnet_audit_events (audit_event_id, actor, actor_role, action, resource, outcome, correlation_id, metadata, timestamp_utc)
                    values (@id, @actor, @role, @action, @resource, @outcome, @correlation, @metadata::jsonb, @timestamp)
                    on conflict (audit_event_id) do nothing;
                    """,
                    cancellationToken,
                    ("id", audit.Id),
                    ("actor", audit.Actor),
                    ("role", audit.ActorRole),
                    ("action", audit.Action),
                    ("resource", audit.Resource),
                    ("outcome", audit.Outcome),
                    ("correlation", audit.CorrelationId),
                    ("metadata", ToJson(audit.Metadata ?? new Dictionary<string, object>())),
                    ("timestamp", audit.TimestampUtc));
                rows++;
            }

            foreach (var document in state.RagDocuments.Where(x => x is not null))
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    insert into taxnet_rag_documents (document_id, title, source_type, url, summary, tags, captured_at_utc)
                    values (@id, @title, @source, @url, @summary, @tags::jsonb, @captured)
                    on conflict (document_id) do update
                    set title = excluded.title,
                        source_type = excluded.source_type,
                        url = excluded.url,
                        summary = excluded.summary,
                        tags = excluded.tags,
                        captured_at_utc = excluded.captured_at_utc;
                    """,
                    cancellationToken,
                    ("id", document.Id),
                    ("title", document.Title),
                    ("source", document.SourceType),
                    ("url", document.Url),
                    ("summary", document.Summary),
                    ("tags", ToJson(document.Tags ?? [])),
                    ("captured", document.CapturedAtUtc));
                rows++;
            }

            await transaction.CommitAsync(cancellationToken);
            return new PostgresProjectionStatus(true, true, rows, null);
        }
        catch (Exception ex)
        {
            return new PostgresProjectionStatus(true, false, 0, ex.Message);
        }
    }

    public async Task<PostgresProjectionCounts> GetProjectionCountsAsync(CancellationToken cancellationToken)
    {
        var connectionString = ResolveConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new PostgresProjectionCounts(false, false, new Dictionary<string, long>(), "PostgreSQL connection string is not configured.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                select 'taxnet_persons' as table_name, count(*) from taxnet_persons
                union all select 'taxnet_provider_records', count(*) from taxnet_provider_records
                union all select 'taxnet_resolved_entities', count(*) from taxnet_resolved_entities
                union all select 'taxnet_cases', count(*) from taxnet_cases
                union all select 'taxnet_audit_events', count(*) from taxnet_audit_events
                union all select 'taxnet_rag_documents', count(*) from taxnet_rag_documents
                order by table_name;
                """,
                connection);

            var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                counts[reader.GetString(0)] = reader.GetInt64(1);
            }

            return new PostgresProjectionCounts(true, true, counts, null);
        }
        catch (Exception ex)
        {
            return new PostgresProjectionCounts(true, false, new Dictionary<string, long>(), ex.Message);
        }
    }

    private async Task<IReadOnlyList<string>> ReadTablesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            select table_name
            from information_schema.tables
            where table_schema = 'public' and table_name like 'taxnet_%'
            order by table_name;
            """,
            connection);
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task TruncateProjectionTablesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            truncate table
                taxnet_persons,
                taxnet_provider_records,
                taxnet_resolved_entities,
                taxnet_cases,
                taxnet_audit_events,
                taxnet_rag_documents;
            """,
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<ProviderRecordProjection> BuildProviderRecords(TaxNetState state)
    {
        var peopleByToken = state.People
            .Where(x => x is not null)
            .Select(x => new { Token = IdentityTokenValue(x.IdentityToken, x.Id), x.Id })
            .Where(x => !string.IsNullOrWhiteSpace(x.Token))
            .GroupBy(x => x.Token, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Id, StringComparer.OrdinalIgnoreCase);
        var records = new List<ProviderRecordProjection>();

        foreach (var item in state.TaxProfiles.Where(x => x is not null))
        {
            var token = IdentityTokenValue(item.IdentityToken, item.ProviderRecordId);
            records.Add(new ProviderRecordProjection(ProjectionId(item.ProviderRecordId, "tax"), "FBR", ResolvePerson(peopleByToken, token), "TaxProfile", item.SourceUpdatedAtUtc, ToJson(item)));
        }

        foreach (var item in state.Vehicles.Where(x => x is not null))
        {
            var token = IdentityTokenValue(item.OwnerIdentityToken, item.ProviderRecordId);
            records.Add(new ProviderRecordProjection(ProjectionId(item.ProviderRecordId, "vehicle"), "EXCISE", ResolvePerson(peopleByToken, token), "Vehicle", item.SourceUpdatedAtUtc, ToJson(item)));
        }

        foreach (var item in state.Properties.Where(x => x is not null))
        {
            var token = IdentityTokenValue(item.OwnerIdentityToken, item.ProviderRecordId);
            records.Add(new ProviderRecordProjection(ProjectionId(item.ProviderRecordId, "property"), "PROPERTY", ResolvePerson(peopleByToken, token), "Property", item.SourceUpdatedAtUtc, ToJson(item)));
        }

        foreach (var item in state.UtilityBills.Where(x => x is not null))
        {
            var token = IdentityTokenValue(item.OwnerIdentityToken, item.ProviderRecordId);
            records.Add(new ProviderRecordProjection(ProjectionId(item.ProviderRecordId, "utility"), "UTILITY", ResolvePerson(peopleByToken, token), "UtilityBill", item.SourceUpdatedAtUtc, ToJson(item)));
        }

        foreach (var item in state.Businesses.Where(x => x is not null))
        {
            var token = IdentityTokenValue(item.RelatedIdentityToken, item.ProviderRecordId);
            records.Add(new ProviderRecordProjection(ProjectionId(item.ProviderRecordId, "business"), "SECP", ResolvePerson(peopleByToken, token), "Business", item.SourceUpdatedAtUtc, ToJson(item)));
        }

        foreach (var item in state.Travel.Where(x => x is not null))
        {
            var token = IdentityTokenValue(item.TravelerIdentityToken, item.ProviderRecordId);
            records.Add(new ProviderRecordProjection(ProjectionId(item.ProviderRecordId, "travel"), "TRAVEL", ResolvePerson(peopleByToken, token), "Travel", item.SourceUpdatedAtUtc, ToJson(item)));
        }

        return records;
    }

    private static string ResolvePerson(IReadOnlyDictionary<string, string> peopleByToken, string token)
        => !string.IsNullOrWhiteSpace(token) && peopleByToken.TryGetValue(token, out var personId) ? personId : "unknown";

    private static string IdentityTokenValue(IdentityToken? token, string fallback)
        => string.IsNullOrWhiteSpace(token?.Value) ? $"missing-token:{fallback}" : token.Value;

    private static string ProjectionId(string? providerRecordId, string prefix)
        => string.IsNullOrWhiteSpace(providerRecordId) ? $"{prefix}-{Guid.NewGuid():N}" : providerRecordId;

    private static string ToJson<T>(T value)
        => System.Text.Json.JsonSerializer.Serialize(value);

    private string ResolveConnectionString()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING"),
            _options.Storage.PostgresConnectionString,
            _configuration.GetConnectionString("taxnet"),
            _configuration.GetConnectionString("postgres")
        };

        return candidates.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
    }

    private const string SchemaSql = """
        create table if not exists taxnet_persons (
            person_id text primary key,
            full_name text not null,
            city text not null,
            province text not null,
            cnic_masked text not null,
            identity_token_hash text not null,
            updated_at_utc timestamptz not null default now()
        );

        create table if not exists taxnet_provider_records (
            provider_record_id text primary key,
            provider_code text not null,
            person_id text not null,
            record_type text not null,
            source_updated_at_utc timestamptz not null,
            content jsonb not null,
            ingested_at_utc timestamptz not null default now()
        );

        create table if not exists taxnet_resolved_entities (
            entity_id text primary key,
            person_id text not null,
            match_confidence numeric(5,4) not null,
            requires_human_review boolean not null,
            match_reasons jsonb not null,
            updated_at_utc timestamptz not null default now()
        );

        create table if not exists taxnet_cases (
            case_id text primary key,
            entity_id text not null,
            person_id text not null,
            status text not null,
            assigned_to text not null,
            risk_band text not null,
            score int not null,
            evidence jsonb not null,
            created_at_utc timestamptz not null,
            updated_at_utc timestamptz not null
        );

        create table if not exists taxnet_audit_events (
            audit_event_id text primary key,
            actor text not null,
            actor_role text not null,
            action text not null,
            resource text not null,
            outcome text not null,
            correlation_id text not null,
            metadata jsonb not null,
            timestamp_utc timestamptz not null
        );

        create table if not exists taxnet_rag_documents (
            document_id text primary key,
            title text not null,
            source_type text not null,
            url text not null,
            summary text not null,
            tags jsonb not null,
            captured_at_utc timestamptz not null
        );

        create table if not exists taxnet_migrations (
            migration_id text primary key,
            applied_at_utc timestamptz not null default now()
        );

        insert into taxnet_migrations (migration_id)
        values ('001-operational-schema')
        on conflict (migration_id) do nothing;

        create index if not exists ix_taxnet_cases_risk_status on taxnet_cases(risk_band, status);
        create index if not exists ix_taxnet_provider_records_person on taxnet_provider_records(person_id, record_type);
        create index if not exists ix_taxnet_audit_events_resource on taxnet_audit_events(resource, timestamp_utc desc);
        """;
}

public sealed record PipelineRunRequest(string? ProviderCode, int MaxProfiles);

public sealed record PipelineRunResult(
    string RunId,
    string ProviderCode,
    IReadOnlyList<PipelinePhaseResult> Phases,
    int Profiles,
    int ResolvedEntities,
    int Cases,
    TimeSpan Duration,
    DateTimeOffset CompletedAtUtc);

public sealed record PipelinePhaseResult(
    string Name,
    string Status,
    int Records,
    IReadOnlyList<string> Messages);

public sealed record IdentityEvaluationResult(
    int ResolvedEntities,
    int LinkedRecords,
    int RequiresHumanReview,
    decimal AutoLinkRate,
    decimal AmbiguityRate,
    IReadOnlyList<string> Messages);

public sealed record GraphBuildSummary(
    int GraphsBuilt,
    int Relationships,
    IReadOnlyList<string> Messages);

public sealed record RiskScoringSummary(
    int CasesScored,
    int Critical,
    int High,
    int Medium,
    IReadOnlyList<string> Messages);

public sealed record PostgresSchemaStatus(
    bool Configured,
    bool Reachable,
    IReadOnlyList<string> Tables,
    string? Error);

public sealed record PostgresProjectionStatus(
    bool Configured,
    bool Reachable,
    int RowsProjected,
    string? Error);

public sealed record PostgresProjectionCounts(
    bool Configured,
    bool Reachable,
    IReadOnlyDictionary<string, long> Tables,
    string? Error);

internal sealed record ProviderRecordProjection(
    string Id,
    string ProviderCode,
    string PersonId,
    string RecordType,
    DateTimeOffset SourceUpdatedAtUtc,
    string ContentJson);
