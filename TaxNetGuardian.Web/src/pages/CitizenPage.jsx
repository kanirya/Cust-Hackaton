import React from "react";
import { BadgeCheck, CheckCircle2, FileText, ShieldCheck, Upload, UserCircle, Users } from "lucide-react";
import { EmptyState, FeedItem, Panel } from "../components/common/Primitives.jsx";

function riskClass(value) {
  return String(value || "low").toLowerCase();
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

export { Citizen };
