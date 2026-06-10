import React, { useState } from "react";
import {
  Command,
  Database,
  FileText,
  KeyRound,
  Layers3,
  Save,
  Search,
  ShieldCheck,
  Sparkles,
  Workflow
} from "lucide-react";

function pct(value) {
  return `${Math.round(Number(value || 0) * 100)}%`;
}
function RagWorkspace({ rag, feedRagDocument, queryRag, ragResult, modelGateway, invokeModel, modelResult, selectedCaseId }) {
  return (
    <div className="page-stack">
      <div className="hero-strip">
        <div>
          <p className="eyebrow">RAG Policy Service</p>
          <h3>Ground explanations in policy chunks, citations, and model gateway guardrails.</h3>
        </div>
        <span className="risk-pill low">{rag?.chunkCount || 0} chunks indexed</span>
      </div>
      <div className="system-grid">
        <Panel title="Policy Ingestion & Retrieval" subtitle="Index source text, query context, and inspect citations." icon={Layers3}>
          <RagFeedCenter feedRagDocument={feedRagDocument} />
          <RagQueryCenter queryRag={queryRag} ragResult={ragResult} />
        </Panel>
        <Panel title="Model Gateway Test Bench" subtitle="Invoke local/template or external-ready routes with redaction." icon={Sparkles}>
          <ModelInvokeCenter invokeModel={invokeModel} modelResult={modelResult} selectedCaseId={selectedCaseId} />
          <div className="cards-grid">
            {modelGateway?.routing?.map((route) => (
              <article className="small-card" key={route.task}>
                <strong>{route.task}</strong>
                <p>{route.route}</p>
                <small>{route.reason}</small>
              </article>
            ))}
          </div>
        </Panel>
      </div>
      <Panel title="Indexed Policy Memory" subtitle="Current searchable documents and citation sources." icon={FileText}>
        <div className="cards-grid">
          {rag?.documents?.map((doc) => (
            <article className="small-card" key={doc.id}>
              <strong>{doc.title}</strong>
              <p>{doc.summary}</p>
              <small>{doc.sourceType} - {doc.url}</small>
            </article>
          ))}
        </div>
      </Panel>
    </div>
  );
}

