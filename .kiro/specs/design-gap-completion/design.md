# Design Document: Design Gap Completion

## Overview

This design closes the gaps between the TaxNet Guardian implementation and the design documents in `docs/`. It is **gap-closing work on an existing, fairly complete .NET modular-monolith codebase**, not a greenfield build. The single-host modular-monolith deployment is intentional (design §4A); nothing here splits services into physical microservices. Every new behavior is added behind the existing HTTP contracts in `TaxNetGuardian.Api/Program.cs`, the existing worker runtime in `TaxNetGuardian.Worker.Shared`, the existing authorization model in `Application/Security/AuthorizationCatalog.cs`, and the existing in-memory `TaxNetState` store persisted via `SaveSnapshot()`.

The feature delivers eight requirements:

1. **Public Data Connector Worker** — a new `TaxNetGuardian.Workers.PublicDataConnector` that fetches approved public documents, snapshots them to S3, and submits extracted text to RAG.
2. **Notification Service Worker** — a new `TaxNetGuardian.Workers.Notification` that consumes a notification queue and delivers in-app notifications, replacing the inline flush in `TaxNetState.RunWorkerCycle`.
3. **Sandbox Failure & Latency Simulator** — operator-configurable failure rules enforced on Sandbox_Provider reads.
4. **Sandbox Profile Editing & Asset Authoring** — `PATCH` profile and `POST` asset endpoints with validation and expected-risk-band marking.
5. **Embedding-Based RAG with Pluggable Vector Store** — `IVectorStore` + `IEmbeddingProvider` abstractions with a deterministic fallback and an embedding indexing path.
6. **Explainability Evidence Guardrail** — a claim-level "no claim without evidence" validation boundary on generated explanations.
7. **Worker Runtime Consistency** — the new workers follow the existing File/LocalStack runtime conventions exactly.
8. **Authentication & Authorization Integration** — the new endpoints reuse `AuthorizationCatalog` path policies, longest-prefix matching, and the `taxnet-admin` super-role.

### Design Principles (anchored to the real code)

- **`TaxNetState` is the single source of truth.** All new mutable state (failure rules, expected risk bands, assets, vector entries, guardrail outcomes) is added as collections/fields on the `TaxNetState` partial class and serialized through `TaxNetSnapshot`. Every mutation takes `lock (_lock)`, calls the existing private `AddAuditEvent(...)`, and ends with `SaveSnapshot()`.
- **Workers stay thin.** Each new worker is a tiny `Program.cs` that calls `WorkerOptions.FromEnvironment(...)` then `WorkerHost.RunAsync(options, handler)`, exactly like `TaxNetGuardian.Workers.RagPolicy/Program.cs`. Business logic lives in the API and is reached through `WorkerContext.PostApiJsonAsync` / `GetApiAsync`.
- **The API owns all logic; workers own orchestration.** Hashing, validation, guardrail evaluation, vector retrieval, and failure-rule selection are implemented as `TaxNetState` methods (testable in-process) and exposed via minimal-API endpoints. Workers only move messages and call those endpoints.
- **Authorization is centralized.** New endpoints rely on `AuthorizationCatalog` path-prefix policies. `/sandbox/admin` is already mapped to `["taxnet-admin","taxnet-sandbox-admin","taxnet-data-engineer"]`; new sandbox endpoints inherit it automatically through the global middleware in `Program.cs`.

---

## Implementation Flow (Start to End)

This section is the recommended build order. Each step keeps the solution compiling and the app runnable. Do the steps in order; later workers depend on the API contracts added in earlier steps.

### How to run the system

**API (File mode — default, fully offline):**
```powershell
dotnet run --project TaxNetGuardian.Api
# Serves on http://localhost:5191 (see WorkerOptions.ApiBaseUrl default and launchSettings.json)
```

**A worker, one cycle then exit (File mode default):**
```powershell
dotnet run --project TaxNetGuardian.Workers.Notification
```

**A worker, continuous polling:**
```powershell
dotnet run --project TaxNetGuardian.Workers.Notification -- --watch
```

**LocalStack mode (queues + object store over SQS/S3):**
```powershell
# 1. bring up LocalStack + provision queues/buckets
docker compose -f docker-compose.localstack.yml up -d
./scripts/localstack-terraform-apply.ps1     # applies infra/localstack (add new queues first — Step 1)
# 2. run API and workers with mode env vars
$env:TAXNET_QUEUE_MODE="LocalStack"; $env:TAXNET_OBJECT_STORE_MODE="LocalStack"; $env:LOCALSTACK_ENDPOINT="http://localhost:4566"
dotnet run --project TaxNetGuardian.Api
$env:TAXNET_QUEUE_MODE="LocalStack"; $env:TAXNET_OBJECT_STORE_MODE="LocalStack"
dotnet run --project TaxNetGuardian.Workers.PublicDataConnector -- --watch
```

`TAXNET_QUEUE_MODE` and `TAXNET_OBJECT_STORE_MODE` are evaluated **independently**; each defaults to `File` when unset or invalid (handled today by `WorkerOptions.FromEnvironment`). Verify which mode a worker chose from its startup line: `... queueMode=File objectStore=File`.

### Build sequence

**Step 0 — Baseline.** Confirm the solution builds and the API runs:
```powershell
dotnet build TaxNetGuardian.slnx
dotnet run --project TaxNetGuardian.Api   # GET /api/health -> 200
```

**Step 1 — Infra names (Req 7).** Add the three new queue names to `infra/localstack/main.tf` `local.queue_names` (`taxnet-dev-public-data-connector-jobs`, `taxnet-dev-notification-jobs`, `taxnet-dev-embedding-jobs`) and a CloudWatch log group for each. File mode needs no provisioning (directories are created on demand), so the app stays runnable immediately. Verify: `terraform -chdir=infra/localstack plan` lists the new queues.

**Step 2 — Shared data models + snapshot (Reqs 3,4,5,6).** Add the new domain records (`FailureRule`, `FailureRuleRequest`, vector/guardrail records) and extend `TaxNetSnapshot` + `ApplySnapshot`/`SaveSnapshot` with the new collections. Build. Verify: API starts, `GET /api/system/persistence` still returns and the new (empty) collections round-trip through a save/load.

**Step 3 — Sandbox Failure & Latency Simulator (Req 3).** Add failure-rule state + endpoints + the `SandboxFailureSimulator` enforcement boundary on provider reads. Verify with `POST /sandbox/admin/failure-rules` then a read against the targeted provider.

**Step 4 — Sandbox Profile Editing & Asset Authoring (Req 4).** Add `PATCH /sandbox/admin/profiles/{id}` and `POST /sandbox/admin/profiles/{id}/assets`. Verify with curl/HTTP file against a seeded `syntheticPersonId` from `GET /sandbox/admin/profiles`.

**Step 5 — Embedding RAG + pluggable Vector Store (Req 5).** Introduce `IVectorStore` + `IEmbeddingProvider`, the deterministic fallback, wire them into `FeedRagDocument`/`QueryRag`, and add the retrieval-path indicator + citation metadata to the result. Verify `POST /api/system/rag/query` returns `retrievalPath: "deterministic_fallback"` offline.

**Step 6 — Explainability Evidence Guardrail (Req 6).** Add the claim-level guardrail evaluator and the guarded explanation endpoint. Verify `POST /api/orchestrator/cases/{caseId}/explain-guarded` returns grounded/ungrounded counts that sum to total.

**Step 7 — Public Data Connector Worker (Reqs 1,7).** Add the new worker project, register it in `TaxNetGuardian.slnx`, add the connector API contract (`POST /api/connectors/public-data/fetch`), and add the worker to `SeedWorkers()`. Verify with `POST /api/system/workers/enqueue` to `taxnet-dev-public-data-connector-jobs` then run the worker once.

**Step 8 — Notification Service Worker (Reqs 2,7).** Add the worker project, register it, add the notification delivery contract (`POST /api/system/notifications/{id}/deliver`), add it to `SeedWorkers()`, and switch `RunWorkerCycle` to enqueue rather than inline-flush. Verify a `Queued` notification transitions to `Sent` exactly once.

**Step 9 — Authz + audit sweep (Req 8).** Confirm every new endpoint matches the intended `AuthorizationCatalog` policy (longest-prefix), records the resolved actor in its audit event, and that `taxnet-admin` overrides. Add any missing path policy entries (none expected for `/sandbox/admin`; add `/api/connectors/public-data` if a narrower policy than `/api/connectors` is desired).

**Step 10 — Tests.** Add the unit + property tests described in the Testing Strategy and run them.

---

## Architecture

### Where the new pieces live

```
TaxNetGuardian.Api/
  Program.cs                                  # + new minimal-API endpoints (Reqs 1,2,3,4,5,6)
  Application/
    Sandbox/   TaxNetState.FailureRules.cs    # NEW (Req 3) failure-rule CRUD + evaluation
    Sandbox/   TaxNetState.ProfileEditing.cs  # NEW (Req 4) PATCH/asset logic
    Rag/       TaxNetState.Rag.cs             # MODIFIED (Req 5) route through IVectorStore
    Explainability/ TaxNetState.Guardrail.cs  # NEW (Req 6) claim-level guardrail
    Operations/ TaxNetState.Operations.cs     # MODIFIED (Req 2) RunWorkerCycle enqueues
    PublicData/ TaxNetState.PublicData.cs     # NEW (Req 1) approved-source policy, hashing, ingest
    Notifications/ TaxNetState.Notifications.cs # NEW (Req 2) deliver/idempotency
  Infrastructure/
    Sandbox/   SandboxFailureSimulator.cs     # NEW (Req 3) IGovernmentDataProvider decorator
    Rag/       IVectorStore.cs                # NEW (Req 5) vector store + embedding abstractions
    Rag/       InMemoryVectorStore.cs         # NEW (Req 5) default impl
    Rag/       DeterministicEmbeddingProvider.cs # NEW (Req 5) offline fallback
  Domain/
    Sandbox/   FailureRuleModels.cs           # NEW (Req 3)
    Rag/       VectorModels.cs                # NEW (Req 5)
    Explainability/ GuardrailModels.cs        # NEW (Req 6)
    PublicData/ PublicSourceModels.cs         # NEW (Req 1)
  Infrastructure/Persistence/TaxNetSnapshot.cs # MODIFIED add new collections

TaxNetGuardian.Workers.PublicDataConnector/   # NEW worker project (Reqs 1,7)
TaxNetGuardian.Workers.Notification/          # NEW worker project (Reqs 2,7)
TaxNetGuardian.slnx                           # + 2 (optionally 3) new <Project> entries
infra/localstack/main.tf                      # + 3 queue names + log groups (Req 7)
```

