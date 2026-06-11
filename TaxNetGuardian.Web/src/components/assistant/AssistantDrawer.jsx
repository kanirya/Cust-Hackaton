import React from "react";
import { ArrowRight, Bot, Sparkles } from "lucide-react";
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

export { AssistantAnswer, AssistantDrawer };
