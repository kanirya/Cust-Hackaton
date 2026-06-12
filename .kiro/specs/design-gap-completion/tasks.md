# Implementation Plan: Design Gap Completion

## Overview

This plan implements the eight requirements behind the existing `TaxNetGuardian.Api` modular-monolith, `TaxNetGuardian.Worker.Shared` runtime, and `infra/localstack` Terraform, following the design's "Implementation Flow (Start to End)" build order (Steps 0–10). Each task is an incremental coding step that keeps `dotnet build TaxNetGuardian.slnx` green and the API runnable, adding behavior behind existing HTTP contracts, the worker runtime, the `AuthorizationCatalog`, and the snapshotted in-memory `TaxNetState`.

All business logic is implemented as testable `TaxNetState` methods and in-process services in the API; the two new workers stay thin (`WorkerOptions.FromEnvironment` + `WorkerHost.RunAsync`). Property-based tests use **FsCheck 3.x** (`FsCheck.Xunit` `[Property]`) configured for **≥100 iterations**, one test per design property, each tagged `Feature: design-gap-completion, Property {n}`. The design defines **39** correctness properties (numbered 1–39); every one has a dedicated property-test sub-task below.

## Tasks

- [x] 1. Project setup: infra queue names + test project
  - [x] 1.1 Add new queue names and log groups to LocalStack Terraform
    - In `infra/localstack/main.tf`, add `taxnet-dev-public-data-connector-jobs`, `taxnet-dev-notification-jobs`, and `taxnet-dev-embedding-jobs` to `local.queue_names` (auto-creates queue + DLQ + redrive + DLQ-depth alarm)
    - Add matching CloudWatch log groups `/taxnet/dev/workers/public-data-connector` and `/taxnet/dev/workers/notification`
    - Keep File mode runnable with no provisioning (directories created on demand)
    - _Requirements: 7.1, 7.4_

  - [x] 1.2 Create the `TaxNetGuardian.Tests` xUnit project with FsCheck
    - Add a new xUnit test project referencing `TaxNetGuardian.Api` and `TaxNetGuardian.Worker.Shared`
    - Add the `FsCheck` and `FsCheck.Xunit` (3.x) NuGet packages
    - Register the project in `TaxNetGuardian.slnx`
    - Add a shared test fixture that constructs `TaxNetState` with a temp `IWebHostEnvironment.ContentRootPath` (isolated `App_Data`) and the deterministic `_random = new(42)` seed
    - _Requirements: 5.5, 7.3_

- [x] 2. Shared domain models and snapshot extension
  - [x] 2.1 Add the new domain records for all capabilities
    - Create `Domain/PublicData/PublicSourceModels.cs` (`PublicDataFetchRequest`, `PublicDataIngestRequest`, `PublicSourceOutcome`, `PublicDataFetchResult`)
    - Create `Domain/Sandbox/FailureRuleModels.cs` (`FailureBehavior`, `FailureRuleRequest`, `FailureRule`, `FailureApplication`/`FailureDecision`) and `Domain/Sandbox/ProfileEditModels.cs` (`ProfilePatchRequest`, `AssetAuthorRequest`, `ProfileEditOutcome`, `SandboxValidation`)
    - Create `Domain/Rag/VectorModels.cs` (`VectorEntry`, `VectorMatch`, `RetrievalPath`) and `Domain/Explainability/GuardrailModels.cs` (`ExplanationClaim`, `ValidatedClaim`, `GuardrailOutcome`, `ValidatedExplanation`)
    - Reuse existing records (`NotificationItem`, `SyntheticPerson`, `RagChunk`, `PolicyCitation`, `AuditEvent`) unchanged
    - _Requirements: 1.3, 3.2, 4.1, 5.1, 6.4_

  - [x] 2.2 Extend `TaxNetSnapshot` and `TaxNetState` with new collections
    - Add `FailureRules : List<FailureRule>` (init-only, default empty) to the `TaxNetState` partial class
    - Add `FailureRules` to `SaveSnapshot()` and `ApplySnapshot()` so older snapshots without the array load as empty (backward compatible)
    - Confirm `SyntheticPerson.ExpectedRiskBand` and `NotificationItem.Channel`/`Status` need no schema change
    - _Requirements: 3.1, 4.2_

  - [ ]* 2.3 Write unit test for snapshot round-trip
    - Assert the new (empty) `FailureRules` collection round-trips through `SaveSnapshot`/`ApplySnapshot` and that a legacy snapshot without it deserializes to empty
    - _Requirements: 3.1_