### Component interaction (failure simulator + workers)

```mermaid
flowchart TB
    subgraph Workers["Worker executables (TaxNetGuardian.Worker.Shared runtime)"]
        PDC["PublicDataConnector.Worker<br/>queue: taxnet-dev-public-data-connector-jobs"]
        NTF["Notification.Worker<br/>queue: taxnet-dev-notification-jobs"]
    end
    subgraph Infra["Runtime infra (File default | LocalStack)"]
        Q[(SQS / file queues)]
        OS[(S3 / file object store)]
    end
    subgraph Api["TaxNetGuardian.Api (modular monolith)"]
        EP["Minimal API endpoints"]
        AUTH["AuthorizationCatalog<br/>path-prefix policies"]
        STATE["TaxNetState (in-memory + snapshot)"]
        SIM["SandboxFailureSimulator<br/>(IGovernmentDataProvider decorator)"]
        VEC["IVectorStore + IEmbeddingProvider"]
        GUARD["Explainability Guardrail"]
        REG["GovernmentProviderRegistry"]
    end

    PDC -->|ReceiveAsync| Q
    NTF -->|ReceiveAsync| Q
    PDC -->|PutObject raw snapshot| OS
    PDC -->|POST /api/connectors/public-data/fetch| EP
    NTF -->|POST /api/system/notifications/{id}/deliver| EP
    EP --> AUTH
    AUTH --> STATE
    EP --> VEC
    EP --> GUARD
    REG --> SIM
    SIM --> STATE
    STATE -->|SaveSnapshot| OS
```

### Request authorization pipeline (Req 8)

Authorization is enforced by the existing global middleware in `Program.cs` that calls `AuthorizationCatalog.TryGetAccessDecision(context, out var decision)` and returns 403 when `!decision.Allowed`. The decision uses **longest-prefix matching** over `PathPolicies` and grants `taxnet-admin` unconditionally. New endpoints require **no per-endpoint role checks** as long as their path falls under an existing policy prefix:

- `/sandbox/admin/failure-rules*` and `/sandbox/admin/profiles/*` → matched by the existing `/sandbox/admin` policy.
- `/api/system/rag*` → matched by the existing `/api/system/rag` policy.
- `/api/connectors/public-data*` → matched by the existing `/api/connectors` policy (add a longer-prefix entry only if a narrower role set is wanted).
- The guarded explanation endpoint under `/api/orchestrator/...` currently has **no** policy prefix; see Req 8 design for the explicit decision to keep it consistent with the existing `/api/orchestrator/cases/{caseId}/explain` endpoint (no prefix policy today) or to add one.

Resolved actor identity for audit events comes from `AuthorizationCatalog.GetCurrentActor(context)` (JWT `sub` first, then `X-Demo-User`, then role), exactly as existing state-changing endpoints already do.

### Modular-monolith fidelity

No process is split out. The "Notification service" and "Public Data Connector service" named in design docs are realized as **worker executables that call the monolith API**, identical to the seven existing workers. The vector store, embedding provider, failure simulator, and guardrail are **in-process services** resolved from DI in the single API host. This matches the established pattern where `GovernmentProviderRegistry`, `RagPolicyService`, and `AiOrchestratorService` are all singletons in the one host.

---

## Components and Interfaces

Each subsection gives: **current state → target design → concrete changes (files, signatures, endpoints, audit shapes) → build order**, and maps back to the requirement's acceptance criteria (ACs). Note: the requirements document repeats numbers 2–6 in Requirement 1; this design treats Requirement 1 as having ACs 1–10 with the more specific (timeout/size) variants of 2 and 3 as authoritative.

### Req 1 — Public Data Connector Worker

**Current state.** No `TaxNetGuardian.Workers.PublicDataConnector` project exists. The raw-snapshots bucket `taxnet-dev-raw-source-snapshots` already exists and is already used by `FeedRagDocument` (`StoreObject("taxnet-dev-raw-source-snapshots", $"rag-source/{id}.txt", ...)`). The RAG indexing entry point is `TaxNetState.FeedRagDocument(RagFeedRequest)` exposed at `POST /api/system/rag/documents`.

**Target design.** A new worker consumes fetch-request envelopes from `taxnet-dev-public-data-connector-jobs`. For each request it (a) classifies the source against an approved-source policy, (b) fetches with a 30s timeout and 50MB cap, (c) stores the raw snapshot to S3 with provenance metadata, (d) extracts text, and (e) submits to the API RAG contract. The worker stays thin: classification, hashing, provenance recording, and audit are owned by a new API contract `POST /api/connectors/public-data/fetch`, so the logic is unit/property testable in-process. The worker performs the actual HTTP fetch and snapshot `PutObject` (network/IO belongs in the worker), then posts the fetched bytes' text + provenance to the API.

**New domain models** (`Domain/PublicData/PublicSourceModels.cs`):
```csharp
public sealed record PublicDataFetchRequest(
    string SourceUrl,
    string SourceType,          // e.g. "PublicTaxNotice", "PublicFeeSchedule", "PublicPolicyPdf"
    string? Title,
    IReadOnlyList<string>? Tags);

public sealed record PublicDataIngestRequest(   // worker -> API after it has fetched+stored
    string SourceUrl,
    string SourceType,
    string? Title,
    string ExtractedText,
    string ContentHash,         // SHA-256 hex of raw bytes
    string ParserVersion,       // e.g. "public-data-parser-v1.0"
    long RawSizeBytes,
    DateTimeOffset CapturedAtUtc,
    string RawSnapshotKey,      // S3 key under taxnet-dev-raw-source-snapshots
    IReadOnlyList<string>? Tags);

public enum PublicSourceOutcome { Indexed, Rejected, Failed, FailedExtraction }

public sealed record PublicDataFetchResult(
    string SourceUrl,
    PublicSourceOutcome Outcome,
    string? ContentHash,
    string? FailureReason,
    string? RagDocumentId,
    DateTimeOffset CompletedAtUtc);
```

**Approved-source policy** (pure function, `TaxNetState.PublicData.cs`): satisfies Req 1 AC 5, 6.
```csharp
public static class PublicSourcePolicy
{
    // Allowlisted host/path patterns for public notices, fee schedules, public company/market
    // data where permitted, public policy PDFs, public government press releases.
    public static bool IsApproved(string sourceUrl, out string reason);
    // Rejects: auth-required, captcha-gated, terms/privacy-restricted, citizen-verification pages.
}
```

**New `TaxNetState` methods** (`Application/PublicData/TaxNetState.PublicData.cs`):
```csharp
// Called by POST /api/connectors/public-data/fetch. Validates approval, records provenance,
// submits extracted text to RAG via the existing FeedRagDocument path, emits audit, snapshots.
public PublicDataFetchResult IngestPublicData(PublicDataIngestRequest request, string actor);

// Deterministic content hash used by both the worker and tests (Req 1 AC 7).
public static string ComputeContentHash(ReadOnlySpan<byte> rawBytes); // SHA-256 lowercase hex
```

`IngestPublicData` logic:
- If `!PublicSourcePolicy.IsApproved(url)` → return `Rejected`, emit audit `PublicDataFetch`/outcome `Rejected` with the disallowed URL, **do not** call `FeedRagDocument` (AC 5, 6).
- If `string.IsNullOrWhiteSpace(request.ExtractedText.Trim())` → return `FailedExtraction`, retain the raw snapshot (worker already stored it), emit audit outcome `FailedExtraction` (AC 10).
- Otherwise call `FeedRagDocument(new RagFeedRequest(title, sourceType, sourceUrl, extractedText, tags))`, capture the returned `ImportJob`/document id, emit audit outcome `Indexed` (AC 4, 8), `SaveSnapshot()`.

**New endpoint** (`Program.cs`):
| Method | Route | Auth policy | Request | Response |
|---|---|---|---|---|
| POST | `/api/connectors/public-data/fetch` | `/api/connectors` policy (`taxnet-admin`, `taxnet-sandbox-admin`, `taxnet-data-engineer`) | `PublicDataIngestRequest` | `PublicDataFetchResult` (200) or 400 validation |

**Worker `Program.cs`** (`TaxNetGuardian.Workers.PublicDataConnector/Program.cs`), mirrors `RagPolicy/Program.cs`:
```csharp
using System.Security.Cryptography;
using System.Text.Json;
using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("PublicDataConnector.Worker", "taxnet-dev-public-data-connector-jobs", args);
return await WorkerHost.RunAsync(options, new PublicDataConnectorWorker());

internal sealed class PublicDataConnectorWorker : IWorkerJobHandler
{
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);   // Req 1 AC 9
    private const long MaxSnapshotBytes = 50L * 1024 * 1024;                    // Req 1 AC 9
    private const string ParserVersion = "public-data-parser-v1.0";

    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken ct)
    {
        var req = JsonSerializer.Deserialize<PublicDataFetchEnvelope>(envelope.PayloadJson, context.JsonOptions)!;

        // Worker performs the network fetch (IO belongs in the worker). On any network error,
        // timeout, non-success status, or oversize, POST a Failed ingest so the API records it.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(FetchTimeout);
        byte[] raw;
        try
        {
            using var resp = await context.Http.GetAsync(req.SourceUrl, cts.Token);
            resp.EnsureSuccessStatusCode();
            raw = await resp.Content.ReadAsByteArrayAsync(cts.Token);
            if (raw.LongLength > MaxSnapshotBytes) throw new InvalidOperationException("Snapshot exceeds 50 MB cap.");
        }
        catch (Exception ex)
        {
            await context.PostApiJsonAsync("/api/connectors/public-data/fetch",
                FailedIngest(req, ex.Message), ct);   // API records Failed outcome (AC 9)
            return;
        }

        var hash = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();   // AC 3, 7
        var capturedAtUtc = DateTimeOffset.UtcNow;
        var key = $"public-data/{hash}/{envelope.Id}.bin";
        await context.Objects.PutObjectAsync("taxnet-dev-raw-source-snapshots", key,
            "application/octet-stream", Convert.ToBase64String(raw), ct);          // AC 2

        var text = ExtractText(raw);   // simple text/HTML/PDF-to-text extraction
        await context.PostApiJsonAsync("/api/connectors/public-data/fetch",
            new { req.SourceUrl, req.SourceType, req.Title, extractedText = text,
                  contentHash = hash, parserVersion = ParserVersion, rawSizeBytes = raw.LongLength,
                  capturedAtUtc, rawSnapshotKey = key, req.Tags }, ct);            // AC 4
    }
}
```

