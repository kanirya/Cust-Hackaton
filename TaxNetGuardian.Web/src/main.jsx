import React, { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  Activity,
  AlertTriangle,
  ArrowRight,
  BadgeCheck,
  BarChart3,
  Bell,
  Bot,
  BriefcaseBusiness,
  Building2,
  Car,
  CheckCircle2,
  CircleDollarSign,
  ClipboardCheck,
  Command,
  Database,
  FileText,
  Filter,
  Gauge,
  GitBranch,
  Globe2,
  Home,
  KeyRound,
  Landmark,
  Layers3,
  LifeBuoy,
  LockKeyhole,
  MapPin,
  Network,
  PieChart,
  RefreshCw,
  Save,
  Scale,
  Search,
  Settings,
  Shield,
  ShieldCheck,
  Sparkles,
  TerminalSquare,
  Upload,
  UserCircle,
  Users,
  Workflow,
  Zap
} from "lucide-react";
import "./styles.css";
import { BackendSystems, RagWorkspace, System } from "./components/system/SystemViews.jsx";
import { EmptyState, FeedItem, InfoRows, JsonPreview, Metric, Panel, RadialScore } from "./components/common/Primitives.jsx";
import { GraphCanvas } from "./components/graph/GraphCanvas.jsx";
import { Sidebar, TopBar } from "./components/shell/AppShell.jsx";
import { Overview } from "./pages/OverviewPage.jsx";
import { CaseQueue } from "./pages/CaseQueuePage.jsx";

const roles = [
  "taxnet-admin",
  "taxnet-supervisor",
  "taxnet-auditor",
  "taxnet-sandbox-admin",
  "taxnet-policy-analyst",
  "taxnet-model-admin",
  "taxnet-citizen"
];

const navItems = [
  { id: "overview", label: "National Dashboard", icon: BarChart3 },
  { id: "queue", label: "Case Queue", icon: BriefcaseBusiness },
  { id: "investigation", label: "Graph Investigation", icon: Network },
  { id: "sandbox", label: "Gov Data Sandbox", icon: TerminalSquare },
  { id: "rag", label: "RAG Policy", icon: Layers3 },
  { id: "backend", label: "Backend Systems", icon: Database },
  { id: "citizen", label: "Citizen Portal", icon: Users },
  { id: "system", label: "System Control", icon: Workflow }
];

const api = async (path, options = {}, role = getRole()) => {
  const response = await fetch(path, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      "X-Demo-Role": role,
      "X-Demo-User": "taxnet-demo-operator",
      ...(options.headers || {})
    }
  });
  if (!response.ok) {
    throw new Error(await response.text());
  }
  return response.json();
};

function getInitialPage() {
  const path = window.location.pathname.toLowerCase();
  if (path.includes("sandbox")) return "sandbox";
  if (path.includes("rag") || path.includes("policy")) return "rag";
  if (path.includes("backend") || path.includes("infra") || path.includes("audit")) return "backend";
  if (path.includes("system")) return "system";
  if (path.includes("citizen")) return "citizen";
  if (path.includes("cases") || path.includes("queue")) return "queue";
  if (path.includes("graph") || path.includes("investigation")) return "investigation";
  return "overview";
}

function getRole() {
  return localStorage.getItem("taxnet.role") || "taxnet-admin";
}

function money(value) {
  return `PKR ${Number(value || 0).toLocaleString()}`;
}

function pct(value) {
  return `${Math.round(Number(value || 0) * 100)}%`;
}

function riskClass(value) {
  return String(value || "low").toLowerCase();
}

function compact(value) {
  const n = Number(value || 0);
  if (n >= 1_000_000_000) return `PKR ${(n / 1_000_000_000).toFixed(1)}B`;
  if (n >= 1_000_000) return `PKR ${(n / 1_000_000).toFixed(1)}M`;
  return money(n);
}