function BackendSystems({ workers, infra, audit, objectStore, notifications, persistence, providers }) {
  return (
    <div className="page-stack">
      <div className="hero-strip">
        <div>
          <p className="eyebrow">Backend service map</p>
          <h3>Workers, connectors, storage, audit logs, and production replacement contracts.</h3>
        </div>
        <span className="risk-pill low">{providers?.length || 0} connectors</span>
      </div>
      <div className="system-grid">
        <Panel title="Worker Queues" subtitle="SQS-style contracts and current worker health." icon={Workflow}>
          <div className="cards-grid">
            {workers?.workers?.map((w) => <WorkerCard worker={w} key={w.name} />)}
          </div>
        </Panel>
        <Panel title="Connector Readiness" subtitle="Sandbox adapters with official-provider credential slots." icon={KeyRound}>
          <div className="cards-grid">
            {providers?.map((provider) => (
              <article className="small-card" key={provider.providerCode}>
                <strong>{provider.providerCode}</strong>
                <p>{provider.name}</p>
                <small>{provider.mode} - {provider.credentialSecretName}</small>
              </article>
            ))}
          </div>
        </Panel>
      </div>
      <div className="system-grid">
        <Panel title="Infrastructure Readiness" subtitle="Production replacements mapped to MVP stores." icon={Database}>
          <div className="cards-grid">
            {infra?.stores?.map((store) => (
              <article className="small-card" key={store.name}>
                <strong>{store.name}</strong>
                <p>{store.mvp}</p>
                <small>{store.status} to {store.replacement}</small>
              </article>
            ))}
          </div>
        </Panel>
        <Panel title="Persistent Backend State" subtitle="File-backed operational store used by the MVP service." icon={Save}>
          <div className="cards-grid">
            <article className="small-card">
              <strong>State snapshot</strong>
              <p>{persistence?.statePath || "Loading state path..."}</p>
              <small>{Number(persistence?.stateBytes || 0).toLocaleString()} bytes</small>
            </article>
            <article className="small-card">
              <strong>Object files</strong>
              <p>{persistence?.objectRoot || "Loading object root..."}</p>
              <small>{persistence?.objectFiles || 0} files on disk</small>
            </article>
            <article className="small-card">
              <strong>Snapshot counts</strong>
              <p>{persistence?.snapshotCollections?.people || 0} people, {persistence?.snapshotCollections?.cases || 0} cases</p>
              <small>{persistence?.snapshotCollections?.ragDocuments || 0} RAG docs, {persistence?.snapshotCollections?.reports || 0} reports</small>
            </article>
          </div>
        </Panel>
      </div>
      <div className="system-grid">
        <Panel title="Audit, Object Store, Notifications" subtitle="CloudWatch/S3/SNS-ready metadata streams." icon={ShieldCheck}>
          <div className="ops-grid">
            <div>
              <strong>Audit log</strong>
              {(audit?.items || []).slice(0, 5).map((event) => (
                <article className="job-row" key={event.id}>
                  <span className="risk-pill low">{event.outcome}</span>
                  <div><strong>{event.action}</strong><small>{event.actor} - {event.resource}</small></div>
                </article>
              ))}
            </div>
            <div>
              <strong>Object store</strong>
              {(objectStore?.objects || []).slice(0, 5).map((object) => (
                <article className="job-row" key={object.uri}>
                  <span className="risk-pill low">S3</span>
                  <div><strong>{object.bucket}</strong><small>{object.key}</small></div>
                </article>
              ))}
            </div>
            <div>
              <strong>Notifications</strong>
              {(notifications?.items || []).slice(0, 5).map((item) => (
                <article className="job-row" key={item.id}>
                  <span className="risk-pill medium">{item.status}</span>
                  <div><strong>{item.subject}</strong><small>{item.recipient} - {item.channel}</small></div>
                </article>
              ))}
              {(!notifications?.items || notifications.items.length === 0) && <EmptyState title="No notifications queued" />}
            </div>
          </div>
        </Panel>
      </div>
    </div>
  );
}