**Queue / bucket:** queue `taxnet-dev-public-data-connector-jobs`; raw bucket `taxnet-dev-raw-source-snapshots` (existing); failures bucket `taxnet-dev-worker-failures` (handled automatically by `WorkerHost`).

**Audit event shape** (via `AddAuditEvent`): actor = resolved actor, role = `taxnet-data-engineer`, action = `PublicDataFetch`, resource = source URL, outcome ∈ {`Indexed`,`Rejected`,`Failed`,`FailedExtraction`}, metadata `{ contentHash, sourceType, ragDocumentId?, failureReason? }` (AC 8).

**Worker health surface (Req 7 AC 8):** add to `SeedWorkers()`:
```csharp
new("PublicDataConnector.Worker", "taxnet-dev-public-data-connector-jobs", 0, 0, 0, "Idle", DateTimeOffset.UtcNow),
```

**Build order:** add models → add `PublicSourcePolicy` + `IngestPublicData` + endpoint → `SeedWorkers()` entry → worker project + `.slnx` entry → enqueue test. Satisfies Req 1 AC 1–10.

---

### Req 2 — Notification Service Worker

**Current state.** Notifications are created inline (e.g. `AddCorrection` inserts a `NotificationItem` with `Status="Queued"`). `RunWorkerCycle` flushes **all** queued notifications to `Sent` inline. `NotificationItem(Id, Recipient, Channel, Subject, Body, Status, CreatedAtUtc)` has only `Channel` (string) and `Status` ∈ {`Queued`,`Sent`}. `GET /api/system/notifications` exists. There is no standalone notification worker, no queue, no channel abstraction, and no idempotency guard beyond the "remove queued / re-insert as sent" loop.

**Target design.** A `taxnet-dev-notification-jobs` queue carries `{ notificationId }` jobs. The `Notification.Worker` consumes a job and calls `POST /api/system/notifications/{id}/deliver`. The API resolves the `NotificationItem`, routes it through a `INotificationChannel` abstraction (only `InAppNotificationChannel` implemented), sets `Queued → Sent` **exactly once**, and emits an audit event. `RunWorkerCycle` is changed to **enqueue** a delivery job per queued notification instead of flipping status inline (keeping a synchronous fallback flush only when queue mode cannot enqueue, to preserve the existing demo button behavior).

**Channel abstraction** (`Infrastructure/Notifications/INotificationChannel.cs`): satisfies Req 2 AC 5, 6.
```csharp
public sealed record NotificationDeliveryResult(
    bool Delivered, string ChannelUsed, bool RequestedChannelUnavailable, string? Error);

public interface INotificationChannel
{
    string Channel { get; }                          // "InApp", future: "Sns","Email","Sms"
    Task<NotificationDeliveryResult> DeliverAsync(NotificationItem item, CancellationToken ct);
}

public interface INotificationChannelRegistry
{
    INotificationChannel Resolve(string requestedChannel);   // falls back to InApp when unconfigured
}
```
`InAppNotificationChannel.DeliverAsync` is a no-op success (in-app means "visible via `GET /api/system/notifications`"). `NotificationChannelRegistry.Resolve(requested)` returns the matching channel or, when none is configured, the `InApp` channel with `RequestedChannelUnavailable = true` (AC 6).

**New `TaxNetState` method** (`Application/Notifications/TaxNetState.Notifications.cs`):
```csharp
// Idempotent delivery. Returns the (possibly unchanged) item + outcome. Reqs 2 AC 2,3,6,7,8,9,10.
public (NotificationItem? Item, NotificationDeliveryResult Result, string Outcome)
    DeliverNotification(string notificationId, INotificationChannelRegistry channels, string actor);

// Producer-side helper used by RunWorkerCycle and notification-producing code.
public IReadOnlyList<string> EnqueueQueuedNotificationIds(); // returns ids needing delivery
```
`DeliverNotification` logic:
- Find item by id. If missing → outcome `Rejected`, emit audit, **no state change** (AC 10).
- If `Status == "Sent"` → return unchanged, outcome `AlreadySent`, **do not** re-flip (AC 8).
- Else resolve channel, call `DeliverAsync`. On success → replace item with `Status="Sent"`, outcome `Sent`, emit audit `{ recipient, channel: result.ChannelUsed, requestedChannelUnavailable }` (AC 3, 4, 6, 7). On failure → leave `Queued`, outcome `Failed`, emit audit failed (AC 9).
- `SaveSnapshot()` on any state change.

**New endpoint:**
| Method | Route | Auth policy | Request | Response |
|---|---|---|---|---|
| POST | `/api/system/notifications/{id}/deliver` | `/api/system` policy | none (id in path) | `{ item, outcome, channelUsed, requestedChannelUnavailable }` |

`/api/system/notifications` already returns the list; no change needed for reads.

**`RunWorkerCycle` change (Req 2):** replace the inline `Notifications.RemoveAll(...)/Insert(Sent)` loop with: enqueue one `{ notificationId }` envelope per `Queued` item to `taxnet-dev-notification-jobs` using the same `BuildQueueClient`/`NewQueueEnvelope` helpers already in `Program.cs` (move those to a shared internal helper, or expose an `EnqueueNotificationJobs` method). The existing inline flush remains as the File-mode synchronous path so the demo "Run worker cycle" button still shows progress without a separately running worker.

**Worker `Program.cs`** (`TaxNetGuardian.Workers.Notification/Program.cs`):
```csharp
using System.Text.Json;
using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("Notification.Worker", "taxnet-dev-notification-jobs", args);
return await WorkerHost.RunAsync(options, new NotificationWorker());

internal sealed class NotificationWorker : IWorkerJobHandler
{
    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(envelope.PayloadJson);
        var id = doc.RootElement.GetProperty("notificationId").GetString();
        var resp = await context.PostApiJsonAsync($"/api/system/notifications/{id}/deliver", new { }, ct);
        resp.EnsureSuccessStatusCode();   // API is idempotent; re-delivery of a Sent item is a no-op 200
    }
}
```

**Queue:** `taxnet-dev-notification-jobs`. **Audit shape:** action `NotificationDelivered`, resource = notificationId, outcome ∈ {`Sent`,`AlreadySent`,`Failed`,`Rejected`}, metadata `{ recipient, channel, requestedChannelUnavailable }` (AC 7).

**Health surface:** add `new("Notification.Worker", "taxnet-dev-notification-jobs", 0, 0, 0, "Idle", DateTimeOffset.UtcNow)` to `SeedWorkers()`.

**Build order:** channel abstraction → `DeliverNotification` + endpoint → `RunWorkerCycle` enqueues → worker project + `.slnx` → `SeedWorkers()` entry. Satisfies Req 2 AC 1–10.

---

### Req 3 — Sandbox Failure and Latency Simulator

**Current state.** Provider reads have two paths: (a) the canonical pipeline path through `IGovernmentDataProvider` resolved by `GovernmentProviderRegistry` (used by `IngestionPipelineService`), and (b) the direct `/sandbox/{provider}/...` HTTP endpoints in `Program.cs` that read `state.Vehicles`, `state.TaxProfiles`, etc. directly. There are no failure rules and no latency injection anywhere. `ProviderDescriptor` lists provider codes; `state.Providers` is the source of valid provider codes (`SANDBOX`, plus NADRA/FBR/EXCISE/SECP/Property/Utility/Travel descriptors).

**Target design.** Failure rules live on `TaxNetState`. Enforcement happens at a single, shared evaluation point — `SandboxFailureSimulator` — which both the `IGovernmentDataProvider` decorator and the direct `/sandbox/*` HTTP endpoints consult. This guarantees "reads honor rules" on both read paths. The simulator selects the **most recently created active rule** for a provider (AC 13), injects latency (AC 12), and produces the behavior outcome (AC 7–11).

**New domain models** (`Domain/Sandbox/FailureRuleModels.cs`):
```csharp
public enum FailureBehavior { Offline, StaleData, PartialData, RateLimited, ServerError }

public sealed record FailureRuleRequest(
    string ProviderCode,
    string Behavior,                 // parsed case-insensitively into FailureBehavior
    int? InjectedLatencyMs);         // optional 0..60000

public sealed record FailureRule(
    string RuleId,
    string ProviderCode,
    FailureBehavior Behavior,
    int InjectedLatencyMs,
    bool Active,
    DateTimeOffset CreatedAtUtc);

public enum FailureApplication { None, Offline, StaleData, PartialData, RateLimited, ServerError }

public sealed record FailureDecision(
    FailureApplication Application,
    int InjectedLatencyMs,
    string? RuleId);                 // None when no active rule applies (AC 14)
```

**New `TaxNetState` methods** (`Application/Sandbox/TaxNetState.FailureRules.cs`):
```csharp
public List<FailureRule> FailureRules { get; } = [];   // also added to snapshot

// AC 1,2,3,4,15 — validate then create.
public (FailureRule? Rule, string? ValidationError) CreateFailureRule(FailureRuleRequest request, string actor);
// AC 5,6,15 — deactivate + remove; returns false when not found.
public bool DeleteFailureRule(string ruleId, string actor);
// AC 13,14 — most-recent active rule wins; None when no active rule.
public FailureDecision ResolveFailureDecision(string providerCode);
```
`CreateFailureRule` validation (AC 2, 4): provider code must match an existing `Providers` entry (or a known sandbox provider code); `Behavior` must parse to exactly one `FailureBehavior`; `InjectedLatencyMs`, if present, must be in `[0, 60000]`. On any violation → return `(null, error)` and **do not** mutate (AC 4). On success assign `RuleId = $"frule-{...}"`, `Active = true`, insert, emit audit, save (AC 1, 15).

