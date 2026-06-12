import React, { useEffect, useState } from "react";
import { Activity, ArrowRight, BadgeCheck, Bot, CheckCircle2, ClipboardCheck, FileText, Fingerprint, Network, Scale, UserCircle, Users } from "lucide-react";
import { EmptyState, InfoRows, JsonPreview, Panel, RadialScore } from "../components/common/Primitives.jsx";
import { GraphCanvas } from "../components/graph/GraphCanvas.jsx";
import { AssistantAnswer } from "../components/assistant/AssistantDrawer.jsx";

function pct(value) {
  return `${Math.round(Number(value || 0) * 100)}%`;
}

function riskClass(value) {
  return String(value || "low").toLowerCase();
}
function Investigation({ selectedCase, graph, query, setQuery, askAssistant, generateReport, assignSelectedCase, requestClarification, recordDecision, assistantAnswer, investigateCnic, cnicInvestigation }) {
  const [cnic, setCnic] = useState("");

  useEffect(() => {
    if (selectedCase?.person?.cnicMasked) {
      setCnic(selectedCase.person.cnicMasked);
    }
  }, [selectedCase?.person?.cnicMasked]);

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
        <Panel title="CNIC Investigation" subtitle="Link records by Pakistan's stable identity number, even when names differ." icon={Fingerprint}>
          <div className="cnic-investigation">
            <label>
              CNIC
              <input value={cnic} onChange={(e) => setCnic(e.target.value)} placeholder="42201-***01" />
            </label>
            <button onClick={() => investigateCnic(cnic)}><Fingerprint size={16} /> Investigate CNIC</button>
          </div>
          {cnicInvestigation && <CnicInvestigationResult result={cnicInvestigation} />}
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

function CnicInvestigationResult({ result }) {
  return (
    <div className="cnic-result">
      <div className="result-box">
        <strong>{result.status} - {result.cnicMasked}</strong>
        <p>{result.aiNarrative}</p>
        <small>{result.model?.selectedProvider} via {result.model?.route}</small>
      </div>
      <div className="investigation-sections">
        <section>
          <strong>Findings</strong>
          {result.findings?.map((item) => <p key={item}>{item}</p>)}
        </section>
        <section>
          <strong>Recommended actions</strong>
          {result.recommendedActions?.map((item) => <p key={item}>{item}</p>)}
        </section>
      </div>
      <div className="signal-list">
        {result.signals?.slice(0, 6).map((signal) => (
          <article className="evidence-card" key={signal.name}>
            <div><strong>{signal.name}</strong><span>{signal.severity}</span></div>
            <p>{signal.detail}</p>
          </article>
        ))}
      </div>
      <div className="record-list">
        {result.matchedRecords?.slice(0, 8).map((record) => (
          <article className="job-row" key={record.recordId}>
            <span className="risk-pill low">{record.recordType}</span>
            <div><strong>{record.provider} - {record.displayName}</strong><small>{record.summary}</small></div>
          </article>
        ))}
      </div>
      <small>{result.humanReviewWarning}</small>
    </div>
  );
}

export { Investigation };