function System({ workers, rag, authz, modelGateway, infra, audit, objectStore, notifications, persistence, feedRagDocument, queryRag, ragResult, invokeModel, modelResult, selectedCaseId }) {
  return (
    <div className="page-stack">
      <div className="system-grid">
        <Panel title="Worker Pipeline" subtitle="SQS-style queues, retries, and DLQs." icon={Workflow}>
          <div className="cards-grid">
            {workers?.workers?.map((w) => <WorkerCard worker={w} key={w.name} />)}
          </div>
          <div className="job-list system-jobs">
            {(workers?.jobs || []).slice(0, 5).map((job) => (
              <article key={job.id} className="job-row">
                <span className={`risk-pill ${job.status === "Failed" ? "critical" : job.status === "SucceededWithWarnings" ? "medium" : "low"}`}>{job.status}</span>
                <div><strong>{job.type}</strong><small>{job.source} - {job.recordsCreated}/{job.recordsProcessed}</small></div>
              </article>
            ))}
          </div>
        </Panel>
        <Panel title="Authorization Matrix" subtitle="Cognito-ready roles and scopes." icon={LockKeyhole}>
          <div className="cards-grid">
            {authz?.roles?.slice(0, 8).map((role) => (
              <article className="small-card" key={role.role}>
                <strong>{role.role}</strong>
                <p>{role.description}</p>
                <small>{role.scopes.slice(0, 3).join(", ")}</small>
              </article>
            ))}
          </div>
        </Panel>
      </div>
      <div className="system-grid">
        <Panel title="RAG Policy Memory" subtitle={`${rag?.chunkCount || 0} indexed chunks with citation metadata.`} icon={Layers3}>
          <RagFeedCenter feedRagDocument={feedRagDocument} />
          <RagQueryCenter queryRag={queryRag} ragResult={ragResult} />
          <div className="cards-grid">
            {rag?.documents?.map((doc) => (
              <article className="small-card" key={doc.id}>
                <strong>{doc.title}</strong>
                <p>{doc.summary}</p>
                <small>{doc.sourceType}</small>
              </article>
            ))}
          </div>
        </Panel>
        <Panel title="Model Gateway" icon={Sparkles}>
          <ModelInvokeCenter invokeModel={invokeModel} modelResult={modelResult} selectedCaseId={selectedCaseId} />
          <div className="cards-grid">
            {modelGateway?.routing?.map((route) => (
              <article className="small-card" key={route.task}>
                <strong>{route.task}</strong>
                <p>{route.route}</p>
                <small>{route.reason}</small>
              </article>
            ))}
          </div>
        </Panel>
      </div>
      <div className="system-grid">
        <Panel title="Infrastructure Readiness" subtitle="Production replacements mapped to MVP services." icon={Database}>
          <div className="cards-grid">
            {infra?.stores?.map((store) => (
              <article className="small-card" key={store.name}>
                <strong>{store.name}</strong>
                <p>{store.mvp}</p>
                <small>{store.status} to {store.replacement}</small>
              </article>
            ))}
          </div>
        </Panel>
        <Panel title="Persistent Backend State" subtitle="Snapshot plus object-store files survive service restarts." icon={Save}>
          <div className="cards-grid">
            <article className="small-card">
              <strong>Snapshot</strong>
              <p>{persistence?.statePath || "Loading state path..."}</p>
              <small>{Number(persistence?.stateBytes || 0).toLocaleString()} bytes</small>
            </article>
            <article className="small-card">
              <strong>Object store</strong>
              <p>{persistence?.objectRoot || "Loading object root..."}</p>
              <small>{persistence?.objectFiles || 0} files</small>
            </article>
          </div>
        </Panel>
      </div>
      <div className="system-grid">
        <Panel title="Audit, Objects & Notifications" subtitle="CloudWatch/S3/SNS-ready operational metadata." icon={ShieldCheck}>
          <div className="ops-grid">
            <div>
              <strong>Audit log</strong>
              {(audit?.items || []).slice(0, 5).map((event) => (
                <article className="job-row" key={event.id}>
                  <span className="risk-pill low">{event.outcome}</span>
                  <div><strong>{event.action}</strong><small>{event.actor} - {event.resource}</small></div>
                </article>
              ))}
            </div>
            <div>
              <strong>Object store</strong>
              {(objectStore?.objects || []).slice(0, 5).map((object) => (
                <article className="job-row" key={object.uri}>
                  <span className="risk-pill low">S3</span>
                  <div><strong>{object.bucket}</strong><small>{object.key}</small></div>
                </article>
              ))}
            </div>
            <div>
              <strong>Notifications</strong>
              {(notifications?.items || []).slice(0, 5).map((item) => (
                <article className="job-row" key={item.id}>
                  <span className="risk-pill medium">{item.status}</span>
                  <div><strong>{item.subject}</strong><small>{item.recipient} - {item.channel}</small></div>
                </article>
              ))}
              {(!notifications?.items || notifications.items.length === 0) && <EmptyState title="No notifications queued" />}
            </div>
          </div>
        </Panel>
      </div>
    </div>
  );
}

function RagFeedCenter({ feedRagDocument }) {
  const [title, setTitle] = useState("Punjab property valuation bulletin");
  const [sourceType, setSourceType] = useState("GovernmentPage");
  const [url, setUrl] = useState("https://example.gov.pk/property-valuation");
  const [tags, setTags] = useState("property,valuation,tax-risk");
  const [content, setContent] = useState("Official valuation tables indicate that undeclared high-value plots and commercial units should be correlated with filer status, declared income, and utility consumption before enforcement.");

  async function submit() {
    await feedRagDocument({
      title,
      sourceType,
      url,
      content,
      tags: tags.split(",").map((tag) => tag.trim()).filter(Boolean)
    });
  }

  return (
    <div className="rag-feed">
      <div className="feed-controls">
        <label>Title<input value={title} onChange={(e) => setTitle(e.target.value)} /></label>
        <label>Source<select value={sourceType} onChange={(e) => setSourceType(e.target.value)}>
          <option>GovernmentPage</option>
          <option>PolicyPDF</option>
          <option>TaxCircular</option>
          <option>CourtGuidance</option>
        </select></label>
        <label>URL<input value={url} onChange={(e) => setUrl(e.target.value)} /></label>
      </div>
      <textarea className="textarea-large compact-textarea" value={content} onChange={(e) => setContent(e.target.value)} />
      <div className="upload-strip">
        <input value={tags} onChange={(e) => setTags(e.target.value)} aria-label="Tags" />
        <button onClick={submit}><Layers3 size={15} /> Index policy</button>
      </div>
    </div>
  );
}