function App() {
  const [page, setPage] = useState(getInitialPage());
  const [role, setRole] = useState(getRole());
  const [summary, setSummary] = useState(null);
  const [cases, setCases] = useState([]);
  const [selectedCaseId, setSelectedCaseId] = useState("case-P001");
  const [selectedCase, setSelectedCase] = useState(null);
  const [graph, setGraph] = useState(null);
  const [providers, setProviders] = useState([]);
  const [profiles, setProfiles] = useState([]);
  const [selectedProfile, setSelectedProfile] = useState(null);
  const [workers, setWorkers] = useState(null);
  const [rag, setRag] = useState(null);
  const [datasetHub, setDatasetHub] = useState(null);
  const [datasetTemplates, setDatasetTemplates] = useState([]);
  const [authz, setAuthz] = useState(null);
  const [modelGateway, setModelGateway] = useState(null);
  const [infra, setInfra] = useState(null);
  const [audit, setAudit] = useState(null);
  const [objectStore, setObjectStore] = useState(null);
  const [notifications, setNotifications] = useState(null);
  const [persistence, setPersistence] = useState(null);
  const [ragResult, setRagResult] = useState(null);
  const [modelResult, setModelResult] = useState(null);
  const [assistantAnswer, setAssistantAnswer] = useState(null);
  const [query, setQuery] = useState("Why was this case marked critical?");
  const [toast, setToast] = useState("");
  const [citizen, setCitizen] = useState(null);

  useEffect(() => {
    localStorage.setItem("taxnet.role", role);
  }, [role]);

  useEffect(() => {
    refreshAll();
  }, []);

  useEffect(() => {
    if (selectedCaseId) {
      loadCase(selectedCaseId);
    }
  }, [selectedCaseId]);

  const selectedEntityId = selectedCase?.caseItem?.entityId || cases[0]?.entityId;

  async function refreshAll() {
    const [summaryData, caseData, providerData, workerData, ragData, datasetData, templateData, authData, modelData, infraData, auditData, objectData, notificationData, persistenceData, citizenData] = await Promise.all([
      api("/api/dashboard/summary"),
      api("/api/cases"),
      api("/api/sandbox/providers"),
      api("/api/system/workers"),
      api("/api/system/rag"),
      api("/api/sandbox/datasets"),
      api("/api/sandbox/datasets/templates"),
      api("/api/authz"),
      api("/api/system/model-gateway"),
      api("/api/system/infra"),
      api("/api/system/audit"),
      api("/api/system/object-store"),
      api("/api/system/notifications"),
      api("/api/system/persistence"),
      api("/api/citizen/me", {}, "taxnet-citizen")
    ]);
    setSummary(summaryData);
    setCases(caseData.items);
    setProviders(providerData.value || providerData);
    setWorkers(workerData);
    setRag(ragData);
    setDatasetHub(datasetData);
    setDatasetTemplates(templateData);
    setAuthz(authData);
    setModelGateway(modelData);
    setInfra(infraData);
    setAudit(auditData);
    setObjectStore(objectData);
    setNotifications(notificationData);
    setPersistence(persistenceData);
    setCitizen(citizenData);
    if (!selectedCaseId && caseData.items.length) setSelectedCaseId(caseData.items[0].id);
    await loadProfiles();
  }

  async function loadCase(caseId) {
    const detail = await api(`/api/cases/${caseId}`);
    setSelectedCase(detail);
    const g = await api(`/api/graph/entities/${detail.caseItem.entityId}/neighborhood`);
    setGraph(g);
  }

  async function loadProfiles() {
    const data = await api("/api/sandbox/profiles?limit=120");
    setProfiles(data.items);
    if (data.items?.length) {
      const detail = await api(`/api/sandbox/profiles/${data.items[0].id}`);
      setSelectedProfile(detail);
    }
  }

  function navigate(id) {
    setPage(id);
    const urls = {
      overview: "/",
      queue: "/cases",
      investigation: "/graph",
      sandbox: "/sandbox",
      rag: "/rag",
      backend: "/backend",
      citizen: "/citizen",
      system: "/system"
    };
    const url = urls[id] || "/";
    window.history.pushState({}, "", url);
  }

  async function runPipeline() {
    const result = await api("/api/ingestion/run", { method: "POST", body: "{}" }, role);
    setToast(`${result.importedProfiles} profiles imported, ${result.cases} cases scored.`);
    await refreshAll();
  }

  async function askAssistant(customQuestion = query) {
    if (!selectedCaseId) return;
    const result = await api(`/api/assistant/cases/${selectedCaseId}/ask`, {
      method: "POST",
      body: JSON.stringify({ question: customQuestion })
    }, role);
    setAssistantAnswer(result);
  }

  async function generateReport() {
    const result = await api(`/api/reports/cases/${selectedCaseId}`, { method: "POST", body: "{}" }, role);
    setAssistantAnswer({
      answer: `Report ${result.reportId} generated at ${result.storageUri}. ${result.caseSummary}`,
      evidenceIds: result.evidence.map((x) => x.id),
      citations: result.citations,
      warnings: [result.disclaimer],
      score: result.score.score,
      riskBand: result.score.riskBand
    });
    await loadCase(selectedCaseId);
  }

  async function assignSelectedCase() {
    if (!selectedCaseId) return;
    const result = await api(`/api/cases/${selectedCaseId}/assign`, {
      method: "POST",
      body: JSON.stringify({ assignedTo: "Senior Auditor - Lahore" })
    }, "taxnet-supervisor");
    setToast(`${result.id} assigned to ${result.assignedTo}.`);
    await refreshAll();
    await loadCase(selectedCaseId);
  }

  async function requestClarification() {
    if (!selectedCaseId) return;
    const result = await api(`/api/cases/${selectedCaseId}/request-citizen-clarification`, {
      method: "POST",
      body: "{}"
    }, "taxnet-auditor");
    setToast(`${result.id}: citizen clarification requested.`);
    await refreshAll();
    await loadCase(selectedCaseId);
  }

  async function recordDecision(decision) {
    if (!selectedCaseId) return;
    const result = await api(`/api/cases/${selectedCaseId}/decision`, {
      method: "POST",
      body: JSON.stringify({
        decision,
        notes: decision === "ClosedFalsePositive"
          ? "Citizen correction accepted after review of current ownership evidence."
          : "Structured evidence verified and case moved to next human review state."
      })
    }, "taxnet-senior-auditor");
    setToast(`${result.id} moved to ${result.status}.`);
    await refreshAll();
    await loadCase(selectedCaseId);
  }

  async function generateSandbox() {
    const result = await api("/api/sandbox/admin/generate", {
      method: "POST",
      body: JSON.stringify({ count: 180, suspiciousPercent: 28, noisePercent: 24 })
    }, "taxnet-sandbox-admin");
    setToast(`${result.profiles} sandbox profiles generated; ${result.cases} cases flagged.`);
    await refreshAll();
  }

  async function feedDataset(payload) {
    const result = await api("/api/sandbox/datasets/feed", {
      method: "POST",
      body: JSON.stringify(payload)
    }, "taxnet-sandbox-admin");
    setToast(`${result.batch.recordCount} ${result.batch.datasetType} records applied from ${result.batch.fileName}.`);
    await refreshAll();
  }

  async function feedRagDocument(payload) {
    const result = await api("/api/system/rag/documents", {
      method: "POST",
      body: JSON.stringify(payload)
    }, "taxnet-policy-analyst");
    setToast(`RAG indexed: ${result.job.source}`);
    await refreshAll();
  }

  async function queryRag(payload) {
    const result = await api("/api/system/rag/query", {
      method: "POST",
      body: JSON.stringify(payload)
    }, "taxnet-policy-analyst");
    setRagResult(result);
    setToast(`RAG retrieved ${result.chunks.length} chunks at ${pct(result.retrievalConfidence)} confidence.`);
    await refreshAll();
  }

  async function invokeModel(payload) {
    const result = await api("/api/system/model-gateway/invoke", {
      method: "POST",
      body: JSON.stringify(payload)
    }, "taxnet-model-admin");
    setModelResult(result);
    setToast(`${result.selectedProvider} handled ${result.taskType}.`);
    await refreshAll();
  }

  async function updateProvider(providerCode, payload) {
    const result = await api(`/api/sandbox/providers/${providerCode}`, {
      method: "PATCH",
      body: JSON.stringify(payload)
    }, "taxnet-sandbox-admin");
    setToast(`${result.providerCode} updated to ${result.mode} (${result.status}).`);
    await refreshAll();
  }

  async function submitCorrection() {
    const result = await api("/api/citizen/corrections", {
      method: "POST",
      body: JSON.stringify({
        caseId: selectedCaseId || "case-P001",
        correctionType: "AssetOwnershipDispute",
        message: "Please review linked ownership records and verify current asset ownership dates.",
        evidenceFileIds: ["demo-upload-placeholder"]
      })
    }, "taxnet-citizen");
    setToast(`${result.correctionId}: ${result.message}`);
    await refreshAll();
  }

  const pageTitle = {
    overview: "National Overview",
    queue: "Auditor Case Queue",
    investigation: "Case Investigation",
    sandbox: "Gov Data Sandbox",
    rag: "RAG Policy Service",
    backend: "Backend Systems",
    citizen: "Citizen Correction Portal",
    system: "System Control Plane"
  }[page];

  return (
    <div className="app-shell">
      <Sidebar page={page} navigate={navigate} navItems={navItems} />
      <main className="workspace">
        <TopBar
          title={pageTitle}
          role={role}
          roles={roles}
          setRole={setRole}
          onRefresh={refreshAll}
          onPipeline={runPipeline}
        />
        {toast && <button className="toast" onClick={() => setToast("")}>{toast}</button>}
        <div className="workspace-grid">
          <section className="content-area">
            {page === "overview" && (
              <Overview
                summary={summary}
                cases={cases}
                providers={providers}
                rag={rag}
                setPage={navigate}
                setSelectedCaseId={setSelectedCaseId}
              />
            )}
            {page === "queue" && (
              <CaseQueue
                cases={cases}
                selectedCaseId={selectedCaseId}
                setSelectedCaseId={setSelectedCaseId}
                setPage={navigate}
              />
            )}
            {page === "investigation" && (
              <Investigation
                selectedCase={selectedCase}
                graph={graph}
                query={query}
                setQuery={setQuery}
                askAssistant={askAssistant}
                generateReport={generateReport}
                assignSelectedCase={assignSelectedCase}
                requestClarification={requestClarification}
                recordDecision={recordDecision}
                assistantAnswer={assistantAnswer}
              />
            )}
            {page === "sandbox" && (
              <Sandbox
                providers={providers}
                profiles={profiles}
                selectedProfile={selectedProfile}
                setSelectedProfile={setSelectedProfile}
                generateSandbox={generateSandbox}
                datasetHub={datasetHub}
                datasetTemplates={datasetTemplates}
                feedDataset={feedDataset}
                updateProvider={updateProvider}
              />
            )}
            {page === "rag" && (
              <RagWorkspace
                rag={rag}
                feedRagDocument={feedRagDocument}
                queryRag={queryRag}
                ragResult={ragResult}
                modelGateway={modelGateway}
                invokeModel={invokeModel}
                modelResult={modelResult}
                selectedCaseId={selectedCaseId}
              />
            )}
            {page === "backend" && (
              <BackendSystems
                workers={workers}
                infra={infra}
                audit={audit}
                objectStore={objectStore}
                notifications={notifications}
                persistence={persistence}
                providers={providers}
              />
            )}
            {page === "citizen" && (
              <Citizen citizen={citizen} submitCorrection={submitCorrection} />
            )}
            {page === "system" && (
              <System
                workers={workers}
                rag={rag}
                authz={authz}
                modelGateway={modelGateway}
                infra={infra}
                audit={audit}
                objectStore={objectStore}
                notifications={notifications}
                persistence={persistence}
                feedRagDocument={feedRagDocument}
                queryRag={queryRag}
                ragResult={ragResult}
                invokeModel={invokeModel}
                modelResult={modelResult}
                selectedCaseId={selectedCaseId}
              />
            )}
          </section>
          <AssistantDrawer
            selectedCase={selectedCase}
            answer={assistantAnswer}
            query={query}
            setQuery={setQuery}
            askAssistant={askAssistant}
          />
        </div>
      </main>
    </div>
  );
}