`ResolveFailureDecision` (AC 13, 14): `FailureRules.Where(r => r.Active && r.ProviderCode == code).OrderByDescending(r => r.CreatedAtUtc).FirstOrDefault()`; map to `FailureDecision`; return `None` with `InjectedLatencyMs = 0` when no active rule.

**Enforcement boundary** (`Infrastructure/Sandbox/SandboxFailureSimulator.cs`):
```csharp
public sealed class SandboxFailureSimulator
{
    private readonly TaxNetState _state;
    public SandboxFailureSimulator(TaxNetState state) => _state = state;

    // Applies latency (Task.Delay >= InjectedLatencyMs, AC 12) then returns the decision.
    public async Task<FailureDecision> ApplyAsync(string providerCode, CancellationToken ct);

    // Transforms a normal record set per behavior:
    //   Offline      -> throws SandboxOfflineException (AC 7)
    //   RateLimited  -> caller maps to HTTP 429 (AC 8)
    //   ServerError  -> caller maps to HTTP 500 (AC 9)
    //   StaleData    -> rewrites each record's as-of timestamp to strictly < now (AC 10)
    //   PartialData  -> returns a strict subset (Count-1, min 0) of records (AC 11)
    public IReadOnlyList<T> ShapeRecords<T>(FailureDecision decision, IReadOnlyList<T> normal,
        Func<T, T> ageTimestamp);
}
```
HTTP mapping is done in a small endpoint filter/helper `ApplyFailureRule(providerCode, normalResultFactory)` used by the `/sandbox/{provider}/*` endpoints:
- `Offline` → `Results.Json(new { provider, status = "Offline" }, statusCode: 503)` and never returns normal records (AC 7).
- `RateLimited` → `Results.StatusCode(429)` (AC 8). `ServerError` → `Results.StatusCode(500)` (AC 9).
- `StaleData`/`PartialData` → shape the records then return normally (AC 10, 11).
- `None` → return the provider's normal response unchanged (AC 14).

For the canonical pipeline path, register the decorator in `GovernmentProviderRegistry` so the resolved `SandboxGovernmentDataProvider` is wrapped:
```csharp
new FailureSimulatingGovernmentDataProvider(inner: new SandboxGovernmentDataProvider(state), simulator)
```
The decorator awaits `simulator.ApplyAsync(ProviderCode, ct)` before delegating, throws `SandboxOfflineException` for `Offline`, and shapes returned lists for `StaleData`/`PartialData`. `RateLimited`/`ServerError` surface as typed exceptions the pipeline already treats as provider failures.

**New endpoints:**
| Method | Route | Auth policy | Request | Response |
|---|---|---|---|---|
| POST | `/sandbox/admin/failure-rules` | `/sandbox/admin` policy | `FailureRuleRequest` | 201-style `FailureRule` (200) or 400 validation (AC 1,4) |
| DELETE | `/sandbox/admin/failure-rules/{ruleId}` | `/sandbox/admin` policy | none | `{ removed: true }` (200) or 404 (AC 5,6) |
| GET | `/sandbox/admin/failure-rules` | `/sandbox/admin` policy | none | `{ items, total }` (operator visibility) |

**Audit shape:** action `FailureRuleCreated` / `FailureRuleDeleted`, resource = providerCode, metadata `{ ruleId, behavior }` (AC 15).

**Build order:** models + snapshot field → `CreateFailureRule`/`DeleteFailureRule`/`ResolveFailureDecision` → `SandboxFailureSimulator` + HTTP helper applied to `/sandbox/{provider}/*` reads → registry decorator → endpoints. Satisfies Req 3 AC 1–15.

---

### Req 4 — Sandbox Profile Editing and Asset Authoring

**Current state.** `GET /sandbox/admin/profiles` and `GET /sandbox/admin/profiles/{syntheticPersonId}` exist (return `BuildSandboxProfile`). `SyntheticPerson` already has `ExpectedRiskBand`. Assets are separate collections keyed by identity token: `Vehicles`, `Properties`, `UtilityBills`, `Businesses`, `Travel`, `TaxProfiles`. There is **no** `PATCH` profile endpoint and **no** asset-authoring endpoint.

**Target design.** Add `PATCH /sandbox/admin/profiles/{syntheticPersonId}` (atomic field updates incl. `ExpectedRiskBand`) and `POST /sandbox/admin/profiles/{syntheticPersonId}/assets` (typed asset creation). All validation happens **before** any mutation so rejected requests leave the profile untouched (AC 8). Both reuse `BuildSandboxProfile` for responses.

**New domain models** (`Domain/Sandbox/ProfileEditModels.cs`):
```csharp
public sealed record ProfilePatchRequest(
    string? FullName, string? UrduName, string? FatherName,
    string? City, string? Province, string? ExpectedRiskBand);

public sealed record AssetAuthorRequest(
    string AssetType,                 // vehicle|property|utility|business|travel|taxreturn (case-insensitive)
    IReadOnlyDictionary<string, string> Fields);   // each value validated 1..256 chars

public static class SandboxValidation
{
    public static readonly string[] RiskBands = ["Low", "Medium", "High", "Critical"]; // case-sensitive (AC 2,6)
    public static readonly string[] AssetTypes = ["vehicle","property","utility","business","travel","taxreturn"];
    public const int MaxAssetsPerType = 100;          // AC 7
    public static bool IsValidText(string? v) => v is { Length: >= 1 and <= 256 }; // AC 1,4,5
}
```

**New `TaxNetState` methods** (`Application/Sandbox/TaxNetState.ProfileEditing.cs`):
```csharp
public enum ProfileEditOutcome { Updated, NotFound, ValidationError, LimitReached }

// AC 1,2,3,6,8,9 — validate all fields, then apply atomically; emit audit; save.
public (ProfileEditOutcome Outcome, object? Profile, string? Error)
    PatchProfile(string syntheticPersonId, ProfilePatchRequest request, string actor);

// AC 3,4,5,6,7,8,9 — validate type/fields/risk-band/limit, then add asset; emit audit; save.
public (ProfileEditOutcome Outcome, object? Profile, string? Error)
    AddProfileAsset(string syntheticPersonId, AssetAuthorRequest request, string actor);
```
`PatchProfile` logic:
- Find person by `Id`; if missing → `NotFound`, no mutation (AC 3).
- Validate each **provided** text field with `IsValidText` (1–256); if `ExpectedRiskBand` provided, it must be in `RiskBands` **case-sensitively** (AC 2, 6). Any failure → `ValidationError`, **no mutation** (AC 8).
- Build the replacement `SyntheticPerson` via `with { ... }` for all provided fields at once (atomic, AC 1), replace in `People`, emit audit `{ syntheticPersonId, changedFields }` within the same locked call (well under 5s, AC 9), `SaveSnapshot()`.

`AddProfileAsset` logic:
- Find person; if missing → `NotFound` (AC 3).
- `AssetType` must be one of `AssetTypes` (case-insensitive match) else `ValidationError` (AC 5). Each field value `IsValidText` else `ValidationError` (AC 5). If request carries an `ExpectedRiskBand`-style band it must match case-sensitively (AC 6).
- Count existing assets of that type for the person's identity token; if `>= 100` → `LimitReached`, no mutation (AC 7).
- Construct the concrete record (`VehicleRecord`, `PropertyRecord`, …) mapping `Fields` to record properties with synthetic IDs and the person's `IdentityToken`, add to the matching collection, emit audit `{ syntheticPersonId, assetType }`, `SaveSnapshot()` (AC 4, 9).

**New endpoints:**
| Method | Route | Auth policy | Request | Response |
|---|---|---|---|---|
| PATCH | `/sandbox/admin/profiles/{syntheticPersonId}` | `/sandbox/admin` policy | `ProfilePatchRequest` | updated profile (200) / 404 / 400 |
| POST | `/sandbox/admin/profiles/{syntheticPersonId}/assets` | `/sandbox/admin` policy | `AssetAuthorRequest` | updated profile (200) / 404 / 400 / 409 limit |

Endpoint maps outcome → status: `Updated`→200, `NotFound`→404, `ValidationError`→400, `LimitReached`→409.

**Audit shape:** action `SandboxProfilePatched` / `SandboxAssetAdded`, resource = syntheticPersonId, metadata `{ changedFields }` or `{ assetType }` (AC 9).

**Build order:** models + validation helpers → `PatchProfile`/`AddProfileAsset` → endpoints. Satisfies Req 4 AC 1–9.

---

### Req 5 — Embedding-Based RAG Retrieval with Pluggable Vector Store

**Current state.** Retrieval is lexical-only in `TaxNetState.QueryRag` (token overlap + boosts over `RagChunks`). Indexing in `IndexRagDocument` builds `RagChunk`s with `Keywords` and `PolicyCitation` (which already carries `ChunkId`, source `Url`, `Title`, `SourceType`). `RagQueryResult` has `Chunks`, `Citations`, `QualityChecks`, `RetrievalConfidence` but **no retrieval-path indicator**. Config key `TaxNet:Storage:VectorStore` is currently `"LexicalRagIndex"`. `TopK` is clamped to `[1,10]` today.

**Target design.** Introduce `IVectorStore` and `IEmbeddingProvider`. When an embedding provider is configured, `QueryRag` embeds the query, retrieves top-K by similarity, and tags the result `retrievalPath = "embedding"`. When none is configured, or the embedding call errors/times out (10s default), it falls back to the **existing lexical path** tagged `retrievalPath = "deterministic_fallback"`. Indexing additionally writes chunk embeddings to the vector store. The deterministic fallback (and the deterministic embedding provider) must produce identical ordering for identical inputs (AC 5).

**New domain models** (`Domain/Rag/VectorModels.cs`):
```csharp
public sealed record VectorEntry(
    string ChunkId, string DocumentId, float[] Embedding, PolicyCitation Citation);

public sealed record VectorMatch(string ChunkId, string DocumentId, double Similarity, PolicyCitation Citation);

public enum RetrievalPath { Embedding, DeterministicFallback }
```
Extend `RagQueryResult` with `string RetrievalPath` (serialized `"embedding"` | `"deterministic_fallback"`, AC 7) and ensure each chunk's `Citation` (already containing `DocumentId` via the chunk and `ChunkId`) is surfaced as citation metadata (AC 7).

