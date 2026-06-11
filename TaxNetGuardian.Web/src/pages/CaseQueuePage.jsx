import React, { useState } from "react";
import { BriefcaseBusiness, Filter } from "lucide-react";
import { Panel } from "../components/common/Primitives.jsx";

function riskClass(value) {
  return String(value || "low").toLowerCase();
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

export { CaseQueue };
