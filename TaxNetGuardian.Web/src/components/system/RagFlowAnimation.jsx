import React, { useEffect, useState } from "react";

// Graph-style visualization of the RAG pipeline. Nodes light up in sequence while a query runs,
// edges pulse a "data packet" along the path, and the retrieved chunks render with animated
// relevance bars when the result arrives.
const STAGES = [
  { id: "query", label: "Query", sub: "auditor question" },
  { id: "tokenize", label: "Tokenize", sub: "extract terms" },
  { id: "search", label: "Search Index", sub: "scan chunks" },
  { id: "rank", label: "Score & Rank", sub: "relevance" },
  { id: "topk", label: "Top-K", sub: "select context" },
  { id: "ground", label: "Ground Model", sub: "cite + answer" }
];

function pct(v) { return `${Math.round(Number(v || 0) * 100)}%`; }

function RagFlowAnimation({ result, running }) {
  const [activeStage, setActiveStage] = useState(-1);

  // Sequentially light up the stages while a query is running.
  useEffect(() => {
    if (running) {
      setActiveStage(0);
      const timers = STAGES.map((_, i) => setTimeout(() => setActiveStage(i), i * 280));
      return () => timers.forEach(clearTimeout);
    }
    if (result) setActiveStage(STAGES.length - 1);
  }, [running, result]);

  const done = !running && !!result;
  const path = result?.retrievalPath === "embedding" ? "Embedding similarity" : "Deterministic lexical";
  const ranked = result?.ranked || [];
  const terms = result?.queryTerms || [];

  return (
    <div className="rag-flow">
      <div className="rag-flow-graph">
        <svg viewBox="0 0 720 90" preserveAspectRatio="xMidYMid meet" className="rag-flow-svg">
          {STAGES.slice(0, -1).map((_, i) => {
            const x1 = 60 + i * 120;
            const x2 = 60 + (i + 1) * 120;
            const lit = running ? i < activeStage : done;
            return (
              <line
                key={`e${i}`}
                x1={x1 + 26} y1={45} x2={x2 - 26} y2={45}
                className={`rag-edge ${lit ? "lit" : ""} ${running && i === activeStage - 1 ? "flow" : ""}`}
              />
            );
          })}
          {STAGES.map((s, i) => {
            const cx = 60 + i * 120;
            const state = running
              ? (i < activeStage ? "done" : i === activeStage ? "active" : "")
              : (done ? "done" : "");
            return (
              <g key={s.id} className={`rag-node ${state}`}>
                <circle cx={cx} cy={45} r={20} />
                <text x={cx} y={49} textAnchor="middle" className="rag-node-idx">{i + 1}</text>
              </g>
            );
          })}
        </svg>
        <div className="rag-flow-labels">
          {STAGES.map((s, i) => (
            <div key={s.id} className={`rag-flow-label ${(running ? i <= activeStage : done) ? "on" : ""}`}>
              <strong>{s.label}</strong>
              <span>{s.sub}</span>
            </div>
          ))}
        </div>
      </div>

      {done && (
        <div className="rag-flow-result">
          <div className="rag-flow-meta">
            <span className={`rag-path-badge ${result.retrievalPath === "embedding" ? "embed" : "lexical"}`}>{path}</span>
            <span className="rag-conf">Confidence <strong>{pct(result.retrievalConfidence)}</strong></span>
            <span className="rag-terms">
              {terms.slice(0, 8).map((t) => <i key={t}>{t}</i>)}
            </span>
          </div>
          <div className="rag-chunks">
            {ranked.length === 0 && <div className="rag-noresult">No chunks matched this query.</div>}
            {ranked.map((c, i) => (
              <div className="rag-chunk-row" key={c.chunkId} style={{ animationDelay: `${i * 70}ms` }}>
                <div className="rag-chunk-head">
                  <strong>{c.title}</strong>
                  <span className="rag-chunk-src">{c.sourceType}</span>
                </div>
                <div className="rag-relbar">
                  <i style={{ width: pct(c.relevance) }} className={c.relevance > 0 ? "" : "zero"} />
                </div>
                <div className="rag-chunk-foot">
                  <span>relevance {pct(c.relevance)}</span>
                  <span>{c.matchedTerms} term{c.matchedTerms === 1 ? "" : "s"} matched</span>
                  <span>score {Number(c.score).toFixed(1)}</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

export { RagFlowAnimation };