**Abstractions** (`Infrastructure/Rag/IVectorStore.cs`):
```csharp
public interface IEmbeddingProvider
{
    bool IsConfigured { get; }                                   // AC 2 vs 3
    Task<float[]> EmbedAsync(string text, CancellationToken ct); // may throw/timeout -> fallback (AC 8)
}

public interface IVectorStore
{
    Task UpsertAsync(IReadOnlyList<VectorEntry> entries, CancellationToken ct);   // AC 6
    Task<IReadOnlyList<VectorMatch>> QueryAsync(float[] queryEmbedding, int topK, CancellationToken ct); // AC 2,4
    Task RemoveDocumentAsync(string documentId, CancellationToken ct);
}
```

**Default implementations:**
- `InMemoryVectorStore` (`Infrastructure/Rag/InMemoryVectorStore.cs`): cosine similarity over stored `VectorEntry` list; `QueryAsync` returns at most `topK`, ranked descending, and **all available** when fewer than K exist with no placeholders (AC 4). Selected when `TaxNet:Storage:VectorStore` ∈ {`InMemoryEmbedding`}. (pgvector/Qdrant are future impls behind the same interface — non-goals here but expressible.)
- `DeterministicEmbeddingProvider` (`Infrastructure/Rag/DeterministicEmbeddingProvider.cs`): `IsConfigured => true` only when explicitly selected; produces a fixed-dimension vector from a stable hash of normalized tokens so the same text always yields the same vector (supports AC 5 determinism for the embedding path during offline demos). When the config selects the lexical store (`"LexicalRagIndex"`, current default) the embedding provider reports `IsConfigured = false`, forcing the deterministic lexical fallback (AC 3).

**Configuration** (`appsettings*.json`, `TaxNetPlatformOptions.Storage`):
- `TaxNet:Storage:VectorStore`: `"LexicalRagIndex"` (default, lexical fallback) | `"InMemoryEmbedding"` | future `"Pgvector"`/`"Qdrant"`.
- `TaxNet:Rag:EmbeddingTimeoutSeconds`: default `10` (AC 8).
- `TaxNet:Rag:DefaultTopK`: default `5`; query `TopK` clamped to `[1,50]` (AC 2) — **change the current `[1,10]` clamp to `[1,50]`**.

**`QueryRag` changes** (`Application/Rag/TaxNetState.Rag.cs`):
```csharp
public RagQueryResult QueryRag(RagQueryRequest request,
    IEmbeddingProvider embeddings, IVectorStore vectorStore);
```
Logic:
- `topK = Math.Clamp(request.TopK <= 0 ? DefaultTopK : request.TopK, 1, 50);` (AC 2).
- If `embeddings.IsConfigured`: run `EmbedAsync` under a 10s `CancellationTokenSource` (AC 8). On success → `vectorStore.QueryAsync(vec, topK)`, map matches → chunks (resolve from `RagChunks` by `ChunkId`), `retrievalPath = "embedding"` (AC 2, 7). On timeout/error → log, set `retrievalPath = "deterministic_fallback"`, record "embedding path unavailable" in `QualityChecks` (AC 8), and fall through to lexical.
- If not configured, or on fallback: run the existing lexical scoring (AC 3, 5) → `retrievalPath = "deterministic_fallback"`.
- In both paths, exclude any chunk whose source is a private citizen record (filter on `Citation.SourceType`/document classification; the existing `QueryRag` already asserts "excludes raw PII and private citizen records") (AC 9).
- When fewer than K chunks exist, return all ranked with no placeholders (AC 4).

**`IndexRagDocument` / `FeedRagDocument` changes (AC 6):** after building `RagChunk`s, embed each chunk via `embeddings.EmbedAsync` and `vectorStore.UpsertAsync(entries)`. This is the **embedding indexing path**; it runs in-process when `FeedRagDocument` is called (including via the RagPolicy worker). An optional `taxnet-dev-embedding-jobs` queue is reserved for async embedding at scale but is not required for the offline default.

**DI** (`Program.cs`): register `IEmbeddingProvider` and `IVectorStore` as singletons selected by `TaxNet:Storage:VectorStore`; inject into the `RagPolicyService`/endpoints that call `QueryRag`/`FeedRagDocument`.

**Endpoints:** unchanged routes `POST /api/system/rag/query` and `POST /api/system/rag/documents` (under existing `/api/system/rag` policy, Req 8 AC 4); responses now include `retrievalPath` and per-chunk `{ documentId, chunkId }` citation metadata.

**Build order:** abstractions + impls → config + `[1,50]` clamp → `QueryRag` dual-path + `retrievalPath` → `IndexRagDocument` upsert → DI. Satisfies Req 5 AC 1–9.

---

### Req 6 — Explainability Evidence Guardrail

**Current state.** `BuildExplanation(caseId)` returns `AuditExplanation(CaseId, Summary, KeyReasons, EvidenceIds, Citations, HumanReviewWarning)`. `KeyReasons` are strings like `"{component.Name}: {component.Explanation}"`; `EvidenceIds` come from score component `EvidenceIds`; `Citations` are policy citations. The orchestrator endpoint `POST /api/orchestrator/cases/{caseId}/explain` returns the explanation immediately with only string-label "validation" notes. There is **no** claim-level grounding boundary and nothing withholds an explanation until validation.

