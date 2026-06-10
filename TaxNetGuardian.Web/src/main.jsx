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
  if (path.includes("citizen")) return "citizen";
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
  const [authz, setAuthz] = useState(null);
  const [modelGateway, setModelGateway] = useState(null);
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
    const [summaryData, caseData, providerData, workerData, ragData, authData, modelData, citizenData] = await Promise.all([
      api("/api/dashboard/summary"),
      api("/api/cases"),
      api("/api/sandbox/providers"),
      api("/api/system/workers"),
      api("/api/system/rag"),
      api("/api/authz"),
      api("/api/system/model-gateway"),
      api("/api/citizen/me", {}, "taxnet-citizen")
    ]);
    setSummary(summaryData);
    setCases(caseData.items);
    setProviders(providerData.value || providerData);
    setWorkers(workerData);
    setRag(ragData);
    setAuthz(authData);
    setModelGateway(modelData);
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
    const url = id === "sandbox" ? "/sandbox" : id === "citizen" ? "/citizen" : "/";
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
      answer: `Report ${result.reportId} generated. ${result.caseSummary}`,
      evidenceIds: result.evidence.map((x) => x.id),
      citations: result.citations,
      warnings: [result.disclaimer],
      score: result.score.score,
      riskBand: result.score.riskBand
    });
  }

  async function generateSandbox() {
    const result = await api("/api/sandbox/admin/generate", {
      method: "POST",
      body: JSON.stringify({ count: 180, suspiciousPercent: 28, noisePercent: 24 })
    }, "taxnet-sandbox-admin");
    setToast(`${result.profiles} sandbox profiles generated; ${result.cases} cases flagged.`);
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
    citizen: "Citizen Correction Portal",
    system: "System Control Plane"
  }[page];

  return (
    <div className="app-shell">
      <Sidebar page={page} navigate={navigate} />
      <main className="workspace">
        <TopBar
          title={pageTitle}
          role={role}
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
              />
            )}
            {page === "citizen" && (
              <Citizen citizen={citizen} submitCorrection={submitCorrection} />
            )}
            {page === "system" && (
              <System workers={workers} rag={rag} authz={authz} modelGateway={modelGateway} />
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

function Sidebar({ page, navigate }) {
  return (
    <aside className="sidebar">
      <div className="brand">
        <div className="brand-mark"><Shield size={22} /></div>
        <div>
          <h1>TaxNet Guardian</h1>
          <p>Intelligent compliance</p>
        </div>
      </div>
      <nav className="nav">
        {navItems.map((item) => {
          const Icon = item.icon;
          return (
            <button key={item.id} className={page === item.id ? "active" : ""} onClick={() => navigate(item.id)}>
              <Icon size={18} />
              <span>{item.label}</span>
            </button>
          );
        })}
      </nav>
      <button className="new-investigation"><Zap size={18} /> New Investigation</button>
      <div className="user-card">
        <div className="avatar"><UserCircle size={26} /></div>
        <div>
          <strong>Tax Auditor</strong>
          <span>Region North</span>
        </div>
      </div>
      <div className="sidebar-footer">
        <button><Settings size={17} /> Settings</button>
        <button><LifeBuoy size={17} /> Support</button>
      </div>
    </aside>
  );
}

function TopBar({ title, role, setRole, onRefresh, onPipeline }) {
  return (
    <header className="topbar">
      <div>
        <p className="breadcrumb">Overview / {title}</p>
        <h2>{title}</h2>
      </div>
      <div className="command-search">
        <Search size={18} />
        <input placeholder="Search by NTN, CNIC, Case ID, provider..." />
        <kbd>⌘K</kbd>
      </div>
      <div className="top-actions">
        <select value={role} onChange={(e) => setRole(e.target.value)} aria-label="Role">
          {roles.map((r) => <option key={r}>{r}</option>)}
        </select>
        <button onClick={onPipeline}><RefreshCw size={16} /> Pipeline</button>
        <button onClick={onRefresh}><Bell size={16} /></button>
      </div>
    </header>
  );
}

function Overview({ summary, cases, providers, rag, setPage, setSelectedCaseId }) {
  const riskDistribution = useMemo(() => {
    const total = Math.max(1, cases.length);
    return [
      ["Critical", cases.filter((x) => x.riskBand === "Critical").length],
      ["High", cases.filter((x) => x.riskBand === "High").length],
      ["Medium", cases.filter((x) => x.riskBand === "Medium").length],
      ["Low", Math.max(0, (summary?.totalProfiles || 0) - cases.length)]
    ].map(([label, count]) => ({ label, count, pct: Math.round((count / total) * 100) }));
  }, [cases, summary]);

  return (
    <div className="page-stack">
      <div className="hero-strip">
        <div>
          <p className="eyebrow">Government-grade security meets modern intelligence</p>
          <h3>Consolidated compliance intelligence across domestic economic regions.</h3>
        </div>
        <button onClick={() => setPage("queue")}>Open case queue <ArrowRight size={16} /></button>
      </div>
      <div className="metric-grid">
        <Metric icon={CircleDollarSign} label="Revenue opportunity" value={compact(summary?.estimatedRecoverableTax)} trend="+12.4%" />
        <Metric icon={AlertTriangle} label="Critical cases" value={summary?.criticalCases ?? "..."} trend="+5.2%" risk="critical" />
        <Metric icon={Gauge} label="ER precision" value={pct(summary?.entityResolutionPrecision)} trend="Optimized" risk="low" />
        <Metric icon={ClipboardCheck} label="False positive target" value="3.8%" trend="Steady" risk="medium" />
      </div>
      <div className="overview-layout">
        <Panel title="Regional Risk Map" subtitle="Synthetic clusters by city and signal strength." icon={Globe2}>
          <RiskMap summary={summary} />
        </Panel>
        <div className="stack">
          <Panel title="Risk Distribution" icon={PieChart}>
            {riskDistribution.map((item) => (
              <div className="dist-row" key={item.label}>
                <div><span>{item.label}</span><strong>{item.count}</strong></div>
                <div className={`bar ${riskClass(item.label)}`}><i style={{ width: `${Math.min(100, item.pct)}%` }} /></div>
              </div>
            ))}
          </Panel>
          <Panel title="Intelligence Feed" icon={Activity}>
            <FeedItem icon={GitBranch} title="Complex cluster found" text="Circular trading pattern detected between linked businesses and high-utility profiles." />
            <FeedItem icon={ShieldCheck} title="Policy update applied" text={`${rag?.documents?.length || 0} RAG policy documents available for citations.`} />
            <FeedItem icon={Database} title="Provider health" text={`${providers.filter((x) => x.status === "Healthy").length}/${providers.length} sandbox providers healthy.`} />
          </Panel>
        </div>
      </div>
      <Panel title="Priority Cases" subtitle="Fast path into investigations." icon={BriefcaseBusiness}>
        <div className="case-card-grid">
          {cases.slice(0, 4).map((c) => (
            <button key={c.id} className="case-card" onClick={() => { setSelectedCaseId(c.id); setPage("investigation"); }}>
              <span className={`risk-pill ${riskClass(c.riskBand)}`}>{c.riskBand}</span>
              <strong>{c.id}</strong>
              <p>{c.city}, {c.province}</p>
              <div className="score-line"><span>{c.score}/100</span><i style={{ width: `${c.score}%` }} /></div>
            </button>
          ))}
        </div>
      </Panel>
    </div>
  );
}

function CaseQueue({ cases, selectedCaseId, setSelectedCaseId, setPage }) {
  const [risk, setRisk] = useState("All");
  const [city, setCity] = useState("All");
  const [minScore, setMinScore] = useState(30);
  const cities = ["All", ...Array.from(new Set(cases.map((x) => x.city))).sort()];
  const filtered = cases.filter((c) =>
    (risk === "All" || c.riskBand === risk) &&
    (city === "All" || c.city === city) &&
    c.score >= minScore
  );

  return (
    <div className="page-stack">
      <Panel title="Worklist filters" subtitle="Compact chips for rapid drill-down." icon={Filter}>
        <div className="filter-grid">
          <label>Risk band<select value={risk} onChange={(e) => setRisk(e.target.value)}><option>All</option><option>Critical</option><option>High</option><option>Medium</option></select></label>
          <label>Geographic hub<select value={city} onChange={(e) => setCity(e.target.value)}>{cities.map((x) => <option key={x}>{x}</option>)}</select></label>
          <label>Score range ({minScore}-100)<input type="range" min="0" max="100" value={minScore} onChange={(e) => setMinScore(Number(e.target.value))} /></label>
        </div>
      </Panel>
      <Panel title="Active investigations" subtitle={`${filtered.length} cases visible with current filters.`} icon={BriefcaseBusiness}>
        <div className="data-table">
          <table>
            <thead><tr><th>Case</th><th>Location</th><th>Risk</th><th>Score</th><th>Signals</th><th>Action</th></tr></thead>
            <tbody>
              {filtered.map((c) => (
                <tr key={c.id} className={selectedCaseId === c.id ? "selected" : ""}>
                  <td><strong>{c.id}</strong><small>{c.status}</small></td>
                  <td>{c.city}<small>{c.province}</small></td>
                  <td><span className={`risk-pill ${riskClass(c.riskBand)}`}>{c.riskBand}</span></td>
                  <td><div className="score-cell"><strong>{c.score}</strong><div className="mini-bar"><i style={{ width: `${c.score}%` }} /></div></div></td>
                  <td>{c.topReasons?.slice(0, 3).map((x) => <span className="signal" key={x}>{x}</span>)}</td>
                  <td><button onClick={() => { setSelectedCaseId(c.id); setPage("investigation"); }}>Investigate</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Panel>
    </div>
  );
}

function Investigation({ selectedCase, graph, query, setQuery, askAssistant, generateReport, assistantAnswer }) {
  if (!selectedCase) return <EmptyState title="Loading investigation" />;
  const c = selectedCase.caseItem;
  const p = selectedCase.person;
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
                <footer>{ev.source} · {ev.id}</footer>
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
      </section>
    </div>
  );
}

function Sandbox({ providers, profiles, selectedProfile, setSelectedProfile, generateSandbox }) {
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
                    <td>{profile.vehicleCount} V · {profile.propertyCount} P · {profile.businessCount} B</td>
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

function Citizen({ citizen, submitCorrection }) {
  if (!citizen) return <EmptyState title="Loading citizen portal" />;
  return (
    <div className="citizen-layout">
      <Panel title="My Compliance Summary" subtitle="Citizen-safe view with correction path." icon={ShieldCheck}>
        <div className="citizen-card">
          <div className="avatar large"><UserCircle size={40} /></div>
          <div>
            <h3>{citizen.person.fullName}</h3>
            <p>{citizen.person.cnicMasked} · {citizen.person.city}</p>
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

function System({ workers, rag, authz, modelGateway }) {
  return (
    <div className="page-stack">
      <div className="system-grid">
        <Panel title="Worker Pipeline" subtitle="SQS-style queues, retries, and DLQs." icon={Workflow}>
          <div className="cards-grid">
            {workers?.workers?.map((w) => <WorkerCard worker={w} key={w.name} />)}
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
        <Panel title="RAG Policy Memory" icon={Layers3}>
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
      <strong>{answer.riskBand ? `${answer.riskBand} · ${answer.score}/100` : "Assistant response"}</strong>
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

function Metric({ icon: Icon, label, value, trend, risk = "low" }) {
  return (
    <article className="metric-card">
      <div className="metric-icon"><Icon size={20} /></div>
      <span>{label}</span>
      <strong>{value}</strong>
      <em className={risk}>{trend}</em>
    </article>
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

function RiskMap({ summary }) {
  const entries = Object.entries(summary?.casesByCity || {});
  return (
    <div className="risk-map">
      <div className="pak-map">
        {entries.slice(0, 7).map(([city, count], index) => (
          <button
            key={city}
            className={`map-node node-${index} ${count > 5 ? "critical" : count > 3 ? "high" : "low"}`}
            title={`${city}: ${count} cases`}
          >
            <span>{city}</span>
          </button>
        ))}
      </div>
      <div className="map-callout">
        <strong>Sindh Region Intelligence</strong>
        <p>Active audits: {summary?.casesByCity?.Karachi || 0}</p>
        <span>Cluster identified in commercial import profiles.</span>
      </div>
    </div>
  );
}

function FeedItem({ icon: Icon, title, text }) {
  return (
    <article className="feed-item">
      <Icon size={18} />
      <div><strong>{title}</strong><p>{text}</p></div>
    </article>
  );
}

function GraphCanvas({ graph }) {
  const nodes = graph?.nodes || [];
  const edges = graph?.edges || [];
  const positions = useMemo(() => layoutNodes(nodes), [nodes]);
  const byId = new Map(positions.map((n) => [n.id, n]));
  return (
    <svg className="graph-canvas" viewBox="0 0 900 560" role="img" aria-label="Knowledge graph">
      <defs>
        <marker id="arrow" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto">
          <path d="M0,0 L0,6 L9,3 z" fill="#94a3b8" />
        </marker>
      </defs>
      {edges.map((edge) => {
        const a = byId.get(edge.source);
        const b = byId.get(edge.target);
        if (!a || !b) return null;
        return (
          <g key={edge.id}>
            <line x1={a.x} y1={a.y} x2={b.x} y2={b.y} className="graph-edge" markerEnd="url(#arrow)" />
            <text x={(a.x + b.x) / 2} y={(a.y + b.y) / 2 - 8} textAnchor="middle">{edge.type.replaceAll("_", " ")}</text>
          </g>
        );
      })}
      {positions.map((node) => (
        <g key={node.id}>
          <circle cx={node.x} cy={node.y} r={node.type === "Person" ? 34 : 25} className={`graph-node ${node.type.toLowerCase()}`} />
          <text x={node.x} y={node.y + 4} textAnchor="middle" className="node-code">{node.type.slice(0, 2).toUpperCase()}</text>
          <text x={node.x} y={node.y + 50} textAnchor="middle" className="node-label">{node.label}</text>
        </g>
      ))}
    </svg>
  );
}

function layoutNodes(nodes) {
  const center = { x: 450, y: 280 };
  const radius = 190;
  return nodes.map((node, index) => {
    if (index === 0) return { ...node, ...center };
    const angle = (Math.PI * 2 * (index - 1)) / Math.max(1, nodes.length - 1) - Math.PI / 2;
    return { ...node, x: center.x + Math.cos(angle) * radius, y: center.y + Math.sin(angle) * radius };
  });
}

function RadialScore({ value, band }) {
  const circumference = 2 * Math.PI * 42;
  const offset = circumference - (circumference * value) / 100;
  return (
    <div className="radial">
      <svg viewBox="0 0 104 104">
        <circle cx="52" cy="52" r="42" />
        <circle cx="52" cy="52" r="42" style={{ strokeDasharray: circumference, strokeDashoffset: offset }} className={riskClass(band)} />
      </svg>
      <strong>{value}</strong>
    </div>
  );
}

function InfoRows({ rows }) {
  return <div className="info-rows">{rows.map(([k, v]) => <div key={k}><span>{k}</span><strong>{v}</strong></div>)}</div>;
}

function JsonPreview({ value }) {
  return <pre className="json-preview">{JSON.stringify(value, null, 2)}</pre>;
}

function WorkerCard({ worker }) {
  return (
    <article className="small-card">
      <strong>{worker.name}</strong>
      <p>{worker.queueName}</p>
      <small>{worker.status} · depth {worker.queueDepth} · {worker.processedToday} processed</small>
    </article>
  );
}

function EmptyState({ title }) {
  return <div className="empty-state"><Command size={24} /><strong>{title}</strong></div>;
}

createRoot(document.getElementById("root")).render(<App />);
