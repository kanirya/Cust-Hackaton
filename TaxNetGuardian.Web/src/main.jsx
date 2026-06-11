import React, { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  BarChart3,
  BriefcaseBusiness,
  Database,
  Layers3,
  Network,
  TerminalSquare,
  Users,
  Workflow
} from "lucide-react";
import "./styles.css";
import { BackendSystems, RagWorkspace, System } from "./components/system/SystemViews.jsx";
import { Sidebar, TopBar } from "./components/shell/AppShell.jsx";
import { Overview } from "./pages/OverviewPage.jsx";
import { CaseQueue } from "./pages/CaseQueuePage.jsx";
import { Investigation } from "./pages/InvestigationPage.jsx";
import { Sandbox } from "./pages/SandboxPage.jsx";
import { Citizen } from "./pages/CitizenPage.jsx";
import { AssistantDrawer } from "./components/assistant/AssistantDrawer.jsx";

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

createRoot(document.getElementById("root")).render(<App />);
