import React, { useEffect, useRef } from "react";
import { ArrowRight, Bot, Sparkles, X } from "lucide-react";
import { Markdown } from "../common/Markdown.jsx";

function AssistantDrawer({ selectedCase, conversation, input, setInput, askAssistant, onClose }) {
  const endRef = useRef(null);
  const caseId = selectedCase?.caseItem?.id;

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
  }, [conversation]);

  function submit(e) {
    e?.preventDefault();
    const q = (input || "").trim();
    if (!q) return;
    askAssistant(q);
  }

  const messages = conversation || [];

  return (
    <aside className="assistant-drawer chat">
      <div className="drawer-head">
        <Bot size={20} />
        <div>
          <strong>AI Assistant</strong>
          <span>{caseId ? `Case ${caseId}` : "Evidence-grounded support"}</span>
        </div>
        {onClose && (
          <button className="drawer-close" onClick={onClose} aria-label="Hide assistant" title="Hide assistant">
            <X size={16} />
          </button>
        )}
      </div>

      <div className="chat-thread">
        {messages.length === 0 && (
          <div className="chat-empty">
            <Sparkles size={20} />
            <p>Ask about missing evidence, a citizen-safe explanation, or report language{caseId ? ` for ${caseId}` : ""}.</p>
          </div>
        )}
        {messages.map((m, i) => (
          <div key={i} className={`chat-msg ${m.role}`}>
            <div className="chat-bubble">
              {m.role === "assistant"
                ? (m.text ? <Markdown content={m.text} /> : <TypingDots />)
                : <p>{m.text}</p>}
            </div>
          </div>
        ))}
        <div ref={endRef} />
      </div>

      <form className="chat-input" onSubmit={submit}>
        <input
          value={input}
          onChange={(e) => setInput(e.target.value)}
          placeholder={caseId ? "Message the assistant…" : "Select a case to chat…"}
          disabled={!caseId}
        />
        <button type="submit" disabled={!caseId || !(input || "").trim()} aria-label="Send">
          <ArrowRight size={18} />
        </button>
      </form>
    </aside>
  );
}

function TypingDots() {
  return <span className="typing-dots"><i /><i /><i /></span>;
}

function AssistantAnswer({ answer }) {
  return (
    <div className="assistant-answer">
      <strong>{answer.riskBand ? `${answer.riskBand} - ${answer.score}/100` : "Assistant response"}</strong>
      <Markdown content={answer.answer} />
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