function RagQueryCenter({ queryRag, ragResult }) {
  const [query, setQuery] = useState("What policy supports human review before escalation?");
  const [taskType, setTaskType] = useState("AuditExplanation");

  async function submit() {
    await queryRag({
      query,
      taskType,
      jurisdiction: "Pakistan",
      topK: 5,
      tags: ["audit", "human-review", "citizen"]
    });
  }

  return (
    <div className="rag-feed query-console">
      <div className="feed-controls">
        <label>Question<input value={query} onChange={(e) => setQuery(e.target.value)} /></label>
        <label>Task<select value={taskType} onChange={(e) => setTaskType(e.target.value)}>
          <option>AuditExplanation</option>
          <option>CitizenExplanation</option>
          <option>PolicyQuestion</option>
          <option>ReportDraft</option>
        </select></label>
      </div>
      <div className="upload-strip">
        <button onClick={submit}><Search size={15} /> Query RAG</button>
      </div>
      {ragResult && (
        <div className="result-box">
          <strong>{pct(ragResult.retrievalConfidence)} retrieval confidence</strong>
          <p>{ragResult.rewrittenQuery}</p>
          <div className="citation-list">
            {ragResult.citations.map((citation) => <span key={citation.chunkId}>{citation.title}</span>)}
          </div>
        </div>
      )}
    </div>
  );
}

function ModelInvokeCenter({ invokeModel, modelResult, selectedCaseId }) {
  const [taskType, setTaskType] = useState("AuditExplanation");
  const [allowExternalProvider, setAllowExternalProvider] = useState(false);
  const [prompt, setPrompt] = useState("Generate an evidence-grounded explanation with citations and human review warning.");

  async function submit() {
    await invokeModel({
      taskType,
      prompt,
      caseId: selectedCaseId || "case-P001",
      preferredProvider: "auto",
      allowExternalProvider
    });
  }

  return (
    <div className="rag-feed query-console">
      <div className="feed-controls">
        <label>Task<select value={taskType} onChange={(e) => setTaskType(e.target.value)}>
          <option>AuditExplanation</option>
          <option>CitizenExplanation</option>
          <option>ReportDraft</option>
          <option>PolicyQuestion</option>
        </select></label>
        <label className="checkbox-line"><input type="checkbox" checked={allowExternalProvider} onChange={(e) => setAllowExternalProvider(e.target.checked)} /> External route</label>
      </div>
      <textarea className="textarea-large compact-textarea" value={prompt} onChange={(e) => setPrompt(e.target.value)} />
      <div className="upload-strip">
        <button onClick={submit}><Sparkles size={15} /> Invoke model</button>
      </div>
      {modelResult && (
        <div className="result-box">
          <strong>{modelResult.selectedProvider}</strong>
          <p>{modelResult.output}</p>
          <small>{modelResult.promptTokens + modelResult.completionTokens} tokens - ${modelResult.estimatedCostUsd}</small>
        </div>
      )}
    </div>
  );
}
function Panel({ title, subtitle, icon: Icon, children }) {
  return (
    <section className="panel">
      <header>
        <div>
          <h3>{Icon && <Icon size={17} />} {title}</h3>
          {subtitle && <p>{subtitle}</p>}
        </div>
      </header>
      <div className="panel-body">{children}</div>
    </section>
  );
}

function WorkerCard({ worker }) {
  return (
    <article className="small-card">
      <strong>{worker.name}</strong>
      <p>{worker.queueName}</p>
      <small>{worker.status} - depth {worker.queueDepth} - {worker.processedToday} processed</small>
    </article>
  );
}

function EmptyState({ title }) {
  return <div className="empty-state"><Command size={24} /><strong>{title}</strong></div>;
}

export { BackendSystems, RagWorkspace, System };