**Target design.** Add a guardrail that decomposes a generated explanation into **claims**, maps each claim to zero or more `Evidence_Reference`s (an evidence item id from the case's `Evidence`/score-component `EvidenceIds`, or a citation `ChunkId`), classifies each claim grounded/ungrounded, and returns a validated result that **withholds** raw claims until evaluation completes. The guardrail outcome (grounded count, ungrounded count, total) is exposed in the response, every ungrounded claim carries an ungrounded indicator, and an audit event is emitted.

**New domain models** (`Domain/Explainability/GuardrailModels.cs`):
```csharp
public sealed record ExplanationClaim(
    string ClaimId,
    string Text,
    IReadOnlyList<string> EvidenceReferences,   // evidence item ids and/or citation chunk ids
    bool Grounded);                              // false => ungrounded indicator (AC 3,5)

public sealed record GuardrailOutcome(
    int GroundedCount, int UngroundedCount, int TotalClaimCount); // grounded+ungrounded==total (AC 4)

public sealed record ValidatedExplanation(
    string ExplanationId,
    string CaseId,
    string Summary,
    IReadOnlyList<ExplanationClaim> Claims,
    GuardrailOutcome Outcome,
    bool EvidenceBacked,                         // false when GroundedCount==0 (AC 8)
    string HumanReviewWarning);
```

**New `TaxNetState` method** (`Application/Explainability/TaxNetState.Guardrail.cs`):
```csharp
public enum GuardrailStatus { Validated, EvaluationFailed }

// AC 1,2,3,4,5,6,8. Decomposes claims, maps evidence refs, classifies, emits audit.
public (GuardrailStatus Status, ValidatedExplanation? Result, string? Error)
    ValidateExplanation(string caseId, string actor);
```
Logic:
- Build the base explanation via `BuildExplanation(caseId)`. The set of valid `Evidence_Reference`s = case `Evidence` ids ∪ score-component `EvidenceIds` ∪ explanation `Citations[].ChunkId`.
- **Claim decomposition:** each entry in `KeyReasons` becomes one claim (deterministic split). For each claim, collect the evidence ids referenced by the originating score component plus any citation chunk ids matched by the claim's topic.
- A claim is **grounded** iff `EvidenceReferences.Count >= 1` (AC 2); otherwise **ungrounded** with the indicator set (AC 3).
- Compute `GroundedCount`, `UngroundedCount`, `TotalClaimCount` with the invariant `grounded + ungrounded == total` (AC 4). `EvidenceBacked = GroundedCount > 0` and when `GroundedCount == 0` all claims carry the ungrounded indicator (AC 8).
- Emit audit `GuardrailEvaluated` with `{ explanationId, groundedCount, ungroundedCount }` (AC 6).
- The explanation is assembled **only after** classification completes; the endpoint never returns claims before this method returns (AC 1).
- If evaluation cannot complete (e.g. case missing, decomposition throws), return `(EvaluationFailed, null, error)`; the endpoint returns an error response and no claims are presented as evidence-backed (AC 7).

**New endpoint:**
| Method | Route | Auth policy | Request | Response |
|---|---|---|---|---|
| POST | `/api/orchestrator/cases/{caseId}/explain-guarded` | see Req 8 note (orchestrator path) | `?` query (allow/preferred provider optional) | `ValidatedExplanation` (200) / 404 / 502 guardrail failure |

On `EvaluationFailed` return `Results.Json(new { message = "Guardrail validation failed.", error }, statusCode: 502)` and retain the unvalidated explanation server-side without presenting claims (AC 7).

**Audit shape:** action `GuardrailEvaluated`, resource = explanationId (`expl-{caseId}-{ts}`), metadata `{ groundedCount, ungroundedCount, totalClaimCount }` (AC 6).

**Build order:** models → `ValidateExplanation` → guarded endpoint. Satisfies Req 6 AC 1–8.

---

### Req 7 — Worker Runtime Consistency

**Current state.** `WorkerOptions.FromEnvironment` already reads `TAXNET_QUEUE_MODE`/`TAXNET_OBJECT_STORE_MODE` independently and defaults each to `File`; `--watch` toggles `RunOnce`; `TAXNET_MAX_MESSAGES` (default 5) and `TAXNET_POLL_SECONDS` (default 5) are honored; `WorkerHost.RunAsync` writes failures to `taxnet-dev-worker-failures` and continues on per-message exceptions. The seven existing workers all follow this.

**Target design.** The two new workers (PublicDataConnector, Notification) are built **exactly** on this runtime with no deviations — they only call `WorkerOptions.FromEnvironment(name, queue, args)` and `WorkerHost.RunAsync`. This means Req 7 is satisfied for the new workers by construction; the design adds **no new runtime code**, only the two thin `Program.cs` handlers (shown in Reqs 1 and 2) and their `SeedWorkers()` registrations.

**Verification mapping:**
- AC 1, 2 (independent mode selection + defaulting): inherited from `WorkerOptions.FromEnvironment`; verify by launching with each env var set/unset/garbage and reading the startup line.
- AC 3 (File mode = no network): File clients (`FileBackedQueueClient`/`FileBackedObjectStorageClient`) are chosen when mode ≠ `LocalStack`; no `HttpClient` calls to infra.
- AC 4 (LocalStack via `LOCALSTACK_ENDPOINT`): `LocalStackSqsQueueClient`/`LocalStackS3ObjectStorageClient` use `options.LocalStackEndpoint`.
- AC 5, 6 (run-once vs `--watch` polling at `TAXNET_POLL_SECONDS`, max `TAXNET_MAX_MESSAGES`): inherited from `WorkerHost.RunAsync` loop.
- AC 7 (per-envelope failure isolation → `taxnet-dev-worker-failures`, keep processing): inherited from the `try/catch` in `WorkerHost.RunAsync`.
- AC 8 (represented in API worker health surface with name + queue): add both workers to `SeedWorkers()` (shown above) so `GET /api/system/workers` lists their `name`/`queueName`.

**Infra (`infra/localstack/main.tf`):** add `taxnet-dev-public-data-connector-jobs`, `taxnet-dev-notification-jobs` (and reserved `taxnet-dev-embedding-jobs`) to `local.queue_names` (which auto-creates the queue + DLQ + redrive + DLQ-depth alarm) and add matching `/taxnet/dev/workers/public-data-connector`, `/taxnet/dev/workers/notification` CloudWatch log groups. Satisfies Req 7 AC 1–8.

---

### Req 8 — Authentication and Authorization Integration

**Current state.** `AuthorizationCatalog` resolves roles JWT-first (`GetCurrentRoles` reads role claims, falls back to `X-Demo-Role`, then defaults to `taxnet-admin` in dev), `GetCurrentActor` resolves identity (JWT `sub` → `X-Demo-User` → role). `TryGetAccessDecision` does **longest-prefix** policy matching and grants `taxnet-admin` unconditionally. The global middleware in `Program.cs` enforces this for every `/api` and `/sandbox` path. `/sandbox/admin` and `/api/system/rag` policies already exist.

**Target design.** New endpoints rely entirely on the existing catalog and middleware — **no new authorization code paths**, satisfying consistency by reuse.

**Per-endpoint policy mapping:**
| Endpoint | Matched prefix policy | Allowed roles (+ `taxnet-admin`) |
|---|---|---|
| `POST/DELETE/GET /sandbox/admin/failure-rules*` | `/sandbox/admin` | sandbox-admin, data-engineer (AC 2) |
| `PATCH /sandbox/admin/profiles/{id}`, `POST .../assets` | `/sandbox/admin` | sandbox-admin, data-engineer (AC 2) |
| `POST /api/system/notifications/{id}/deliver` | `/api/system` | admin, security-admin, model-admin, policy-analyst |
| `POST /api/connectors/public-data/fetch` | `/api/connectors` | admin, sandbox-admin, data-engineer |
| `POST /api/system/rag/query`, `.../documents` | `/api/system/rag` | policy-analyst, model-admin (AC 4) |
| `POST /api/orchestrator/cases/{id}/explain-guarded` | (no prefix policy today) | see note below |

**Decisions:**
- **Longest-prefix (AC 6):** `/sandbox/admin/failure-rules` correctly resolves to `/sandbox/admin` (longer than `/sandbox`); `/api/system/rag/query` resolves to `/api/system/rag` (longer than `/api/system`). This is automatic via the existing `OrderByDescending(x => x.PathPrefix.Length)`.
- **admin override (AC 7):** automatic — every policy check includes `current.Equals("taxnet-admin")`.
- **JWT-first fallback (AC 1):** automatic via `GetCurrentRoles`.
- **Deny shape (AC 3):** the global middleware returns 403 with `{ message, role, requiredRoles, path }` and never invokes the endpoint body, so no state mutates. The state-mutating `TaxNetState` methods are only reached after the middleware allows the request.
- **Actor in audit (AC 5):** every new state-changing endpoint passes `AuthorizationCatalog.GetCurrentActor(context)` into the `TaxNetState` method, which records it as the audit `actor` (matches existing `assign`/`decision` endpoints).
- **Orchestrator path note:** the existing `/api/orchestrator/cases/{caseId}/explain` endpoint has **no** prefix policy in `AuthorizationCatalog` today (it is reachable in dev). To enforce Req 8 AC 4 for the new guarded endpoint, **add** an `/api/orchestrator` policy entry `["taxnet-admin","taxnet-auditor","taxnet-senior-auditor","taxnet-policy-analyst"]` to `PathPolicies`. This is the one authorization-catalog change in this feature; it also retroactively protects the existing explain endpoint consistently.

**Build order:** add the single `/api/orchestrator` policy entry → confirm each new endpoint passes `GetCurrentActor` to its state method → manual 403/allow checks per role. Satisfies Req 8 AC 1–7.

---

## Data Models

All new state is added to the existing `TaxNetState` partial class and serialized through `TaxNetSnapshot` (`SaveSnapshot`/`ApplySnapshot`), exactly like the current collections (`People`, `Cases`, `RagChunks`, `Notifications`, `AuditEvents`, …). No external store is introduced; the single-host modular monolith (design §4A) is preserved. Existing records (`NotificationItem`, `SyntheticPerson`, `AuditEvent`, `RagChunk`, `PolicyCitation`, `EvidenceItem`, `AuditExplanation`) are reused unchanged unless noted.

### New domain records by capability

```csharp
// ── Req 1: Public Data Connector ────────────────────────────────────────────
public sealed record PublicDataFetchRequest(string SourceUrl, string SourceType, string? Title, IReadOnlyList<string>? Tags);
public sealed record PublicDataIngestRequest(
    string SourceUrl, string SourceType, string? Title, string ExtractedText,
    string ContentHash, string ParserVersion, long RawSizeBytes,
    DateTimeOffset CapturedAtUtc, string RawSnapshotKey, IReadOnlyList<string>? Tags);
public enum PublicSourceOutcome { Indexed, Rejected, Failed, FailedExtraction }
public sealed record PublicDataFetchResult(
    string SourceUrl, PublicSourceOutcome Outcome, string? ContentHash,
    string? FailureReason, string? RagDocumentId, DateTimeOffset CompletedAtUtc);

// ── Req 2: Notification delivery ─────────────────────────────────────────────
public sealed record NotificationDeliveryResult(bool Delivered, string ChannelUsed, bool RequestedChannelUnavailable, string? Error);
// NotificationItem(Id, Recipient, Channel, Subject, Body, Status, CreatedAtUtc) reused as-is;
// Status remains the string set { "Queued", "Sent" }.

// ── Req 3: Failure & latency simulator ───────────────────────────────────────
public enum FailureBehavior { Offline, StaleData, PartialData, RateLimited, ServerError }
public sealed record FailureRuleRequest(string ProviderCode, string Behavior, int? InjectedLatencyMs);
public sealed record FailureRule(
    string Id, string ProviderCode, FailureBehavior Behavior,
    int? InjectedLatencyMs, bool Active, DateTimeOffset CreatedAtUtc);
public enum SimStatus { Normal, Offline, RateLimited, ServerError }
public sealed record ProviderRecord(string RecordId, DateTimeOffset AsOfUtc, object Payload);
public sealed record SimulationOutcome(int DelayMs, SimStatus Status, IReadOnlyList<ProviderRecord> Records);

// ── Req 4: Profile editing & asset authoring ─────────────────────────────────
public sealed record ProfilePatchRequest(
    string? FullName, string? UrduName, string? FatherName, string? City, string? Province, string? ExpectedRiskBand);
public sealed record AssetAuthorRequest(string AssetType, IReadOnlyDictionary<string, string> Fields, string? ExpectedRiskBand);
public enum ProfileEditOutcome { Updated, NotFound, ValidationError, LimitReached }

// ── Req 5: Embedding RAG + vector store ──────────────────────────────────────
public sealed record VectorMatch(RagChunk Chunk, decimal Similarity);
public enum RetrievalPath { Embedding, DeterministicFallback }
public sealed record RagRetrievalResult(
    IReadOnlyList<RagChunk> Chunks, IReadOnlyList<PolicyCitation> Citations,
    RetrievalPath Path, bool EmbeddingUnavailable);

// ── Req 6: Explainability guardrail ──────────────────────────────────────────
public sealed record ExplanationClaim(string Text, IReadOnlyList<string> EvidenceReferences);
public sealed record ValidatedClaim(string Text, bool Grounded, IReadOnlyList<string> EvidenceReferences);
public sealed record GuardrailOutcome(IReadOnlyList<ValidatedClaim> Claims, int GroundedCount, int UngroundedCount, int TotalCount);
```

### Changes to existing state and snapshot

| Element | Change | Reason |
|---------|--------|--------|
| `TaxNetState.FailureRules : List<FailureRule>` | **new** collection | Req 3 simulator rules |
| `TaxNetState` vector store (`IVectorStore`, default `InMemoryVectorStore`) | **new** in-process service | Req 5 retrieval; embeddings derived from `RagChunks`, rebuilt on index (not snapshotted) |
| `SyntheticPerson.ExpectedRiskBand` | reused (already present) | Req 4 risk-band marking |
| `NotificationItem.Status` transition driver | now driven by `DeliverNotification` | Req 2 idempotent `Queued→Sent` |
| `TaxNetSnapshot` | add `FailureRules` to `SaveSnapshot`/`ApplySnapshot` | persist simulator rules across restarts |
| `WorkerStatus` rows | add `PublicDataConnector.Worker`, `Notification.Worker` in `SeedWorkers()` | Req 7 health surface |
| `AuthorizationCatalog.PathPolicies` | add `/api/orchestrator` policy entry | Req 8 AC 4 (guarded explanation endpoint) |

New snapshot collections are init-only and default to empty:
```csharp
public List<FailureRule> FailureRules { get; init; } = [];      // Req 3
```
Backward compatibility: existing snapshots without these arrays deserialize to empty lists (init defaults), so older `App_Data/taxnet-state.json` files load cleanly. `SyntheticPerson.ExpectedRiskBand` already exists, so Req 4 needs no schema change; `NotificationItem` already carries `Channel`/`Status`, so Req 2 needs no schema change.

### Value constraints (enforced by validators)

| Field | Constraint | Requirement |
|-------|-----------|-------------|
| `FailureRule.ProviderCode` | must match an existing Sandbox_Provider | R3.2 |
| `FailureRule.Behavior` | exactly one of the 5 enum values | R3.2 |
| `FailureRule.InjectedLatencyMs` | `null` or `0..60000` | R3.3 |
| profile text fields | each provided field length `1..256` | R4.1, R4.4, R4.5 |
| `ExpectedRiskBand` | exact, case-sensitive `Low\|Medium\|High\|Critical` | R4.2, R4.6 |
| asset type | one of `vehicle\|property\|utility bill\|business\|travel\|tax return` | R4.4, R4.5 |
| assets per type per profile | ≤ 100 | R4.7 |
| RAG top-K | `clamp(K, 1, 50)`, default 5 | R5.2, R5.4 |
| embedding timeout | configurable, default 10 s | R5.8 |
| guardrail counts | `grounded + ungrounded == total`, all ≥ 0 | R6.4 |

### Object-store buckets and queues (existing conventions reused)

| Bucket | Use |
|--------|-----|
| `taxnet-dev-raw-source-snapshots` | Public Data Connector raw snapshots (R1.2) + existing RAG source text |
| `taxnet-dev-worker-failures` | Worker failure artifacts (R7.7) |
| `taxnet-dev-audit-reports` | Existing case reports (unchanged) |

| Queue | Worker |
|-------|--------|
| `taxnet-dev-public-data-connector-jobs` | PublicDataConnector.Worker (new) |
| `taxnet-dev-notification-jobs` | Notification.Worker (new) |

### Existing types reused (do not redefine)

`RagFeedRequest`, `RagDocument`, `RagChunk`, `PolicyCitation`, `RagQueryRequest`, `RagQueryResult` (extended), `AuditExplanation`, `AuditEvent`, `NotificationItem`, `WorkerStatus`, `ObjectStoreItem`, `ProviderDescriptor`, `SyntheticPerson`, `VehicleRecord`, `PropertyRecord`, `UtilityBillRecord`, `BusinessRecord`, `TravelRecord`, `TaxProfile`, `IdentityToken`.

### State-mutation invariants (apply to all new `TaxNetState` methods)

1. Take `lock (_lock)` for the whole read-validate-mutate-audit-save sequence.
2. Validate fully **before** mutating; on validation failure, return without mutating (no partial writes).
3. Call the existing private `AddAuditEvent(actor, role, action, resource, outcome, metadata)`.
4. Call `SaveSnapshot()` exactly once at the end of a successful mutation.

---

## Error Handling

The codebase has an established error-handling style this feature follows exactly:

- **Global exception middleware.** `GlobalExceptionMiddleware` (registered first in `Program.cs`) converts unhandled exceptions to a consistent error response with a correlation id. New endpoints rely on it for unexpected faults and do not duplicate try/catch for unforeseen errors.
- **Validation as values, not exceptions.** New `TaxNetState` methods return outcome tuples (`(Outcome, Result, Error)`), never throw for expected validation failures. Endpoints map outcomes to status codes:
  - Req 3: invalid rule → `400` with the validation message (AC 3.4); delete missing → `404` (AC 3.6).
  - Req 4: `NotFound`→`404` (AC 4.3), `ValidationError`→`400` (AC 4.5/4.6), `LimitReached`→`409` (AC 4.7).
  - Req 1: rejected/failed/failed-extraction outcomes return `200` with the `PublicDataFetchResult` describing the outcome (the request itself was well-formed); malformed ingest payloads → `400`.
  - Req 6: `EvaluationFailed` → `502` with `{ message, error }`, claims withheld (AC 6.7).
- **`InvalidOperationException` for not-found in existing patterns.** Where existing endpoints catch `InvalidOperationException` → `404`/`400` (e.g. case assign/decision), new lookups that reuse `Cases.First(...)` must switch to `FirstOrDefault` + explicit outcome to avoid unhandled exceptions for missing ids (notably the guardrail path which builds on `BuildExplanation`).
- **Failure-rule behaviors are deliberate errors.** `Offline`/`RateLimited`/`ServerError` are simulated faults: the HTTP helper returns `503`/`429`/`500` respectively; the provider decorator surfaces typed exceptions (`SandboxOfflineException`, etc.) that the ingestion pipeline already treats as provider failures (recorded as a failed `ImportJob`).
- **Worker fault isolation.** Per-envelope exceptions are caught by `WorkerHost.RunAsync`, written to `taxnet-dev-worker-failures`, and processing continues — the new workers inherit this and must not swallow exceptions themselves (let them propagate to the host so the failure artifact is written). The PublicDataConnector worker is the one exception: it catches fetch faults to POST an explicit `Failed` ingest (Req 1 AC 9), then returns normally.
- **Embedding fallback, not failure.** Embedding-provider timeouts/errors are caught inside `QueryRag` and converted to a deterministic-fallback retrieval with a quality-check note (Req 5 AC 8); they never surface as request errors.
- **Concurrency.** All `TaxNetState` mutations occur under `_lock`; `SaveSnapshot` writes atomically via a temp file + `File.Move` (existing behavior), so partial writes never persist.

---

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

Most of the gap logic is pure, in-process, and input-varying (hashing, validation predicates, rule selection, embedding/lexical ranking, claim grounding, mode defaulting, policy decisions), so property-based testing applies strongly. UI-free, side-effect-only, and infrastructure concerns (worker startup, LocalStack wiring, HTTP status mapping, timing/latency) are covered by example/integration/smoke tests in the Testing Strategy instead.

### Req 1 — Public Data Connector

#### Property 1: Content hash is deterministic and content-defined

*For any* byte sequence, `ComputeContentHash` returns the same hash for two captures of identical content, and the hash equals that of an independent copy of the same bytes.

**Validates: Requirements 1.7**

#### Property 2: Only approved sources are ingested

*For any* source URL, if the approved-source policy classifies it as not approved (including auth-required, CAPTCHA-gated, and terms/privacy-restricted URLs), `IngestPublicData` returns `Rejected`, creates no `RagDocument`, and records the disallowed URL.

**Validates: Requirements 1.5, 1.6**

#### Property 3: Approved ingest records complete provenance

*For any* approved ingest request with non-whitespace extracted text, the resulting indexed document records the source URL, a UTC capture timestamp, the content hash, and the parser version.

**Validates: Requirements 1.3**

#### Property 4: Empty extraction is never indexed

*For any* extracted text that is empty or all-whitespace on an approved source, `IngestPublicData` returns `FailedExtraction`, creates no `RagDocument`, and retains the raw snapshot reference.

**Validates: Requirements 1.10**

#### Property 5: Every completed fetch is audited

*For any* `IngestPublicData` call, exactly one audit event is appended with action `PublicDataFetch`, the source URL, the content hash (when available), and the resulting outcome.

**Validates: Requirements 1.8**

### Req 2 — Notification Service Worker

#### Property 6: Delivery transitions Queued to Sent at most once (idempotent)

*For any* notification, delivering it once sets its status to `Sent`, and delivering it again leaves it `Sent` without producing a second `Queued → Sent` transition.

**Validates: Requirements 2.3, 2.8**

#### Property 7: Unconfigured channels fall back to InApp and are flagged

*For any* requested channel name with no configured implementation, the channel registry resolves to `InApp`, delivery sets the item to `Sent`, and the outcome records that the requested channel was unavailable.

**Validates: Requirements 2.6**

#### Property 8: Missing notification ids cause no state change

*For any* notification id not present in `Notifications`, `DeliverNotification` leaves every notification unchanged and emits an audit event with a `Rejected` outcome.

**Validates: Requirements 2.10**

#### Property 9: Every delivery attempt is audited

*For any* delivery attempt, an audit event is appended recording the recipient, the channel used, and the delivery outcome.

**Validates: Requirements 2.7**

### Req 3 — Sandbox Failure and Latency Simulator

#### Property 10: Failure-rule validity predicate and creation

*For any* `FailureRuleRequest`, creation succeeds (returning a rule with a non-empty id, `Active = true`, and echoed fields) if and only if the provider code matches an existing provider, the behavior parses to exactly one supported `FailureBehavior`, and any injected latency lies in `[0, 60000]`.

**Validates: Requirements 3.1, 3.2, 3.3**

#### Property 11: Invalid requests never mutate rule state

*For any* invalid `FailureRuleRequest`, `CreateFailureRule` returns a validation error and the `FailureRules` collection is unchanged.

**Validates: Requirements 3.4**

#### Property 12: Create-then-delete round trip

*For any* created rule, deleting it returns success and `ResolveFailureDecision` for its provider no longer reflects it; deleting any id that is not present returns not-found and leaves the collection unchanged.

**Validates: Requirements 3.5, 3.6**

#### Property 13: Most-recent active rule wins, otherwise normal

*For any* set of rules targeting a provider, `ResolveFailureDecision` returns the application of the active rule with the greatest `CreatedAtUtc`, or `None` when no active rule targets that provider.

**Validates: Requirements 3.13, 3.14**

#### Property 14: StaleData ages every record before the read time

*For any* normal record set, applying `StaleData` yields records whose as-of timestamps are all strictly earlier than the read time.

**Validates: Requirements 3.10**

#### Property 15: PartialData returns a strict, smaller subset

*For any* non-empty normal record set, applying `PartialData` yields fewer records than the input, and every returned record is an element of the input.

**Validates: Requirements 3.11**

#### Property 16: Rule create/delete is audited

*For any* successful create or delete, exactly one audit event is appended recording the action, provider code, rule id, and behavior.

**Validates: Requirements 3.15**

### Req 4 — Sandbox Profile Editing and Asset Authoring

#### Property 17: Valid patch applies all provided fields atomically

*For any* valid `ProfilePatchRequest` against an existing profile (every provided text field 1–256 chars), after `PatchProfile` each provided field equals its requested value and unspecified fields are unchanged.

**Validates: Requirements 4.1**

#### Property 18: Expected risk band is accepted case-sensitively only

*For any* string, a patch/asset request storing it as the expected risk band succeeds if and only if it exactly equals one of `Low`, `Medium`, `High`, `Critical` (case-sensitive); otherwise it is rejected with a validation error.

**Validates: Requirements 4.2, 4.6**

#### Property 19: Valid asset addition grows the matching collection by one

*For any* valid asset request (supported type, all fields 1–256 chars, under the per-type limit) on an existing profile, the matching asset collection for that profile and type grows by exactly one.

**Validates: Requirements 4.4**

#### Property 20: Asset type and field validation rejects invalid input

*For any* asset request whose type is unsupported or whose any field is outside 1–256 characters, `AddProfileAsset` returns a validation error and adds no asset.

**Validates: Requirements 4.5**

#### Property 21: The per-type limit of 100 is enforced

*For any* profile already holding 100 assets of a type, `AddProfileAsset` for that type returns `LimitReached` and adds no asset.

**Validates: Requirements 4.7**

#### Property 22: Rejected edits leave the profile and its assets unchanged

*For any* rejected `PatchProfile` or `AddProfileAsset` request (validation error, limit reached, or not found), a snapshot of the profile and its assets taken before equals the snapshot taken after.

**Validates: Requirements 4.3, 4.8**

#### Property 23: Successful edits are audited with their change details

*For any* successful patch or asset addition, exactly one audit event is appended recording the action, the `syntheticPersonId`, and the changed fields or asset type.

**Validates: Requirements 4.9**

### Req 5 — Embedding-Based RAG with Pluggable Vector Store

#### Property 24: Top-K is clamped and never exceeded

*For any* requested `TopK`, the effective K equals `clamp(TopK, 1, 50)` (default 5 when unset), and the number of returned chunks is at most the effective K.

**Validates: Requirements 5.2, 5.4**

#### Property 25: Fewer than K chunks returns all available, ranked, with no placeholders

*For any* document set holding fewer chunks than the effective K, retrieval returns exactly the available chunks in ranked order, each referencing a real indexed chunk, with no empty or placeholder entries.

**Validates: Requirements 5.4**

#### Property 26: Deterministic fallback retrieval is repeatable

*For any* query against an unchanged document set, two deterministic-fallback retrievals return identical chunk identifiers in identical positions.

**Validates: Requirements 5.5**

#### Property 27: Retrieval-path indicator reflects the path actually used

*For any* query, the result's retrieval-path indicator is `embedding` only when a configured embedding provider produced the matches, and is `deterministic_fallback` whenever no provider is configured or the embedding call errors.

**Validates: Requirements 5.3, 5.8**

#### Property 28: Indexing populates the vector store per chunk

*For any* indexed document, after `FeedRagDocument` the vector store contains one entry for each chunk produced for that document.

**Validates: Requirements 5.6**

#### Property 29: Every retrieved chunk carries citation metadata

*For any* retrieval, each returned chunk exposes its source document identifier and chunk identifier, and the result carries a valid retrieval-path value.

**Validates: Requirements 5.7**

#### Property 30: PII and private citizen records are never retrieved

*For any* query over a document set containing private-citizen-tagged chunks, the results contain none of those chunks on either retrieval path.

**Validates: Requirements 5.9**

### Req 6 — Explainability Evidence Guardrail

#### Property 31: A claim is grounded exactly when it has evidence, and the indicator matches

*For any* validated explanation, each claim is classified grounded if and only if it has at least one evidence reference, and the ungrounded indicator is present on a claim if and only if that claim is ungrounded.

**Validates: Requirements 6.2, 6.3, 6.5**

#### Property 32: Claim counts partition the total and govern evidence-backing

*For any* validated explanation, the grounded count and ungrounded count are non-negative, sum to the total claim count, the total equals the number of claims, and the explanation is marked evidence-backed if and only if the grounded count is greater than zero (so an all-ungrounded explanation has grounded count zero and is not evidence-backed).

**Validates: Requirements 6.4, 6.8**

#### Property 33: Guardrail evaluation is audited

*For any* successful guardrail validation, exactly one audit event is appended recording the explanation identifier, the grounded claim count, and the ungrounded claim count.

**Validates: Requirements 6.6**

### Req 7 — Worker Runtime Consistency

#### Property 34: Runtime mode defaults to File unless exactly LocalStack

*For any* pair of environment values for queue mode and object-store mode, each selection independently resolves to `LocalStack` only when the value equals `LocalStack` (case-insensitive) and resolves to `File` for every other value, including unset.

**Validates: Requirements 7.1, 7.2**

### Req 8 — Authentication and Authorization Integration

#### Property 35: Role resolution is JWT-first with header fallback

*For any* combination of JWT role claims and `X-Demo-Role` headers, resolved roles come from the JWT when it carries any TaxNet role, and the header roles are used only when the JWT carries none.

**Validates: Requirements 8.1**

#### Property 36: Disallowed roles are denied

*For any* role set that contains neither a role permitted by the matched path policy nor `taxnet-admin`, the access decision is denied.

**Validates: Requirements 8.3**

#### Property 37: The longest matching path policy is applied

*For any* request path matching one or more policy prefixes, the applied policy is the one with the longest matching prefix.

**Validates: Requirements 8.6**

#### Property 38: taxnet-admin is always authorized

*For any* request path and any matched policy, a role set containing `taxnet-admin` is allowed.

**Validates: Requirements 8.7**

#### Property 39: State-changing endpoints record the resolved actor

*For any* resolved actor identity (JWT subject when authenticated, otherwise the `X-Demo-User` header value), the audit event emitted by a new state-changing endpoint records that identity as its actor.

**Validates: Requirements 8.5**

---

## Testing Strategy

### Dual approach

- **Property-based tests** verify the 39 universal properties above across generated inputs.
- **Unit/example tests** cover concrete behaviors, HTTP status mapping, and edge cases.
- **Integration/smoke tests** cover worker startup, LocalStack wiring, and timing-sensitive behaviors that are not cost-effective to run 100+ times.

### Test project and library

Add a `TaxNetGuardian.Tests` xUnit project referencing `TaxNetGuardian.Api` and `TaxNetGuardian.Worker.Shared`. Use **FsCheck** (specifically `FsCheck.Xunit`'s `[Property]`) as the property-based testing library for .NET — do **not** hand-roll randomized testing. Configure a minimum of **100 iterations** per property:

```csharp
[Property(MaxTest = 100)]
public Property StaleData_ages_all_records_before_read_time() { /* ... */ }
```

Each property test carries a comment tag referencing its design property:

```csharp
// Feature: design-gap-completion, Property 14: For any normal record set, applying StaleData
// yields records whose as-of timestamps are all strictly earlier than the read time.
```

`TaxNetState` is constructed with a temp `IWebHostEnvironment.ContentRootPath` so each test run uses an isolated `App_Data` directory; the deterministic `_random = new(42)` seed makes generation reproducible.

### Property test mapping (100+ iterations each)

| Property | Generators / approach |
|---|---|
| 1 Content hash determinism | random `byte[]`; hash equals hash of a copy |
| 2 Approved-source classification | random URLs incl. restricted patterns |
| 3 Provenance completeness | random approved ingest requests |
| 4 Empty extraction | random all-whitespace strings |
| 5 / 9 / 16 / 23 / 33 Audit emission | assert one audit event with required fields after each op |
| 6 Notification idempotency | random notifications, deliver twice |
| 7 Channel fallback | random non-InApp channel names |
| 8 Missing id no-op | random ids absent from state |
| 10 / 11 Rule validity + no-mutation | random `FailureRuleRequest` (valid + invalid mixes) |
| 12 Create/delete round trip | random valid rules + random ids |
| 13 Most-recent-active selection | random rule sets with varied `CreatedAtUtc`/active flags |
| 14 / 15 Stale/Partial shaping | random record lists |
| 17 / 18 / 19 / 20 / 21 / 22 Profile editing | random patch/asset requests incl. boundary lengths (0,1,256,257) and the 100-asset boundary |
| 24 / 25 Top-K bounds | random K (incl. <1, >50) and stores of varied size |
| 26 Fallback determinism | random query + fixed doc set, retrieve twice |
| 27 Retrieval-path indicator | configured vs throwing vs unconfigured embedding provider |
| 28 Index populates vectors | random document content |
| 29 Citation metadata | random queries; assert documentId+chunkId present |
| 30 PII exclusion | doc sets mixing private-citizen-tagged chunks |
| 31 / 32 Guardrail grounding + counts | synthetic explanations with varied evidence-reference sets |
| 34 Mode defaulting | random env strings for both modes |
| 35 / 36 / 37 / 38 / 39 Authz | random role sets, paths, JWT/header combinations |

### Example / edge-case unit tests

- Req 1 AC 1.2/1.9: size-cap boundary (just under/over 50 MB) and each failure cause → `Failed`.
- Req 2 AC 2.2/2.4/2.9: delivery per recipient category; failing channel leaves `Queued`.
- Req 3 AC 3.7/3.8/3.9/3.12: endpoint returns offline/429/500; latency elapsed ≥ injected (small fixed set, not 100×).
- Req 5 AC 5.8: throwing embedding provider → `deterministic_fallback` with unavailability note.
- Req 6 AC 6.1/6.7: claims withheld until validation; forced failure → `502`.
- Req 7 AC 7.5/7.6/7.7/7.8: run-once exits 0; failing envelope writes failure artifact; both workers listed in `GET /api/system/workers`.
- Req 8 AC 8.2/8.4: matched policy prefixes for the new paths.

### Integration / smoke tests

- Worker build + run-once smoke for both new workers in File mode (Reqs 1.1, 2.1, 7.3).
- LocalStack round trip (enqueue → worker → API → snapshot) for one worker (Req 7.4) — manual or CI-gated, run once.
- `terraform -chdir=infra/localstack plan` includes the new queues (Req 7 infra).

### Why PBT is scoped as above

Worker hosting, LocalStack HTTP wiring, HTTP status codes, and real latency delays do not vary meaningfully with input or are too costly to run 100×, so they are example/integration/smoke tests. Everything that is a pure decision over a large input space — hashing, source classification, rule validity and selection, record shaping, profile validation, top-K/ranking, claim grounding, mode defaulting, and authorization decisions — is covered by property tests, which is where input variation reveals real edge cases.
