import React, { useMemo } from "react";
import {
  Activity,
  AlertTriangle,
  ArrowRight,
  BriefcaseBusiness,
  CircleDollarSign,
  ClipboardCheck,
  Database,
  Gauge,
  GitBranch,
  Globe2,
  PieChart,
  ShieldCheck
} from "lucide-react";
import { FeedItem, Metric, Panel } from "../components/common/Primitives.jsx";

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

export { Overview };