- [x] 3. Checkpoint - baseline build and models
  - Run `dotnet build TaxNetGuardian.slnx` and `GET /api/health`; ensure all tests pass, ask the user if questions arise.

- [x] 4. Sandbox Failure and Latency Simulator (Req 3)
  - [x] 4.1 Implement failure-rule state methods
    - Create `Application/Sandbox/TaxNetState.FailureRules.cs` with `CreateFailureRule` (validate provider code against existing providers, behavior parses to exactly one `FailureBehavior`, latency null or `[0,60000]`; assign `RuleId`, `Active=true`, emit audit `FailureRuleCreated`, `SaveSnapshot`), `DeleteFailureRule` (remove + emit `FailureRuleDeleted`, false when missing), and `ResolveFailureDecision` (most-recent active rule by `CreatedAtUtc`, else `None`)
    - Validate fully before mutating; no partial writes
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.13, 3.14, 3.15_

  - [x] 4.2 Implement the `SandboxFailureSimulator` enforcement boundary
    - Create `Infrastructure/Sandbox/SandboxFailureSimulator.cs` with `ApplyAsync` (delay `>= InjectedLatencyMs`) and `ShapeRecords` (`StaleData` ages every as-of timestamp strictly before now; `PartialData` returns a strict smaller subset; `Offline` throws `SandboxOfflineException`)
    - Add the `FailureSimulatingGovernmentDataProvider` decorator and register it in `GovernmentProviderRegistry` so the canonical pipeline path honors rules
    - _Requirements: 3.7, 3.10, 3.11, 3.12_

  - [x] 4.3 Add failure-rule endpoints and the `/sandbox/{provider}/*` HTTP helper
    - In `Program.cs` add `POST /sandbox/admin/failure-rules` (200 rule / 400 validation), `DELETE /sandbox/admin/failure-rules/{ruleId}` (200 / 404), and `GET /sandbox/admin/failure-rules`
    - Add the `ApplyFailureRule(providerCode, normalResultFactory)` helper used by direct `/sandbox/{provider}/*` reads to map `Offline`→503, `RateLimited`→429, `ServerError`→500, shape `StaleData`/`PartialData`, and pass through `None`
    - All endpoints fall under the existing `/sandbox/admin` auth policy
    - _Requirements: 3.7, 3.8, 3.9, 3.10, 3.11, 3.12, 3.14_

  - [ ]* 4.4 Write property test for failure-rule validity and creation
    - **Property 10: Failure-rule validity predicate and creation**
    - **Validates: Requirements 3.1, 3.2, 3.3**

  - [ ]* 4.5 Write property test for invalid requests not mutating state
    - **Property 11: Invalid requests never mutate rule state**
    - **Validates: Requirements 3.4**

  - [ ]* 4.6 Write property test for create-then-delete round trip
    - **Property 12: Create-then-delete round trip**
    - **Validates: Requirements 3.5, 3.6**

  - [ ]* 4.7 Write property test for most-recent active rule selection
    - **Property 13: Most-recent active rule wins, otherwise normal**
    - **Validates: Requirements 3.13, 3.14**

  - [ ]* 4.8 Write property test for StaleData aging
    - **Property 14: StaleData ages every record before the read time**
    - **Validates: Requirements 3.10**

  - [ ]* 4.9 Write property test for PartialData subset
    - **Property 15: PartialData returns a strict, smaller subset**
    - **Validates: Requirements 3.11**

  - [ ]* 4.10 Write property test for rule create/delete audit
    - **Property 16: Rule create/delete is audited**
    - **Validates: Requirements 3.15**

  - [ ]* 4.11 Write unit tests for failure HTTP mapping and latency
    - Endpoint returns 503/429/500 for Offline/RateLimited/ServerError; elapsed time ≥ injected latency (small fixed set, not 100×)
    - _Requirements: 3.7, 3.8, 3.9, 3.12_