function Investigation({ selectedCase, graph, query, setQuery, askAssistant, generateReport, assignSelectedCase, requestClarification, recordDecision, assistantAnswer }) {
  if (!selectedCase) return <EmptyState title="Loading investigation" />;
  const c = selectedCase.caseItem;
  const p = selectedCase.person;
  const timeline = selectedCase.timeline || [];
  const corrections = selectedCase.corrections || [];
  const reports = selectedCase.reports || [];
  return (
    <div className="investigation-layout">
      <section className="left-rail">
        <Panel title="Entity Profile" icon={UserCircle}>
          <div className="profile-head">
            <RadialScore value={c.score.score} band={c.score.riskBand} />
            <div>
              <h3>{p.fullName}</h3>
              <p>{p.cnicMasked}</p>
              <span className={`risk-pill ${riskClass(c.score.riskBand)}`}>{c.score.riskBand}</span>
            </div>
          </div>
          <InfoRows rows={[
            ["City", `${p.city}, ${p.province}`],
            ["Match confidence", pct(selectedCase.entity.matchConfidence)],
            ["Status", c.status],
            ["Recommended", c.score.recommendedAction]
          ]} />
        </Panel>
        <Panel title="Score Breakdown" icon={Scale}>
          {c.score.components.filter((x) => x.score > 0).map((component) => (
            <div className="score-component" key={component.name}>
              <div><strong>{component.name}</strong><span>{component.score}/{component.maxScore}</span></div>
              <p>{component.explanation}</p>
            </div>
          ))}
        </Panel>
        <Panel title="Auditor Actions" subtitle="Human-in-the-loop lifecycle controls." icon={ClipboardCheck}>
          <div className="action-stack">
            <button onClick={assignSelectedCase}><UserCircle size={16} /> Assign</button>
            <button onClick={requestClarification}><Users size={16} /> Clarify</button>
            <button onClick={() => recordDecision("EvidenceVerified")}><BadgeCheck size={16} /> Verify</button>
            <button onClick={() => recordDecision("ClosedEscalated")}><ArrowRight size={16} /> Escalate</button>
            <button onClick={() => recordDecision("ClosedFalsePositive")}><CheckCircle2 size={16} /> False positive</button>
          </div>
        </Panel>
      </section>
      <section className="graph-stage">
        <Panel title="Knowledge Graph Explorer" subtitle="Person, assets, businesses, utilities, travel, and case relationships." icon={Network}>
          <GraphCanvas graph={graph} />
        </Panel>
      </section>
      <section className="right-rail">
        <Panel title="Evidence Drawer" icon={FileText}>
          <div className="evidence-list">
            {c.evidence.map((ev) => (
              <article className="evidence-card" key={ev.id}>
                <div><strong>{ev.title}</strong><span>{ev.type}</span></div>
                <p>{ev.description}</p>
                <footer>{ev.source} - {ev.id}</footer>
              </article>
            ))}
          </div>
        </Panel>
        <Panel title="AI Investigation Assistant" icon={Bot}>
          <div className="assistant-box">
            <textarea value={query} onChange={(e) => setQuery(e.target.value)} />
            <div className="button-row">
              <button onClick={() => askAssistant(query)}><Bot size={16} /> Ask</button>
              <button onClick={generateReport}><FileText size={16} /> Report</button>
            </div>
            {assistantAnswer && <AssistantAnswer answer={assistantAnswer} />}
          </div>
        </Panel>
        <Panel title="Case Timeline" subtitle="Audit log stream for reports, corrections, and decisions." icon={Activity}>
          <div className="timeline-list">
            {timeline.slice(0, 8).map((event) => (
              <article className="timeline-event" key={event.id}>
                <span>{new Date(event.timestampUtc).toLocaleString()}</span>
                <strong>{event.eventType}</strong>
                <p>{event.summary}</p>
                <small>{event.actor}</small>
              </article>
            ))}
            {timeline.length === 0 && <EmptyState title="No timeline events" />}
          </div>
        </Panel>
        <Panel title="Corrections & Reports" icon={FileText}>
          <div className="job-list">
            {corrections.map((correction) => (
              <article className="job-row" key={correction.id}>
                <span className="risk-pill medium">{correction.status}</span>
                <div><strong>{correction.correctionType}</strong><small>{correction.message}</small></div>
              </article>
            ))}
            {reports.map((report) => (
              <article className="job-row" key={report.id}>
                <span className="risk-pill low">Report</span>
                <div><strong>{report.id}</strong><small>{report.storageUri}</small></div>
              </article>
            ))}
            {corrections.length === 0 && reports.length === 0 && <EmptyState title="No corrections or reports" />}
          </div>
        </Panel>
      </section>
    </div>
  );
}

