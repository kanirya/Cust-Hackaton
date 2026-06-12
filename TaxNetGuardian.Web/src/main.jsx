import React, { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  BarChart3,
  BrainCircuit,
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
import { Settings } from "./pages/SettingsPage.jsx";
import { ModelTraining } from "./pages/TrainingPage.jsx";
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
  { id: "training", label: "Model Training", icon: BrainCircuit },
  { id: "backend", label: "Backend Systems", icon: Database },
  { id: "citizen", label: "Citizen Portal", icon: Users },
  { id: "system", label: "System Control", icon: Workflow }
];

// In-flight request tracking so the UI can show a global progress indicator and
// disable controls while any request is running (production-style feedback).
let _inflight = 0;
const _inflightListeners = new Set();
function notifyInflight() {
  for (const listener of _inflightListeners) listener(_inflight);
}

const api = async (path, options = {}, role = getRole()) => {
  _inflight += 1;
  notifyInflight();
  try {
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
    return await response.json();
  } finally {
    _inflight -= 1;
    notifyInflight();
  }
};

function getInitialPage() {
  const path = window.location.pathname.toLowerCase();
  if (path.includes("sandbox")) return "sandbox";
  if (path.includes("rag") || path.includes("policy")) return "rag";
  if (path.includes("training") || path.includes("model")) return "training";
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
  const [cnicInvestigation, setCnicInvestigation] = useState(null);
  const [cnicStreaming, setCnicStreaming] = useState(false);
  const [query, setQuery] = useState("Why was this case marked critical?");
  const [toast, setToast] = useState("");
  const [citizen, setCitizen] = useState(null);
  const [busy, setBusy] = useState(false);
  const [busyLabel, setBusyLabel] = useState("");
  const [assistantOpen, setAssistantOpen] = useState(true);
  const [conversation, setConversation] = useState([]);
  const [chatInput, setChatInput] = useState("");
  const [initializing, setInitializing] = useState(true);
  const [flags, setFlags] = useState({});
  const [flagItems, setFlagItems] = useState([]);
  const [cognito, setCognito] = useState(null);
  const [customModel, setCustomModel] = useState(null);
  const [trainingExamples, setTrainingExamples] = useState(null);
  const ff = (k) => flags[k] !== false;

  useEffect(() => {
    const listener = (count) => {
      setBusy(count > 0);
      if (count === 0) setBusyLabel("");
    };
    _inflightListeners.add(listener);
    return () => _inflightListeners.delete(listener);
  }, []);

  useEffect(() => {
    localStorage.setItem("taxnet.role", role);
  }, [role]);

  useEffect(() => {
    refreshAll().finally(() => setInitializing(false));
  }, []);

  useEffect(() => {
    if (selectedCaseId) {
      loadCase(selectedCaseId);
      loadChatHistory(selectedCaseId);
    }
  }, [selectedCaseId]);

  const selectedEntityId = selectedCase?.caseItem?.entityId || cases[0]?.entityId;

  async function refreshAll() {
    const [summaryData, caseData, providerData, workerData, ragData, datasetData, templateData, authData, modelData, infraData, auditData, objectData, notificationData, persistenceData, citizenData, flagData, cognitoData, customModelData, trainingExampleData] = await Promise.all([
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
      api("/api/citizen/me", {}, "taxnet-citizen"),
      api("/api/feature-flags").catch(() => ({ items: [] })),
      api("/api/auth/cognito/status").catch(() => null),
      api("/api/system/custom-model", {}, "taxnet-admin").catch(() => null),
      api("/api/system/custom-model/examples?limit=25", {}, "taxnet-admin").catch(() => ({ items: [] }))
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
    const flagList = flagData?.items || [];
    setFlagItems(flagList);
    setFlags(Object.fromEntries(flagList.map((f) => [f.key, f.enabled])));
    setCognito(cognitoData);
    setCustomModel(customModelData);
    setTrainingExamples(trainingExampleData);
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

  async function openProfile(id) {
    try {
      const detail = await api(`/api/sandbox/profiles/${id}`);
      setSelectedProfile(detail);
    } catch (error) {
      setToast(`Could not load profile ${id}: ${String(error.message || error).slice(0, 140)}`);
    }
  }

  async function setFeatureFlag(key, enabled) {
    try {
      await api(`/api/system/feature-flags/${encodeURIComponent(key)}`, { method: "PUT", body: JSON.stringify({ enabled }) }, "taxnet-admin");
      setFlags((prev) => ({ ...prev, [key]: enabled }));
      setFlagItems((prev) => prev.map((f) => (f.key === key ? { ...f, enabled } : f)));
      setToast(`${key} ${enabled ? "enabled" : "disabled"}.`);
    } catch (err) {
      setToast(`Could not update flag: ${String(err.message || err).slice(0, 140)}`);
    }
  }

  async function refreshTraining() {
    try {
      const [status, ex] = await Promise.all([
        api("/api/system/custom-model", {}, "taxnet-admin"),
        api("/api/system/custom-model/examples?limit=25", {}, "taxnet-admin")
      ]);
      setCustomModel(status);
      setTrainingExamples(ex);
    } catch {
      /* non-fatal */
    }
  }

  async function trainModel() {
    setBusyLabel("Training the custom TaxNet model…");
    try {
      const run = await api("/api/system/custom-model/train", { method: "POST", body: "{}" }, "taxnet-admin");
      setToast(
        run.status === "Succeeded"
          ? `Trained custom model v${run.version}: ${Math.round((run.metrics?.validationSimilarity || 0) * 100)}% validation accuracy on ${run.totalExamples} examples.`
          : `Training did not complete: ${run.notes || "see runs"}.`
      );
      await refreshTraining();
    } catch (err) {
      setToast(`Training failed: ${String(err.message || err).slice(0, 160)}`);
    }
  }

  async function setInferenceMode(mode) {
    try {
      const result = await api("/api/system/custom-model/mode", { method: "PUT", body: JSON.stringify({ mode }) }, "taxnet-admin");
      setToast(`Inference routed via ${result.mode === "BigLlm" ? "Frontier LLM" : result.mode === "CustomModel" ? "Custom Model" : "Hybrid (Auto)"}.`);
      await refreshTraining();
    } catch (err) {
      setToast(`Could not switch model: ${String(err.message || err).slice(0, 140)}`);
    }
  }

  async function testCustomModel(taskType, prompt) {
    return api("/api/system/custom-model/test", { method: "POST", body: JSON.stringify({ taskType, prompt }) }, "taxnet-admin");
  }

  async function exportTrainingData(format = "chat") {
    try {
      const resp = await fetch(`/api/system/custom-model/export?format=${encodeURIComponent(format)}`, {
        headers: { "X-Demo-Role": "taxnet-admin", "X-Demo-User": "taxnet-demo-operator" }
      });
      if (!resp.ok) throw new Error(await resp.text());
      const blob = await resp.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `taxnet-training-${format}.jsonl`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
      setToast(`Exported training data (${format} JSONL).`);
    } catch (err) {
      setToast(`Export failed: ${String(err.message || err).slice(0, 140)}`);
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
      system: "/system",
      settings: "/settings",
      training: "/training"
    };
    const url = urls[id] || "/";
    window.history.pushState({}, "", url);
  }

  async function runPipeline() {
    setBusyLabel("Running ingestion + scoring pipeline…");
    const result = await api("/api/ingestion/run", { method: "POST", body: "{}" }, role);
    setToast(`${result.importedProfiles} profiles imported, ${result.cases} cases scored.`);
    await refreshAll();
  }

  async function loadChatHistory(caseId) {
    try {
      const h = await api(`/api/assistant/cases/${caseId}/history`);
      setConversation((h.items || []).map((m) => ({ role: m.role, text: m.text })));
    } catch {
      setConversation([]);
    }
  }

  // ChatGPT-style conversation: append the user message, stream the assistant reply into
  // a growing bubble, clear the input. History is persisted server-side per case.
  async function askAssistant(question = chatInput) {
    const q = (question ?? "").trim();
    if (!selectedCaseId) { setToast("Select a case first."); return; }
    if (!q) return;
    setAssistantOpen(true);
    setChatInput("");
    setConversation((prev) => [...prev, { role: "user", text: q }, { role: "assistant", text: "", streaming: true }]);

    const updateLast = (patch) =>
      setConversation((prev) => {
        const c = [...prev];
        for (let i = c.length - 1; i >= 0; i--) {
          if (c[i].role === "assistant") { c[i] = { ...c[i], ...patch }; break; }
        }
        return c;
      });

    let acc = "";
    try {
      const resp = await fetch(`/api/assistant/cases/${selectedCaseId}/ask/stream`, {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-Demo-Role": role, "X-Demo-User": "taxnet-demo-operator" },
        body: JSON.stringify({ question: q })
      });
      if (!resp.ok || !resp.body) throw new Error(await resp.text());
      const reader = resp.body.getReader();
      const decoder = new TextDecoder();
      let buffer = "";
      for (;;) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        let idx;
        while ((idx = buffer.indexOf("\n\n")) >= 0) {
          const block = buffer.slice(0, idx);
          buffer = buffer.slice(idx + 2);
          const ev = (/^event:\s*(.*)$/m.exec(block) || [])[1]?.trim();
          const dataLine = (/^data:\s*([\s\S]*)$/m.exec(block) || [])[1];
          if (!dataLine) continue;
          let data;
          try { data = JSON.parse(dataLine); } catch { continue; }
          if (ev === "error") { updateLast({ text: `⚠ ${data.message}`, streaming: false }); return; }
          else if (ev === "delta") { acc += data.text; updateLast({ text: acc, streaming: true }); }
          else if (ev === "done") { updateLast({ text: data.answer, streaming: false }); }
        }
      }
    } catch (err) {
      updateLast({ text: `⚠ ${String(err.message || err).slice(0, 160)}`, streaming: false });
    }
  }

  // Deep, evidence-grounded explanation via the AI orchestrator: builds the
  // case explanation, retrieves RAG policy context, invokes the model gateway,
  // and returns structured validation checks. This is the "everything together"
  // path (case -> evidence -> RAG -> model -> guardrails).
  async function explainCase() {
    if (!selectedCaseId) {
      setToast("Select a case first.");
      return;
    }
    setToast("Running grounded orchestrator explanation...");
    setBusyLabel("Generating evidence-grounded explanation with AI…");
    try {
      const result = await api(
        `/api/orchestrator/cases/${selectedCaseId}/explain?allowExternalProvider=true&preferredProvider=auto`,
        { method: "POST", body: "{}" },
        "taxnet-auditor"
      );
      const inv = result.modelInvocation || {};
      const explanation = result.explanation || {};
      const ragChunks = result.ragContext?.chunks?.length || 0;
      const usedExternal = inv.usedExternalProvider && inv.output;
      setAssistantAnswer({
        answer: usedExternal
          ? inv.output
          : `${explanation.summary || ""} Key reasons: ${(explanation.keyReasons || []).slice(0, 3).join(" ")}`.trim(),
        evidenceIds: explanation.evidenceIds || [],
        citations: result.ragContext?.citations?.length ? result.ragContext.citations : (explanation.citations || []),
        warnings: [...(result.validation || []), explanation.humanReviewWarning].filter(Boolean),
        score: result.score?.score,
        riskBand: result.score?.riskBand
      });
      setToast(`Orchestrator explanation ready (${inv.selectedProvider || "template"}, ${ragChunks} RAG chunks).`);
    } catch (error) {
      setToast(`Orchestrator explanation failed: ${String(error.message || error).slice(0, 160)}`);
    }
  }

  // Streaming CNIC investigation over Server-Sent Events: shows the AI narrative as it is
  // produced, then renders the final formatted result. Falls back to a word-by-word stream
  // of the deterministic narrative when no external model is configured.
  async function investigateCnicStream(cnic) {
    const term = (cnic || "").trim();
    if (!term) {
      setToast("Enter a CNIC to investigate.");
      return;
    }
    setCnicStreaming(true);
    setBusyLabel(`Investigating ${term} with AI…`);
    setCnicInvestigation(null);
    setToast(`Investigating ${term}...`);
    let acc = "";
    let meta = null;
    try {
      const resp = await fetch("/api/investigations/cnic/stream", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-Demo-Role": "taxnet-auditor", "X-Demo-User": "taxnet-demo-operator" },
        body: JSON.stringify({ cnic: term, preferredProvider: "claude", allowExternalProvider: true })
      });
      if (!resp.ok || !resp.body) throw new Error(await resp.text());
      const reader = resp.body.getReader();
      const decoder = new TextDecoder();
      let buffer = "";
      for (;;) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        let idx;
        while ((idx = buffer.indexOf("\n\n")) >= 0) {
          const block = buffer.slice(0, idx);
          buffer = buffer.slice(idx + 2);
          const ev = (/^event:\s*(.*)$/m.exec(block) || [])[1]?.trim();
          const dataLine = (/^data:\s*([\s\S]*)$/m.exec(block) || [])[1];
          if (!dataLine) continue;
          let data;
          try { data = JSON.parse(dataLine); } catch { continue; }
          if (ev === "error") {
            setToast(`Investigation failed: ${data.message}`);
            setCnicStreaming(false);
            return;
          }
          if (ev === "meta") {
            meta = data;
            setCnicInvestigation({
              status: "Analyzing…", cnicMasked: data.cnicMasked, subject: data.subject,
              caseContext: data.caseContext, matchedRecords: data.matchedRecords, signals: data.signals,
              aiNarrative: "", findings: [], recommendedActions: [], model: {}
            });
            if (data.caseContext?.id) setSelectedCaseId(data.caseContext.id);
          } else if (ev === "delta") {
            acc += data.text;
            setCnicInvestigation((prev) => (prev ? { ...prev, aiNarrative: acc } : prev));
          } else if (ev === "done") {
            setCnicInvestigation({
              status: data.status, cnicMasked: data.cnicMasked, subject: meta?.subject,
              caseContext: meta?.caseContext, matchedRecords: meta?.matchedRecords || [], signals: meta?.signals || [],
              aiNarrative: data.aiNarrative, findings: data.findings, recommendedActions: data.recommendedActions,
              model: data.model, humanReviewWarning: "CNIC-linked investigation is decision-support only. It does not prove fraud and requires authorized human review."
            });
            setToast(`CNIC investigation completed for ${data.cnicMasked}: ${(meta?.matchedRecords || []).length} linked records.`);
          }
        }
      }
    } catch (err) {
      setToast(`CNIC investigation failed: ${String(err.message || err).slice(0, 160)}`);
    } finally {
      setCnicStreaming(false);
    }
  }

  async function investigateCnic(cnic) {
    const term = (cnic || "").trim();
    if (!term) {
      setToast("Enter a CNIC to investigate.");
      return null;
    }
    setToast(`Investigating ${term}...`);
    setBusyLabel(`Investigating ${term} with AI…`);
    try {
      const result = await api("/api/investigations/cnic", {
        method: "POST",
        body: JSON.stringify({
          cnic: term,
          preferredProvider: "claude",
          allowExternalProvider: true
        })
      }, "taxnet-auditor");
      setCnicInvestigation(result);
      // If the CNIC resolves to an existing risk case, open it so the full
      // investigation workspace (profile, graph, evidence) reflects the
      // searched person alongside the CNIC-linked records and AI narrative.
      if (result.caseContext?.id) {
        setSelectedCaseId(result.caseContext.id);
      }
      setToast(`CNIC investigation completed for ${result.cnicMasked}: ${result.matchedRecords?.length || 0} linked records, ${result.signals?.length || 0} signals.`);
      return result;
    } catch (err) {
      setToast(`CNIC investigation failed: ${err.message}`);
      return null;
    }
  }

  // Global command-search: resolves the term through the high-accuracy backend search
  // (CNIC, NTN, profile ID, case ID, fuzzy name) and routes to the best match. Case-ID hits
  // open the case directly; person hits run the full aggregate + AI investigation on the
  // subject's exact CNIC so results are precise even for partial or name-based queries.
  async function handleGlobalSearch(term) {
    const q = (term || "").trim();
    if (!q) return;

    let result;
    try {
      result = await api(`/api/search?q=${encodeURIComponent(q)}&limit=8`, {}, "taxnet-auditor");
    } catch (err) {
      setToast(`Search failed: ${String(err.message || err).slice(0, 140)}`);
      return;
    }

    const hits = result?.items || [];
    if (hits.length === 0) {
      // Fall back to a raw CNIC investigation in case the term is an unseeded identity.
      navigate("investigation");
      await investigateCnicStream(q);
      return;
    }

    const top = hits[0];
    navigate("investigation");
    if (top.matchType?.startsWith("Case ID") && top.caseId) {
      setSelectedCaseId(top.caseId);
      setToast(`Opened ${top.caseId} — ${top.fullName} (${Math.round(top.confidence * 100)}% match).`);
      return;
    }
    setToast(`Best match: ${top.fullName} · ${top.matchType} (${Math.round(top.confidence * 100)}%). Investigating…`);
    await investigateCnicStream(top.cnicMasked || q);
  }

  function startNewInvestigation() {
    setCnicInvestigation(null);
    navigate("investigation");
    setToast("New investigation: enter a CNIC in the CNIC Investigation panel to begin.");
  }

  // Live ranked suggestions for the global search box. Uses a raw fetch so it does not
  // trigger the global busy indicator on every keystroke.
  async function fetchSuggestions(term) {
    const q = (term || "").trim();
    if (q.length < 2) return [];
    try {
      const resp = await fetch(`/api/search?q=${encodeURIComponent(q)}&limit=6`, {
        headers: { "X-Demo-Role": "taxnet-auditor", "X-Demo-User": "taxnet-demo-operator" }
      });
      if (!resp.ok) return [];
      const data = await resp.json();
      return data.items || [];
    } catch {
      return [];
    }
  }

  // Route a chosen search hit: case-ID hits open the case; person hits run the full
  // aggregate + AI investigation on the subject's exact CNIC.
  async function selectSearchHit(hit) {
    if (!hit) return;
    navigate("investigation");
    if (hit.matchType?.startsWith("Case ID") && hit.caseId) {
      setSelectedCaseId(hit.caseId);
      setToast(`Opened ${hit.caseId} — ${hit.fullName}.`);
      return;
    }
    setToast(`Investigating ${hit.fullName} (${hit.matchType})…`);
    await investigateCnicStream(hit.cnicMasked);
  }

  async function generateReport() {
    setBusyLabel("Generating audit report with AI…");
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

  // Form-based seeding: build one feed per filled section and submit them in order,
  // scoring on the final feed. A friendlier alternative to hand-writing CSV.
  async function quickSeedPerson(form) {
    const id = (form.personId || `UI${Date.now().toString().slice(-6)}`).trim();
    const cnic = (form.cnic || "").trim();
    if (!form.fullName || !cnic) {
      setToast("Full name and CNIC are required to seed a person.");
      return;
    }
    const city = form.city || "Karachi";
    const province = form.province || "Sindh";
    const feeds = [
      ["identity", `personId,fullName,urduName,fatherName,city,province,cnicMasked,phoneMasked\n${id},${form.fullName},${form.urduName || ""},${form.fatherName || ""},${city},${province},${cnic},${form.phone || "+92-3**-***00"}`]
    ];
    feeds.push(["tax", `personId,ntn,filerStatus,declaredAnnualIncome,taxPaid,taxYear\n${id},NTN-${id},${form.filerStatus || "Non-Filer"},${form.declaredIncome || 0},0,2025`]);
    if (form.vehicleMake) feeds.push(["vehicle", `personId,registrationNumberMasked,make,model,engineCc,modelYear,estimatedValue,province\n${id},REG-${id},${form.vehicleMake},${form.vehicleModel || "Vehicle"},${form.vehicleCc || 1800},2023,${form.vehicleValue || 0},${province}`]);
    if (form.propertyValue) feeds.push(["property", `personId,propertyToken,city,area,propertyType,estimatedValue\n${id},PLOT-${id},${city},${form.propertyArea || "City Center"},Residential,${form.propertyValue}`]);
    if (form.utilityBill) feeds.push(["utility", `personId,meterToken,utilityType,averageMonthlyBill,latestBillAmount,city\n${id},METER-${id},Electricity,${form.utilityBill},${form.utilityBill},${city}`]);
    if (form.businessName) feeds.push(["business", `personId,companyRegistrationNumber,companyName,relationshipType,status\n${id},SECP-${id},${form.businessName},Director,Active`]);
    if (form.travelSpend) feeds.push(["travel", `personId,destination,tripsInLast24Months,estimatedSpend\n${id},${form.travelDest || "International"},${form.travelTrips || 1},${form.travelSpend}`]);

    setBusyLabel(`Seeding ${form.fullName}…`);
    for (let i = 0; i < feeds.length; i++) {
      const [datasetType, content] = feeds[i];
      await api("/api/sandbox/datasets/feed", {
        method: "POST",
        body: JSON.stringify({ datasetType, format: "csv", fileName: `${datasetType}-${id}.csv`, content, runPipeline: i === feeds.length - 1 })
      }, "taxnet-sandbox-admin");
    }
    await refreshAll();
    setToast(`Seeded ${form.fullName} (${cnic}). Search the CNIC to investigate.`);
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

  async function saveModelKey(provider, payload) {
    const result = await api(`/api/system/model-gateway/providers/${provider}/key`, {
      method: "POST",
      body: JSON.stringify(payload)
    }, "taxnet-model-admin");
    setToast(`${result.provider} key stored in ${result.secretName}.`);
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
    system: "System Control Plane",
    settings: "Settings",
    training: "Model Training"
  }[page];

  return (
    <div className="app-shell">
      <div className={`top-progress ${busy ? "active" : ""}`} aria-hidden="true" />
      {initializing && (
        <div className="boot-splash" role="status" aria-live="polite">
          <div className="boot-card">
            <div className="boot-brand">
              <div className="boot-mark" />
              <div>
                <strong>TaxNet Guardian</strong>
                <span>Initializing intelligence platform…</span>
              </div>
            </div>
            <div className="boot-skeleton">
              <div className="sk sk-row" />
              <div className="sk sk-row short" />
              <div className="sk-grid">
                <div className="sk sk-card" /><div className="sk sk-card" />
                <div className="sk sk-card" /><div className="sk sk-card" />
              </div>
              <div className="sk sk-block" />
            </div>
          </div>
        </div>
      )}
      {busy && (
        <div className="busy-chip" role="status" aria-live="polite">
          <span className="busy-spinner" />
          <span>{busyLabel || "Working…"}</span>
        </div>
      )}
      <Sidebar page={page} navigate={navigate} navItems={navItems.filter((n) => (n.id !== "citizen" || ff("citizenPortal")) && (n.id !== "training" || ff("customModel")))} onNewInvestigation={startNewInvestigation} onOpenSettings={() => navigate("settings")} />
      <main className="workspace">
        <TopBar
          title={pageTitle}
          role={role}
          roles={roles}
          setRole={setRole}
          onRefresh={refreshAll}
          onPipeline={runPipeline}
          onSearch={handleGlobalSearch}
          fetchSuggestions={fetchSuggestions}
          onSelectHit={selectSearchHit}
          assistantOpen={assistantOpen && ff("aiAssistant")}
          showAssistantToggle={ff("aiAssistant")}
          onToggleAssistant={() => setAssistantOpen((v) => !v)}
        />
        {toast && <button className="toast" onClick={() => setToast("")}>{toast}</button>}
        <div className={`workspace-grid ${(assistantOpen && ff("aiAssistant")) ? "" : "assistant-collapsed"}`}>
          <section className="content-area">
            {page === "overview" && (
              <Overview
                summary={summary}
                cases={cases}
                providers={providers}
                rag={rag}
                setPage={navigate}
                setSelectedCaseId={setSelectedCaseId}
                ff={ff}
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
                investigateCnic={investigateCnicStream}
                cnicInvestigation={cnicInvestigation}
                explainCase={explainCase}
                busy={busy || cnicStreaming}
                ff={ff}
              />
            )}
            {page === "sandbox" && (
              <Sandbox
                providers={providers}
                profiles={profiles}
                selectedProfile={selectedProfile}
                setSelectedProfile={setSelectedProfile}
                openProfile={openProfile}
                generateSandbox={generateSandbox}
                datasetHub={datasetHub}
                datasetTemplates={datasetTemplates}
                feedDataset={feedDataset}
                updateProvider={updateProvider}
                quickSeedPerson={quickSeedPerson}
                ff={ff}
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
                saveModelKey={saveModelKey}
                modelResult={modelResult}
                selectedCaseId={selectedCaseId}
                ragAnimation={ff("ragAnimation")}
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
            {page === "settings" && (
              <Settings flagItems={flagItems} authz={authz} cognito={cognito} setFeatureFlag={setFeatureFlag} />
            )}
            {page === "training" && (
              <ModelTraining
                customModel={customModel}
                examples={trainingExamples}
                trainModel={trainModel}
                setInferenceMode={setInferenceMode}
                testCustomModel={testCustomModel}
                exportTrainingData={exportTrainingData}
                refreshTraining={refreshTraining}
              />
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
                saveModelKey={saveModelKey}
                modelResult={modelResult}
                selectedCaseId={selectedCaseId}
                ragAnimation={ff("ragAnimation")}
              />
            )}
          </section>
          {assistantOpen && ff("aiAssistant") && (
            <AssistantDrawer
              selectedCase={selectedCase}
              conversation={conversation}
              input={chatInput}
              setInput={setChatInput}
              askAssistant={askAssistant}
              onClose={() => setAssistantOpen(false)}
            />
          )}
        </div>
      </main>
    </div>
  );
}

createRoot(document.getElementById("root")).render(<App />);
