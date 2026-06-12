# Requirements Document

## Introduction

TaxNet Guardian is an explainable graph intelligence platform implemented as a .NET modular-monolith API (`TaxNetGuardian.Api`) that hosts a React SPA (`TaxNetGuardian.Web`), a set of worker executables built on `TaxNetGuardian.Worker.Shared`, and LocalStack/AWS-backed infrastructure (SQS, S3, Secrets Manager, Cognito) provisioned through Terraform in `infra/localstack`.

This feature closes the gaps between the current implementation and the design documents in `docs/` (`TaxNetGuardian_System_Design.md`, `Model_Gateway_And_RAG.md`, `Worker_Runtime.md`). It is gap-closing work on an existing, fairly complete codebase. The single-host modular-monolith deployment is intentional per design §4A, so this feature does **not** require splitting services into physical microservices; it requires that the missing behaviors and contracts described in the design be implemented behind the existing HTTP contracts, worker runtime, and authorization model.

The feature delivers six capability areas:

1. A Public Data Connector Worker that ingests approved public documents into RAG (§8.12).
2. A Notification Service worker that delivers in-app notifications from a queue (§8.14).
3. A sandbox failure and latency simulator that operators can configure and that provider reads honor (§9.2 #6, §9.3).
4. Sandbox profile editing and asset authoring with expected-risk-band marking (§9.3).
5. Embedding-based RAG retrieval behind a pluggable vector store interface with a deterministic fallback and an embedding worker path (§8.11).
6. An explainability guardrail that enforces "no claim without evidence" as an explicit, testable validation boundary (§8.8).

All new workers must follow the existing File/LocalStack runtime mode pattern, emit audit-log events, and expose health/status surfaces consistent with existing workers. All new endpoints must integrate with the existing authentication model (DevelopmentHeaders default, CognitoJwt optional) and the role/scope authorization model in `AuthorizationCatalog`.

## Glossary

- **Api**: The `TaxNetGuardian.Api` host that exposes all HTTP contracts and owns the in-memory `TaxNetState` store.
- **TaxNetState**: The authoritative in-memory application state store inside the Api, persisted via snapshot.
- **Worker**: A standalone executable built on `TaxNetGuardian.Worker.Shared` that consumes a named queue, processes envelopes, and calls Api contracts. Existing workers: Ingestion, IdentityResolution, GraphIntelligence, RiskScoring, RagPolicy, Report, AuditLog.
- **WorkerHost**: The shared runtime in `TaxNetGuardian.Worker.Shared` that receives queue envelopes, dispatches them to a job handler, deletes processed messages, and writes failures to the `taxnet-dev-worker-failures` object-store bucket.
- **Runtime_Mode**: The infrastructure mode for queues and object storage, selected by environment variables `TAXNET_QUEUE_MODE` and `TAXNET_OBJECT_STORE_MODE`, each being either `File` (default, offline filesystem) or `LocalStack` (HTTP adapters for LocalStack SQS/S3).
- **Public_Data_Connector**: The new `TaxNetGuardian.Workers.PublicDataConnector` worker that fetches approved public documents, stores raw snapshots, extracts text, and submits them for RAG indexing.
- **Approved_Public_Source**: A public document source explicitly permitted by §3 and §8.12 (public tax notices, public fee schedules, public company/market data where permitted, public policy PDFs, public government press releases). Excludes private citizen verification pages and any source requiring authentication, CAPTCHA, or restricted by terms or privacy rules.
- **Notification_Worker**: The new `TaxNetGuardian.Workers.Notification` worker that consumes a notification queue and delivers in-app notifications.
- **NotificationItem**: The existing notification record type stored in `TaxNetState.Notifications` with a `Status` of `Queued` or `Sent`.
- **Notification_Channel**: A pluggable delivery target for a notification. The only implemented channel is `InApp`. Production channels (SNS, email, SMS) are non-goals but must be expressible through the same abstraction.
- **Sandbox_Provider**: A mock government data provider (NADRA, FBR, Excise, SECP, Property, Utility, Travel) exposed under `/sandbox/*` and read through `IGovernmentDataProvider`.
- **Failure_Rule**: An operator-configured rule that alters a Sandbox_Provider's responses to simulate `Offline`, `StaleData`, `PartialData`, `RateLimited` (HTTP 429), or `ServerError` (HTTP 500), and/or to inject response latency.
- **Synthetic_Profile**: A synthetic person record (`SyntheticPerson`) in TaxNetState identified by `syntheticPersonId`, together with its associated assets (vehicle, property, utility bill, business, travel, tax return).
- **Expected_Risk_Band**: A demo/evaluation label on a Synthetic_Profile indicating the intended risk band (`Low`, `Medium`, `High`, `Critical`).
- **Embedding_Provider**: A configured service that produces vector embeddings for text. When none is configured, the system uses a Deterministic_Fallback.
- **Vector_Store**: A pluggable store of embeddings and chunk metadata behind a single interface, with implementations targeting in-memory/pgvector/Qdrant.
- **Deterministic_Fallback**: The existing lexical/hybrid retrieval path used when no Embedding_Provider is configured, producing repeatable results for the same inputs.
- **Embedding_Worker**: The worker path that generates embeddings for RAG chunks and writes them to the Vector_Store.
- **Explainability_Guardrail**: The validation boundary that rejects or flags any AI-generated explanation claim not backed by structured evidence or a cited policy context.
- **Evidence_Reference**: A structured evidence identifier (evidence item id) or policy citation (chunk id) that grounds a claim in an explanation.
- **AuditEvent**: An immutable audit record appended via `AddAuditEvent` and stored in `TaxNetState.AuditEvents`.
- **Authorization_Catalog**: The `AuthorizationCatalog` component that maps path prefixes to allowed roles and resolves the current actor's roles and scopes from JWT claims or `X-Demo-Role`/`X-Demo-User` development headers.

## Requirements

### Requirement 1: Public Data Connector Worker

**User Story:** As a sandbox/data administrator, I want an approved-public-source connector worker, so that public policy documents are captured, traceable, and indexed into RAG without touching private citizen data.

#### Acceptance Criteria

1. THE Public_Data_Connector SHALL be implemented as a Worker that follows the WorkerHost runtime contract used by existing workers.
2. WHEN the Public_Data_Connector processes a fetch request for an Approved_Public_Source, THE Public_Data_Connector SHALL store the raw document snapshot in the S3 object store under the raw-source-snapshots bucket.
3. WHEN the Public_Data_Connector stores a raw snapshot, THE Public_Data_Connector SHALL record the source URL, capture timestamp in UTC, content hash, and parser version for that snapshot.
4. WHEN the Public_Data_Connector has stored a raw snapshot, THE Public_Data_Connector SHALL extract text from the snapshot and submit the extracted text to the Api RAG indexing contract.
5. IF a fetch request targets a source that is not an Approved_Public_Source, THEN THE Public_Data_Connector SHALL reject the request, skip fetching, and record a rejected outcome with the disallowed source URL.
6. IF a source requires authentication, presents a CAPTCHA, or is restricted by terms or privacy rules, THEN THE Public_Data_Connector SHALL classify the source as not approved and SHALL NOT retrieve citizen-level records.
2. WHEN the Public_Data_Connector processes a fetch request for an Approved_Public_Source, THE Public_Data_Connector SHALL retrieve the document within a fetch timeout of 30 seconds and store the raw document snapshot, up to a maximum snapshot size of 50 MB, in the S3 object store under the raw-source-snapshots bucket.
3. WHEN the Public_Data_Connector stores a raw snapshot, THE Public_Data_Connector SHALL record the source URL, the capture timestamp as an ISO 8601 UTC value, the content hash, and the parser version for that snapshot.
4. WHEN the Public_Data_Connector has stored a raw snapshot, THE Public_Data_Connector SHALL extract text from the snapshot and submit the extracted text to the Api RAG indexing contract.
5. IF a fetch request targets a source that is not an Approved_Public_Source, THEN THE Public_Data_Connector SHALL reject the request, skip fetching, and record a rejected outcome with the disallowed source URL.
6. IF a source requires authentication, presents a CAPTCHA, or is restricted by terms or privacy rules, THEN THE Public_Data_Connector SHALL classify the source as not approved and SHALL NOT retrieve citizen-level records.
7. WHEN two captures of the same source produce identical content, THE Public_Data_Connector SHALL produce identical content hashes for both captures.
8. WHEN the Public_Data_Connector completes processing of a fetch request, THE Public_Data_Connector SHALL emit an AuditEvent recording the action, source URL, content hash, and outcome.
9. IF a fetch request for an Approved_Public_Source fails because of a network error, the 30-second fetch timeout being exceeded, a non-success response, or the document exceeding the 50 MB maximum snapshot size, THEN THE Public_Data_Connector SHALL abort the fetch, skip RAG submission, and record a failed outcome with the source URL and the failure reason.
10. IF text extraction from a stored raw snapshot yields no non-whitespace text, THEN THE Public_Data_Connector SHALL skip RAG submission, retain the stored raw snapshot, and record a failed-extraction outcome with the source URL.

### Requirement 2: Notification Service Worker

**User Story:** As an auditor, supervisor, or citizen, I want notifications delivered through a dedicated notification worker, so that alerts, escalations, and portal updates are processed asynchronously and recorded.

#### Acceptance Criteria

1. THE Notification_Worker SHALL be implemented as a Worker that follows the WorkerHost runtime contract used by existing workers.
2. WHEN the Notification_Worker consumes a notification job from its queue, THE Notification_Worker SHALL resolve the NotificationItem identifier carried by the job and deliver the corresponding NotificationItem through the `InApp` Notification_Channel.
3. WHEN the Notification_Worker delivers a NotificationItem with status `Queued`, THE Notification_Worker SHALL set that NotificationItem status to `Sent`.
4. WHEN the Notification_Worker delivers a NotificationItem, THE Notification_Worker SHALL deliver it to the recipient designated on the NotificationItem, where recipients include auditor alerts, administrator alerts, supervisor escalations, and citizen portal updates.
5. WHERE a Notification_Channel other than `InApp` is selected, THE Notification_Worker SHALL route the NotificationItem through the Notification_Channel abstraction without requiring changes to notification-producing code.
6. IF no production Notification_Channel implementation is configured for a requested channel, THEN THE Notification_Worker SHALL deliver through the `InApp` Notification_Channel, set the NotificationItem status to `Sent`, and record on the delivery outcome that the requested channel was unavailable.
7. WHEN the Notification_Worker delivers a NotificationItem, THE Notification_Worker SHALL emit an AuditEvent recording the recipient, channel, and delivery outcome.
8. WHEN a notification job referencing a NotificationItem already in status `Sent` is consumed, THE Notification_Worker SHALL leave that NotificationItem in the `Sent` state and SHALL NOT transition it to `Sent` more than once.
9. IF delivery of a NotificationItem fails, THEN THE Notification_Worker SHALL leave that NotificationItem in the `Queued` state and SHALL emit an AuditEvent recording the failed delivery outcome.
10. IF a notification job references a NotificationItem identifier that does not exist, THEN THE Notification_Worker SHALL skip the job without changing any state and SHALL emit an AuditEvent recording a rejected outcome.

### Requirement 3: Sandbox Failure and Latency Simulator

**User Story:** As a sandbox administrator, I want to configure provider failure and latency rules, so that I can demonstrate how the intelligence pipeline behaves under degraded government-data conditions.

#### Acceptance Criteria

1. WHEN an operator sends `POST /sandbox/admin/failure-rules` with a valid Failure_Rule definition, THE Api SHALL create the Failure_Rule, assign a rule identifier, mark the rule active, and return the created Failure_Rule including its rule identifier and active status.
2. THE Api SHALL treat a Failure_Rule definition as valid only WHEN it specifies a target provider code matching an existing Sandbox_Provider and exactly one supported behavior from the set `Offline`, `StaleData`, `PartialData`, `RateLimited`, and `ServerError`.
3. THE Api SHALL accept an optional injected latency value for a Failure_Rule as an integer from 0 to 60000 milliseconds.
4. IF an operator sends `POST /sandbox/admin/failure-rules` with a definition that omits the provider code, references an unknown provider, specifies an unsupported behavior, or specifies an injected latency outside 0 to 60000 milliseconds, THEN THE Api SHALL return a validation error and SHALL NOT create a Failure_Rule.
5. WHEN an operator sends `DELETE /sandbox/admin/failure-rules/{ruleId}` for an existing Failure_Rule, THE Api SHALL deactivate and remove that Failure_Rule and return a success outcome.
6. IF an operator sends `DELETE /sandbox/admin/failure-rules/{ruleId}` for a rule identifier that does not exist, THEN THE Api SHALL return a not-found result.
7. WHILE an active Failure_Rule with behavior `Offline` applies to a Sandbox_Provider, THE Api SHALL cause reads from that Sandbox_Provider to report the provider as offline and SHALL NOT return normal records.
8. WHILE an active Failure_Rule with behavior `RateLimited` applies to a Sandbox_Provider, THE Api SHALL respond to reads from that Sandbox_Provider with HTTP status 429.
9. WHILE an active Failure_Rule with behavior `ServerError` applies to a Sandbox_Provider, THE Api SHALL respond to reads from that Sandbox_Provider with HTTP status 500.
10. WHILE an active Failure_Rule with behavior `StaleData` applies to a Sandbox_Provider, THE Api SHALL return records whose as-of timestamp is earlier than the current read time from that Sandbox_Provider read.
11. WHILE an active Failure_Rule with behavior `PartialData` applies to a Sandbox_Provider, THE Api SHALL return a strict subset containing fewer records than the Sandbox_Provider's normal response.
12. WHILE an active Failure_Rule specifies an injected latency value for a Sandbox_Provider, THE Api SHALL delay reads from that Sandbox_Provider by at least the specified latency value, in addition to any delay from the rule's failure behavior, before responding.
13. WHEN more than one active Failure_Rule targets the same Sandbox_Provider, THE Api SHALL apply the most recently created active Failure_Rule.
14. WHEN no active Failure_Rule applies to a Sandbox_Provider, THE Api SHALL return that Sandbox_Provider's normal response.
15. WHEN a Failure_Rule is created or deleted, THE Api SHALL emit an AuditEvent recording the action, provider code, rule identifier, and behavior.

### Requirement 4: Sandbox Profile Editing and Asset Authoring

**User Story:** As a sandbox administrator, I want to edit synthetic profiles and add assets with an expected risk band, so that I can build targeted demo and evaluation cases.

#### Acceptance Criteria

1. WHEN an operator sends `PATCH /sandbox/admin/profiles/{syntheticPersonId}` with profile field updates for an existing Synthetic_Profile, where each updated text field is between 1 and 256 characters, THE Api SHALL apply all updates atomically and return the updated Synthetic_Profile.
2. WHEN an operator sends `PATCH /sandbox/admin/profiles/{syntheticPersonId}` including an Expected_Risk_Band value that matches, case-sensitively, one of `Low`, `Medium`, `High`, or `Critical`, THE Api SHALL store the Expected_Risk_Band on the Synthetic_Profile.
3. IF an operator sends `PATCH /sandbox/admin/profiles/{syntheticPersonId}` or `POST /sandbox/admin/profiles/{syntheticPersonId}/assets` for a `syntheticPersonId` that does not exist, THEN THE Api SHALL return a not-found result and SHALL NOT modify any data.
4. WHEN an operator sends `POST /sandbox/admin/profiles/{syntheticPersonId}/assets` with an asset of type vehicle, property, utility bill, business, travel, or tax return whose text fields are each between 1 and 256 characters, THE Api SHALL add the asset to the Synthetic_Profile and return the updated Synthetic_Profile.
5. IF an operator sends `POST /sandbox/admin/profiles/{syntheticPersonId}/assets` with an asset type that is not one of vehicle, property, utility bill, business, travel, or tax return, or with a text field outside the 1 to 256 character range, THEN THE Api SHALL reject the request with a validation error and SHALL NOT add an asset.
6. IF an operator sends a `PATCH` or asset request with an Expected_Risk_Band value that does not match, case-sensitively, one of `Low`, `Medium`, `High`, or `Critical`, THEN THE Api SHALL reject the request with a validation error and SHALL NOT modify any data.
7. IF a Synthetic_Profile already holds 100 assets of the requested asset type, THEN THE Api SHALL reject the `POST /sandbox/admin/profiles/{syntheticPersonId}/assets` request with a limit-reached error and SHALL NOT add an asset.
8. WHEN any `PATCH` or asset request is rejected, THE Api SHALL leave the Synthetic_Profile unchanged with no partial updates applied.
9. WHEN a Synthetic_Profile is successfully edited or an asset is successfully added, THE Api SHALL emit an AuditEvent within 5 seconds recording the action, `syntheticPersonId`, and the changed fields or asset type.

### Requirement 5: Embedding-Based RAG Retrieval with Pluggable Vector Store

**User Story:** As a policy analyst, I want embedding-based retrieval behind a pluggable vector store with a deterministic fallback, so that RAG grounding improves where embeddings are available while remaining runnable offline.

#### Acceptance Criteria

1. THE Api SHALL retrieve RAG context through a Vector_Store interface that abstracts the underlying embedding storage implementation.
2. WHERE an Embedding_Provider is configured, THE Api SHALL generate embeddings for query text and retrieve the top K matching chunks from the Vector_Store using embedding similarity, where K is a configurable integer from 1 to 50 inclusive with a default of 5.
3. WHERE no Embedding_Provider is configured, THE Api SHALL retrieve RAG context using the Deterministic_Fallback.
4. IF the Vector_Store holds fewer than K chunks for a query, THEN THE Api SHALL return all available chunks in ranked order without empty or placeholder entries.
5. WHEN the Deterministic_Fallback is used and the same query is issued twice against an unchanged document set, THE Api SHALL return the same chunk identifiers in the same ordered positions for both queries.
6. WHEN a RAG document is indexed, THE Embedding_Worker path SHALL generate embeddings for that document's chunks and store them in the Vector_Store.
7. WHEN a RAG retrieval result is returned, THE Api SHALL include citation metadata containing the source document identifier and chunk identifier for each retrieved chunk, and SHALL indicate the retrieval path used as either `embedding` or `deterministic_fallback`.
8. IF an Embedding_Provider call does not return within a configurable timeout of 10 seconds by default, or returns an error, THEN THE Api SHALL retrieve RAG context using the Deterministic_Fallback, set the retrieval path indicator to `deterministic_fallback`, and record that the embedding path was unavailable.
9. THE Api SHALL exclude raw PII and private citizen records from RAG retrieval results across all retrieval paths.

### Requirement 6: Explainability Evidence Guardrail

**User Story:** As an auditor, I want every AI explanation claim to be backed by structured evidence or a cited policy, so that explanations are trustworthy and defensible.

#### Acceptance Criteria

1. WHEN the Api generates an explanation, THE Api SHALL validate that explanation against the Explainability_Guardrail and SHALL withhold the explanation from the response until validation completes.
2. WHEN an AI-generated explanation claim maps to at least one Evidence_Reference, THE Api SHALL accept that claim as grounded.
3. IF an AI-generated explanation contains a claim with no Evidence_Reference, THEN THE Api SHALL flag that claim as ungrounded and SHALL attach an ungrounded indicator to that claim in the response so that the claim is not labeled as evidence-backed.
4. THE Api SHALL expose the Explainability_Guardrail validation outcome in the explanation response, including the count of grounded claims, the count of ungrounded claims, and the total claim count, each as a non-negative integer where grounded count plus ungrounded count equals the total claim count.
5. THE Api SHALL ensure that every ungrounded claim in a returned explanation carries the ungrounded indicator and that no grounded claim carries it, so that grounded and ungrounded claims are distinguishable by inspecting the response.
6. WHEN the Explainability_Guardrail evaluates an explanation, THE Api SHALL emit an AuditEvent recording the explanation identifier, the grounded claim count, and the ungrounded claim count.
7. IF the Explainability_Guardrail evaluation cannot complete for an explanation, THEN THE Api SHALL withhold the explanation from the response, SHALL return an error response indicating that guardrail validation failed, and SHALL retain the unvalidated explanation without presenting any of its claims as evidence-backed.
8. WHEN every claim in an AI-generated explanation is ungrounded, THE Api SHALL set the grounded claim count to zero, SHALL attach the ungrounded indicator to all claims, and SHALL not present the explanation as evidence-backed.

### Requirement 7: Worker Runtime Consistency

**User Story:** As a platform operator, I want the new workers to follow the existing runtime conventions, so that they run identically across File and LocalStack modes and integrate with existing operations tooling.

#### Acceptance Criteria

1. THE Public_Data_Connector and Notification_Worker SHALL select queue infrastructure from the value of the `TAXNET_QUEUE_MODE` environment variable and object-store infrastructure from the value of the `TAXNET_OBJECT_STORE_MODE` environment variable, where each variable is evaluated independently and accepts only the values `File` or `LocalStack`.
2. IF `TAXNET_QUEUE_MODE` or `TAXNET_OBJECT_STORE_MODE` is unset or set to a value other than `File` or `LocalStack`, THEN THE worker SHALL default that variable's selection to `File` mode.
3. WHERE Runtime_Mode is `File`, THE Public_Data_Connector and Notification_Worker SHALL operate against the local filesystem queues and object store without establishing any connection to external network services.
4. WHERE Runtime_Mode is `LocalStack`, THE Public_Data_Connector and Notification_Worker SHALL operate against LocalStack SQS and S3 through the existing HTTP adapters using the endpoint specified by the `LOCALSTACK_ENDPOINT` environment variable.
5. WHEN the Public_Data_Connector or Notification_Worker is run without the `--watch` argument, THE worker SHALL process up to the configured maximum messages per cycle (`TAXNET_MAX_MESSAGES`, default 5) one time and then exit with a success status code.
6. WHEN the Public_Data_Connector or Notification_Worker is run with the `--watch` argument, THE worker SHALL poll its queue at the configured interval (`TAXNET_POLL_SECONDS`, default 5 seconds) and SHALL continue polling until a cancellation signal is received.
7. IF processing a queue envelope throws an error, THEN THE worker SHALL write a failure artifact identifying the failed envelope and the error to the `taxnet-dev-worker-failures` object-store bucket, SHALL continue processing the remaining messages in the current cycle, and SHALL NOT terminate as a result of that single failure.
8. THE Public_Data_Connector and Notification_Worker SHALL be represented in the Api worker health/status surface, each exposing its worker name and queue name in the same fields used by the existing workers.

### Requirement 8: Authentication and Authorization Integration

**User Story:** As a security administrator, I want the new endpoints to enforce the existing auth and role model, so that access control is consistent across the platform.

#### Acceptance Criteria

1. THE Api SHALL resolve the current actor's roles and scopes for new endpoints through the Authorization_Catalog, evaluating CognitoJwt JWT claims first and falling back to the `X-Demo-Role`/`X-Demo-User` headers under DevelopmentHeaders mode only when no JWT-derived roles are present.
2. WHEN a request targets the sandbox failure-rule or profile-editing endpoints, THE Api SHALL authorize it under the `/sandbox/admin` path policy and allow only the roles permitted for that path in the Authorization_Catalog.
3. IF a request to a new sandbox endpoint is made by an actor whose roles are not permitted for the matched path policy, THEN THE Api SHALL deny the request with an authorization-failure response indicating the actor's roles are not permitted for the path, and SHALL NOT perform the requested operation or modify any state.
4. WHEN a request targets a new RAG or explainability endpoint, THE Api SHALL authorize it under the existing `/api/system/rag` or explanation path policy in the Authorization_Catalog and allow only the roles permitted for that path.
5. WHEN a new endpoint performs a state-changing operation, THE Api SHALL record the resolved actor identity, taken from the JWT subject claim when authenticated or the `X-Demo-User` header value under DevelopmentHeaders mode, in the emitted AuditEvent.
6. WHEN more than one Authorization_Catalog path policy matches a new endpoint's request path, THE Api SHALL apply the policy with the longest matching path prefix.
7. WHERE an actor holds the `taxnet-admin` role, THE Api SHALL authorize the actor's request to any new endpoint regardless of the matched path policy's permitted-role list.