- [x] 5. Sandbox Profile Editing and Asset Authoring (Req 4)
  - [x] 5.1 Implement `PatchProfile`
    - Create `Application/Sandbox/TaxNetState.ProfileEditing.cs` with `PatchProfile` (find by id → `NotFound`; validate each provided text field 1–256 chars and `ExpectedRiskBand` case-sensitive in `Low/Medium/High/Critical`; apply all fields atomically via `with { }`; emit `SandboxProfilePatched`; `SaveSnapshot`)
    - Reject with no partial writes on any validation failure
    - _Requirements: 4.1, 4.2, 4.3, 4.6, 4.8, 4.9_

  - [x] 5.2 Implement `AddProfileAsset`
    - In the same file add `AddProfileAsset` (validate asset type in vehicle/property/utility/business/travel/taxreturn, each field 1–256 chars, optional band case-sensitive; enforce ≤100 assets per type → `LimitReached`; construct the concrete record with the person's `IdentityToken`; emit `SandboxAssetAdded`; `SaveSnapshot`)
    - _Requirements: 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9_

  - [x] 5.3 Add profile-editing endpoints
    - In `Program.cs` add `PATCH /sandbox/admin/profiles/{syntheticPersonId}` and `POST /sandbox/admin/profiles/{syntheticPersonId}/assets`, mapping `Updated`→200, `NotFound`→404, `ValidationError`→400, `LimitReached`→409, reusing `BuildSandboxProfile` for responses
    - Endpoints fall under the existing `/sandbox/admin` auth policy
    - _Requirements: 4.1, 4.3, 4.4, 4.5, 4.7_

  - [ ]* 5.4 Write property test for atomic patch application
    - **Property 17: Valid patch applies all provided fields atomically**
    - **Validates: Requirements 4.1**

  - [ ]* 5.5 Write property test for case-sensitive risk band
    - **Property 18: Expected risk band is accepted case-sensitively only**
    - **Validates: Requirements 4.2, 4.6**

  - [ ]* 5.6 Write property test for valid asset addition
    - **Property 19: Valid asset addition grows the matching collection by one**
    - **Validates: Requirements 4.4**

  - [ ]* 5.7 Write property test for asset type/field validation
    - **Property 20: Asset type and field validation rejects invalid input**
    - **Validates: Requirements 4.5**

  - [ ]* 5.8 Write property test for the per-type limit
    - **Property 21: The per-type limit of 100 is enforced**
    - **Validates: Requirements 4.7**

  - [ ]* 5.9 Write property test for rejected edits leaving state unchanged
    - **Property 22: Rejected edits leave the profile and its assets unchanged**
    - **Validates: Requirements 4.3, 4.8**

  - [ ]* 5.10 Write property test for successful-edit audit
    - **Property 23: Successful edits are audited with their change details**
    - **Validates: Requirements 4.9**

- [x] 6. Checkpoint - sandbox capabilities
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Embedding-Based RAG with Pluggable Vector Store (Req 5)
  - [x] 7.1 Add vector-store and embedding abstractions and default implementations
    - Create `Infrastructure/Rag/IVectorStore.cs` (`IEmbeddingProvider` with `IsConfigured`/`EmbedAsync`, `IVectorStore` with `UpsertAsync`/`QueryAsync`/`RemoveDocumentAsync`)
    - Create `Infrastructure/Rag/InMemoryVectorStore.cs` (cosine similarity, returns at most K ranked, all available when fewer than K, no placeholders) and `Infrastructure/Rag/DeterministicEmbeddingProvider.cs` (stable hash-based vector; `IsConfigured` only when explicitly selected)
    - _Requirements: 5.1, 5.4_

  - [x] 7.2 Add configuration and the top-K clamp change
    - Add `TaxNet:Storage:VectorStore` (`LexicalRagIndex` default | `InMemoryEmbedding`), `TaxNet:Rag:EmbeddingTimeoutSeconds` (default 10), `TaxNet:Rag:DefaultTopK` (default 5) to `appsettings*.json` and `TaxNetPlatformOptions.Storage`
    - Change the current `TopK` clamp from `[1,10]` to `[1,50]`
    - _Requirements: 5.2, 5.8_

  - [x] 7.3 Route `QueryRag` through the dual retrieval path
    - Modify `Application/Rag/TaxNetState.Rag.cs` `QueryRag(request, embeddings, vectorStore)`: when `embeddings.IsConfigured`, embed under a 10s timeout and query the vector store with `retrievalPath = "embedding"`; on timeout/error or when unconfigured, fall back to the existing lexical scoring with `retrievalPath = "deterministic_fallback"` and a quality-check note
    - Exclude private-citizen/PII chunks on both paths; return all available ranked when fewer than K; add `RetrievalPath` and per-chunk `{ documentId, chunkId }` citation metadata to `RagQueryResult`
    - _Requirements: 5.2, 5.3, 5.4, 5.7, 5.8, 5.9_

  - [x] 7.4 Add the embedding indexing path and DI wiring
    - In `IndexRagDocument`/`FeedRagDocument`, after building chunks, embed each chunk and `vectorStore.UpsertAsync` the entries
    - In `Program.cs` register `IEmbeddingProvider` and `IVectorStore` singletons selected by `TaxNet:Storage:VectorStore` and inject them into the endpoints/services that call `QueryRag`/`FeedRagDocument` (keep routes `POST /api/system/rag/query` and `POST /api/system/rag/documents`)
    - _Requirements: 5.6_

  - [ ]* 7.5 Write property test for top-K clamping
    - **Property 24: Top-K is clamped and never exceeded**
    - **Validates: Requirements 5.2, 5.4**

  - [ ]* 7.6 Write property test for fewer-than-K retrieval
    - **Property 25: Fewer than K chunks returns all available, ranked, with no placeholders**
    - **Validates: Requirements 5.4**

  - [ ]* 7.7 Write property test for deterministic fallback repeatability
    - **Property 26: Deterministic fallback retrieval is repeatable**
    - **Validates: Requirements 5.5**

  - [ ]* 7.8 Write property test for the retrieval-path indicator
    - **Property 27: Retrieval-path indicator reflects the path actually used**
    - **Validates: Requirements 5.3, 5.8**

  - [ ]* 7.9 Write property test for indexing populating the vector store
    - **Property 28: Indexing populates the vector store per chunk**
    - **Validates: Requirements 5.6**

  - [ ]* 7.10 Write property test for citation metadata
    - **Property 29: Every retrieved chunk carries citation metadata**
    - **Validates: Requirements 5.7**

  - [ ]* 7.11 Write property test for PII exclusion
    - **Property 30: PII and private citizen records are never retrieved**
    - **Validates: Requirements 5.9**

  - [ ]* 7.12 Write unit test for embedding-provider fallback
    - Throwing embedding provider → `deterministic_fallback` with an unavailability note in quality checks
    - _Requirements: 5.8_

- [x] 8. Explainability Evidence Guardrail (Req 6)
  - [x] 8.1 Implement the claim-level guardrail evaluator
    - Create `Application/Explainability/TaxNetState.Guardrail.cs` `ValidateExplanation(caseId, actor)`: build the base explanation (switch `Cases.First` lookups to `FirstOrDefault` + explicit outcome), decompose `KeyReasons` into claims, map evidence references (case `Evidence` ids ∪ score-component `EvidenceIds` ∪ citation `ChunkId`), classify grounded iff `>= 1` reference, set the ungrounded indicator otherwise
    - Compute `GroundedCount`/`UngroundedCount`/`TotalClaimCount` with `grounded + ungrounded == total`; `EvidenceBacked = GroundedCount > 0`; assemble claims only after classification; emit `GuardrailEvaluated` audit; return `EvaluationFailed` on inability to complete
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.8_

  - [x] 8.2 Add the guarded explanation endpoint
    - In `Program.cs` add `POST /api/orchestrator/cases/{caseId}/explain-guarded` returning `ValidatedExplanation` (200), 404 for missing case, and `502` with `{ message, error }` on `EvaluationFailed` (claims withheld)
    - _Requirements: 6.1, 6.7_

  - [ ]* 8.3 Write property test for grounding classification and indicator
    - **Property 31: A claim is grounded exactly when it has evidence, and the indicator matches**
    - **Validates: Requirements 6.2, 6.3, 6.5**

  - [ ]* 8.4 Write property test for claim-count partition and evidence-backing
    - **Property 32: Claim counts partition the total and govern evidence-backing**
    - **Validates: Requirements 6.4, 6.8**

  - [ ]* 8.5 Write property test for guardrail audit
    - **Property 33: Guardrail evaluation is audited**
    - **Validates: Requirements 6.6**

  - [ ]* 8.6 Write unit tests for withholding and failure response
    - Claims are withheld until validation completes; a forced evaluation failure returns `502` with no claims presented as evidence-backed
    - _Requirements: 6.1, 6.7_

- [x] 9. Checkpoint - RAG and guardrail
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Public Data Connector Worker (Reqs 1, 7)
  - [x] 10.1 Implement the approved-source policy, content hash, and ingest logic
    - Create `Application/PublicData/TaxNetState.PublicData.cs` with `PublicSourcePolicy.IsApproved` (allowlist public notices/fee schedules/policy PDFs/press releases; reject auth-required, CAPTCHA-gated, terms/privacy-restricted, citizen-verification), static `ComputeContentHash` (SHA-256 lowercase hex), and `IngestPublicData(request, actor)`
    - `IngestPublicData`: not approved → `Rejected` (no `FeedRagDocument`); whitespace-only text → `FailedExtraction` (retain snapshot); else call `FeedRagDocument`, capture document id → `Indexed`; emit `PublicDataFetch` audit with outcome; `SaveSnapshot`
    - _Requirements: 1.3, 1.5, 1.6, 1.7, 1.8, 1.10_

  - [x] 10.2 Add the connector API contract
    - In `Program.cs` add `POST /api/connectors/public-data/fetch` accepting `PublicDataIngestRequest`, returning `PublicDataFetchResult` (200) for rejected/failed/failed-extraction/indexed outcomes and 400 for malformed payloads, under the existing `/api/connectors` auth policy
    - _Requirements: 1.4, 1.9_

  - [x] 10.3 Create the PublicDataConnector worker project and register it
    - Create `TaxNetGuardian.Workers.PublicDataConnector/Program.cs` mirroring `RagPolicy/Program.cs`: `WorkerOptions.FromEnvironment("PublicDataConnector.Worker", "taxnet-dev-public-data-connector-jobs", args)` + `WorkerHost.RunAsync`; the handler fetches with a 30s timeout and 50MB cap, stores the raw snapshot to `taxnet-dev-raw-source-snapshots` with provenance, extracts text, and POSTs to the fetch contract (on fetch fault POSTs a `Failed` ingest)
    - Register the project in `TaxNetGuardian.slnx` and add `PublicDataConnector.Worker` (queue `taxnet-dev-public-data-connector-jobs`) to `SeedWorkers()`
    - _Requirements: 1.1, 1.2, 1.4, 1.9, 7.5, 7.6, 7.7, 7.8_

  - [ ]* 10.4 Write property test for content-hash determinism
    - **Property 1: Content hash is deterministic and content-defined**
    - **Validates: Requirements 1.7**

  - [ ]* 10.5 Write property test for approved-source-only ingest
    - **Property 2: Only approved sources are ingested**
    - **Validates: Requirements 1.5, 1.6**

  - [ ]* 10.6 Write property test for provenance completeness
    - **Property 3: Approved ingest records complete provenance**
    - **Validates: Requirements 1.3**

  - [ ]* 10.7 Write property test for empty-extraction handling
    - **Property 4: Empty extraction is never indexed**
    - **Validates: Requirements 1.10**

  - [ ]* 10.8 Write property test for fetch audit emission
    - **Property 5: Every completed fetch is audited**
    - **Validates: Requirements 1.8**

  - [ ]* 10.9 Write unit/integration tests for the connector worker
    - 50 MB size-cap boundary (just under/over) and each failure cause → `Failed`; run-once smoke in File mode exits 0 and the worker appears in `GET /api/system/workers`
    - _Requirements: 1.2, 1.9, 7.3, 7.5, 7.8_

- [x] 11. Notification Service Worker (Reqs 2, 7)
  - [x] 11.1 Implement the notification channel abstraction
    - Create `Infrastructure/Notifications/INotificationChannel.cs` (`NotificationDeliveryResult`, `INotificationChannel`, `INotificationChannelRegistry`) with `InAppNotificationChannel` (no-op success) and a registry that falls back to `InApp` with `RequestedChannelUnavailable = true` when a requested channel is unconfigured
    - _Requirements: 2.5, 2.6_

  - [x] 11.2 Implement idempotent `DeliverNotification`
    - Create `Application/Notifications/TaxNetState.Notifications.cs` `DeliverNotification(id, channels, actor)`: missing id → `Rejected` (no state change); already `Sent` → `AlreadySent` (no re-flip); else resolve channel, deliver, on success set `Queued→Sent` once, on failure leave `Queued`; emit `NotificationDelivered` audit with recipient/channel/outcome; `SaveSnapshot` on change. Add `EnqueueQueuedNotificationIds()` helper
    - _Requirements: 2.2, 2.3, 2.4, 2.6, 2.7, 2.8, 2.9, 2.10_

  - [x] 11.3 Add the delivery endpoint and switch `RunWorkerCycle` to enqueue
    - In `Program.cs` add `POST /api/system/notifications/{id}/deliver` (under `/api/system` policy) returning `{ item, outcome, channelUsed, requestedChannelUnavailable }`
    - In `Application/Operations/TaxNetState.Operations.cs` change `RunWorkerCycle` to enqueue one `{ notificationId }` envelope per `Queued` item to `taxnet-dev-notification-jobs`, keeping the inline File-mode flush as a synchronous fallback for the demo button
    - _Requirements: 2.2, 2.3_

  - [x] 11.4 Create the Notification worker project and register it
    - Create `TaxNetGuardian.Workers.Notification/Program.cs`: `WorkerOptions.FromEnvironment("Notification.Worker", "taxnet-dev-notification-jobs", args)` + `WorkerHost.RunAsync`; the handler reads `notificationId` and POSTs to `/api/system/notifications/{id}/deliver` (idempotent)
    - Register the project in `TaxNetGuardian.slnx` and add `Notification.Worker` (queue `taxnet-dev-notification-jobs`) to `SeedWorkers()`
    - _Requirements: 2.1, 7.5, 7.6, 7.7, 7.8_

  - [ ]* 11.5 Write property test for delivery idempotency
    - **Property 6: Delivery transitions Queued to Sent at most once (idempotent)**
    - **Validates: Requirements 2.3, 2.8**

  - [ ]* 11.6 Write property test for unconfigured-channel fallback
    - **Property 7: Unconfigured channels fall back to InApp and are flagged**
    - **Validates: Requirements 2.6**

  - [ ]* 11.7 Write property test for missing-id no-op
    - **Property 8: Missing notification ids cause no state change**
    - **Validates: Requirements 2.10**

  - [ ]* 11.8 Write property test for delivery audit
    - **Property 9: Every delivery attempt is audited**
    - **Validates: Requirements 2.7**

  - [ ]* 11.9 Write unit tests for delivery edge cases
    - Delivery per recipient category (auditor/admin/supervisor/citizen); a failing channel leaves the item `Queued`; run-once smoke lists the worker in `GET /api/system/workers`
    - _Requirements: 2.2, 2.4, 2.9, 7.3, 7.8_

- [x] 12. Checkpoint - workers
  - Ensure all tests pass, ask the user if questions arise.

- [x] 13. Authorization and audit sweep (Reqs 7, 8)
  - [x] 13.1 Add the `/api/orchestrator` policy and confirm actor/longest-prefix wiring
    - Add an `/api/orchestrator` entry to `AuthorizationCatalog.PathPolicies` (`["taxnet-admin","taxnet-auditor","taxnet-senior-auditor","taxnet-policy-analyst"]`) so the guarded explanation endpoint is protected
    - Confirm every new state-changing endpoint passes `AuthorizationCatalog.GetCurrentActor(context)` into its `TaxNetState` method, and that the new paths resolve to the intended longest-prefix policies with `taxnet-admin` override
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7_

  - [ ]* 13.2 Write property test for runtime mode defaulting
    - **Property 34: Runtime mode defaults to File unless exactly LocalStack**
    - **Validates: Requirements 7.1, 7.2**

  - [ ]* 13.3 Write property test for JWT-first role resolution
    - **Property 35: Role resolution is JWT-first with header fallback**
    - **Validates: Requirements 8.1**

  - [ ]* 13.4 Write property test for disallowed-role denial
    - **Property 36: Disallowed roles are denied**
    - **Validates: Requirements 8.3**

  - [ ]* 13.5 Write property test for longest-prefix policy selection
    - **Property 37: The longest matching path policy is applied**
    - **Validates: Requirements 8.6**

  - [ ]* 13.6 Write property test for taxnet-admin override
    - **Property 38: taxnet-admin is always authorized**
    - **Validates: Requirements 8.7**

  - [ ]* 13.7 Write property test for resolved-actor audit recording
    - **Property 39: State-changing endpoints record the resolved actor**
    - **Validates: Requirements 8.5**

  - [ ]* 13.8 Write unit tests for new-path policy matching
    - Assert the matched policy prefixes for the new sandbox, connector, RAG, notification, and orchestrator paths
    - _Requirements: 8.2, 8.4_

- [ ] 14. Integration and smoke tests
  - [ ]* 14.1 Write run-once smoke tests for both new workers in File mode
    - Each worker processes up to `TAXNET_MAX_MESSAGES` and exits 0; a failing envelope writes an artifact to `taxnet-dev-worker-failures` and processing continues
    - _Requirements: 1.1, 2.1, 7.3, 7.7_

  - [ ]* 14.2 Write a LocalStack round-trip integration test for one worker
    - Enqueue → worker → API → snapshot through LocalStack SQS/S3 using `LOCALSTACK_ENDPOINT` (CI-gated/manual single run)
    - _Requirements: 7.4_

  - [ ]* 14.3 Write a Terraform plan assertion for the new queues
    - Assert `terraform -chdir=infra/localstack plan` includes the three new queues and their log groups
    - _Requirements: 7.1_

- [x] 15. Final checkpoint - full build and test run
  - Run `dotnet build TaxNetGuardian.slnx` and the full test suite; ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional test sub-tasks and can be skipped for a faster MVP; core implementation tasks are never optional.
- Each task references specific requirement acceptance criteria for traceability and follows the design's build sequence (infra → models → simulator → profiles → RAG → guardrail → connector worker → notification worker → authz/audit sweep → tests).
- Property tests use FsCheck 3.x with ≥100 iterations, one test per design property, tagged `Feature: design-gap-completion, Property {n}`; the design defines 39 properties (1–39).
- HTTP status mapping, latency, worker hosting, and LocalStack wiring are covered by unit/integration/smoke tests rather than property tests, per the design's Testing Strategy.
- All new `TaxNetState` mutations take `lock (_lock)`, validate before mutating, call `AddAuditEvent`, and end with `SaveSnapshot()`.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "2.1"] },
    { "id": 1, "tasks": ["2.2"] },
    { "id": 2, "tasks": ["2.3", "4.1", "4.2", "5.1", "5.2", "7.1", "8.1", "10.1", "11.1", "11.2"] },
    { "id": 3, "tasks": ["4.3", "7.3"] },
    { "id": 4, "tasks": ["5.3", "4.4", "4.5", "4.6", "4.7", "4.8", "4.9", "4.10", "4.11"] },
    { "id": 5, "tasks": ["7.4", "5.4", "5.5", "5.6", "5.7", "5.8", "5.9", "5.10"] },
    { "id": 6, "tasks": ["8.2", "7.5", "7.6", "7.7", "7.8", "7.9", "7.10", "7.11", "7.12"] },
    { "id": 7, "tasks": ["10.2", "8.3", "8.4", "8.5", "8.6"] },
    { "id": 8, "tasks": ["10.3", "10.4", "10.5", "10.6", "10.7", "10.8"] },
    { "id": 9, "tasks": ["11.3", "10.9"] },
    { "id": 10, "tasks": ["11.4", "11.5", "11.6", "11.7", "11.8"] },
    { "id": 11, "tasks": ["13.1", "11.9"] },
    { "id": 12, "tasks": ["13.2", "13.3", "13.4", "13.5", "13.6", "13.7", "13.8", "14.1", "14.2", "14.3"] }
  ]
}
```