function Sandbox({ providers, profiles, selectedProfile, setSelectedProfile, generateSandbox, datasetHub, datasetTemplates, feedDataset, updateProvider }) {
  async function openProfile(id) {
    setSelectedProfile(await api(`/api/sandbox/profiles/${id}`));
  }
  return (
    <div className="page-stack">
      <div className="hero-strip sandbox-hero">
        <div>
          <p className="eyebrow">Separate backend and UI</p>
          <h3>Government API emulator with real-provider readiness.</h3>
        </div>
        <button onClick={generateSandbox}><Database size={16} /> Generate 180 profiles</button>
      </div>
      <DatasetFeedCenter datasetHub={datasetHub} datasetTemplates={datasetTemplates} feedDataset={feedDataset} />
      <div className="provider-grid">
        {providers.map((provider) => (
          <article className="provider-card" key={provider.providerCode}>
            <div className="provider-icon"><Landmark size={20} /></div>
            <div>
              <strong>{provider.providerCode}</strong>
              <p>{provider.name}</p>
              <span className={`risk-pill ${provider.status === "Healthy" ? "low" : "medium"}`}>{provider.status}</span>
            </div>
            <small>{provider.credentialSecretName}</small>
            <button
              className="inline-action"
              onClick={() => updateProvider(provider.providerCode, {
                mode: "OfficialReady",
                baseUrl: `https://api.${provider.providerCode.toLowerCase().replaceAll("-", "")}.gov.pk`,
                credentialSecretName: provider.credentialSecretName,
                enabled: true,
                rateLimitPerMinute: provider.supportsBulkImport ? 120 : 60,
                notes: "Sandbox contract configured so official API credentials can be swapped through Secrets Manager."
              })}
            >
              <KeyRound size={14} /> Official-ready
            </button>
          </article>
        ))}
      </div>
      <div className="sandbox-layout">
        <Panel title="Synthetic Profiles" subtitle={`${profiles.length} profiles loaded from sandbox.`} icon={Users}>
          <div className="data-table compact">
            <table>
              <thead><tr><th>ID</th><th>Name</th><th>City</th><th>Expected</th><th>Assets</th></tr></thead>
              <tbody>
                {profiles.map((profile) => (
                  <tr key={profile.id} onClick={() => openProfile(profile.id)}>
                    <td>{profile.id}</td>
                    <td><strong>{profile.fullName}</strong><small>{profile.cnicMasked}</small></td>
                    <td>{profile.city}</td>
                    <td><span className={`risk-pill ${riskClass(profile.expectedRiskBand)}`}>{profile.expectedRiskBand}</span></td>
                    <td>{profile.vehicleCount} V - {profile.propertyCount} P - {profile.businessCount} B</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>
        <Panel title="Provider Record Preview" icon={Database}>
          {selectedProfile ? <JsonPreview value={selectedProfile} /> : <EmptyState title="Select a profile" />}
        </Panel>
      </div>
    </div>
  );
}

function DatasetFeedCenter({ datasetHub, datasetTemplates, feedDataset }) {
  const firstType = datasetTemplates?.[0]?.datasetType || "identity";
  const [datasetType, setDatasetType] = useState(firstType);
  const [format, setFormat] = useState("csv");
  const [fileName, setFileName] = useState("identity-seed.csv");
  const [content, setContent] = useState("");
  const [runPipeline, setRunPipeline] = useState(true);

  useEffect(() => {
    if (!datasetTemplates?.length) return;
    const current = datasetTemplates.find((template) => template.datasetType === datasetType) || datasetTemplates[0];
    setDatasetType(current.datasetType);
    setFileName(`${current.datasetType}-feed.${format}`);
    if (!content) setContent(current.csvExample);
  }, [datasetTemplates]);

  const selectedTemplate = datasetTemplates?.find((template) => template.datasetType === datasetType);

  function loadExample(nextType = datasetType, nextFormat = format) {
    const template = datasetTemplates?.find((item) => item.datasetType === nextType);
    if (!template) return;
    setDatasetType(nextType);
    setFormat(nextFormat);
    setFileName(`${nextType}-feed.${nextFormat}`);
    if (nextFormat === "json") {
      const [headerLine, ...rows] = template.csvExample.split("\n");
      const headers = headerLine.split(",");
      const records = rows.filter(Boolean).map((row) => {
        const values = row.split(",");
        return Object.fromEntries(headers.map((header, index) => [header, values[index] || ""]));
      });
      setContent(JSON.stringify({ records }, null, 2));
      return;
    }
    setContent(template.csvExample);
  }

  function handleFile(event) {
    const file = event.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      setContent(String(reader.result || ""));
      setFileName(file.name);
      setFormat(file.name.toLowerCase().endsWith(".json") ? "json" : "csv");
    };
    reader.readAsText(file);
  }

  async function submit() {
    await feedDataset({ datasetType, format, fileName, content, runPipeline });
  }

  return (
    <div className="feed-layout">
      <Panel title="Dataset Feed Console" subtitle="Upload CSV or JSON into the sandbox adapter, then score it through the same risk pipeline." icon={Upload}>
        <div className="feed-form">
          <div className="feed-controls">
            <label>Dataset<select value={datasetType} onChange={(e) => loadExample(e.target.value, format)}>
              {datasetTemplates?.map((template) => <option key={template.datasetType} value={template.datasetType}>{template.datasetType}</option>)}
            </select></label>
            <label>Format<select value={format} onChange={(e) => loadExample(datasetType, e.target.value)}>
              <option value="csv">CSV</option>
              <option value="json">JSON</option>
            </select></label>
            <label>File name<input value={fileName} onChange={(e) => setFileName(e.target.value)} /></label>
            <label className="checkbox-line"><input type="checkbox" checked={runPipeline} onChange={(e) => setRunPipeline(e.target.checked)} /> Run risk pipeline</label>
          </div>
          <div className="upload-strip">
            <input type="file" accept=".csv,.json,text/csv,application/json" onChange={handleFile} />
            <button onClick={() => loadExample()}><FileText size={15} /> Load template</button>
            <button onClick={submit}><Upload size={15} /> Feed dataset</button>
          </div>
          <textarea className="textarea-large" value={content} onChange={(e) => setContent(e.target.value)} spellCheck="false" />
        </div>
      </Panel>
      <Panel title="Feed Health" subtitle={`${datasetHub?.totals?.records || 0} records received across ${datasetHub?.totals?.batches || 0} batches.`} icon={ClipboardCheck}>
        {selectedTemplate && (
          <div className="template-card">
            <strong>{selectedTemplate.description}</strong>
            <p>{selectedTemplate.columns.join(", ")}</p>
          </div>
        )}
        <div className="job-list">
          {(datasetHub?.jobs || []).slice(0, 6).map((job) => (
            <article key={job.id} className="job-row">
              <span className={`risk-pill ${job.status === "Failed" ? "critical" : job.status === "SucceededWithWarnings" ? "medium" : "low"}`}>{job.status}</span>
              <div><strong>{job.source}</strong><small>{job.recordsCreated}/{job.recordsProcessed} applied</small></div>
            </article>
          ))}
          {(!datasetHub?.jobs || datasetHub.jobs.length === 0) && <EmptyState title="No dataset feeds yet" />}
        </div>
      </Panel>
    </div>
  );
}

function Citizen({ citizen, submitCorrection }) {
  if (!citizen) return <EmptyState title="Loading citizen portal" />;
  return (
    <div className="citizen-layout">
      <Panel title="My Compliance Summary" subtitle="Citizen-safe view with correction path." icon={ShieldCheck}>
        <div className="citizen-card">
          <div className="avatar large"><UserCircle size={40} /></div>
          <div>
            <h3>{citizen.person.fullName}</h3>
            <p>{citizen.person.cnicMasked} - {citizen.person.city}</p>
            <span className={`risk-pill ${riskClass(citizen.riskBand)}`}>{citizen.riskBand}</span>
          </div>
        </div>
        <div className="explain-box">
          <strong>Safe explanation</strong>
          <p>{citizen.safeSummary}</p>
        </div>
      </Panel>
      <Panel title="Submit Correction" subtitle="Challenge stale or mismatched records before escalation." icon={Upload}>
        <div className="correction-form">
          <select>{citizen.correctionOptions.map((x) => <option key={x}>{x}</option>)}</select>
          <textarea defaultValue="The linked record may be outdated. Please verify current ownership and declaration status." />
          <button onClick={submitCorrection}><Upload size={16} /> Submit correction</button>
        </div>
      </Panel>
      <Panel title="Fairness Controls" icon={BadgeCheck}>
        <div className="control-grid">
          <FeedItem icon={FileText} title="Evidence backed" text="Every flag must map to structured evidence." />
          <FeedItem icon={Users} title="Human reviewed" text="AI prioritizes; auditors decide." />
          <FeedItem icon={CheckCircle2} title="Correctable" text="Citizens can submit corrections before escalation." />
        </div>
      </Panel>
    </div>
  );
}

function AssistantDrawer({ selectedCase, answer, query, setQuery, askAssistant }) {
  return (
    <aside className="assistant-drawer">
      <div className="drawer-head">
        <Bot size={21} />
        <div>
          <strong>AI Assistant</strong>
          <span>Evidence-grounded support</span>
        </div>
      </div>
      <div className="drawer-body">
        {selectedCase ? (
          <div className="assistant-message">
            <Sparkles size={16} />
            <p>I am analyzing {selectedCase.caseItem.id}. Ask for missing evidence, citizen-safe explanation, or report language.</p>
          </div>
        ) : (
          <div className="assistant-message"><p>Select a case to begin.</p></div>
        )}
        {answer && <AssistantAnswer answer={answer} />}
      </div>
      <div className="drawer-input">
        <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Ask AI about compliance trends..." />
        <button onClick={() => askAssistant(query)}><ArrowRight size={18} /></button>
      </div>
    </aside>
  );
}

function AssistantAnswer({ answer }) {
  return (
    <div className="assistant-answer">
      <strong>{answer.riskBand ? `${answer.riskBand} - ${answer.score}/100` : "Assistant response"}</strong>
      <p>{answer.answer}</p>
      {answer.evidenceIds?.length > 0 && <small>Evidence: {answer.evidenceIds.slice(0, 5).join(", ")}</small>}
      {answer.citations?.length > 0 && (
        <div className="citation-list">
          {answer.citations.slice(0, 3).map((c) => <span key={c.chunkId}>{c.title}</span>)}
        </div>
      )}
    </div>
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

createRoot(document.getElementById("root")).render(<App />);
