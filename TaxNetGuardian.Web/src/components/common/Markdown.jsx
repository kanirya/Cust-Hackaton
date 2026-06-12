import React from "react";

// Lightweight, dependency-free Markdown renderer for AI narratives.
// Handles headings, bold, bullet/numbered lists, horizontal rules, and paragraphs —
// the subset Claude produces. Renders to safe React elements (no dangerouslySetInnerHTML).

function renderInline(text, keyPrefix) {
  // Split on **bold** segments and render <strong> for the bold parts.
  const parts = String(text).split(/(\*\*[^*]+\*\*)/g);
  return parts.map((part, i) => {
    if (part.startsWith("**") && part.endsWith("**") && part.length > 4) {
      return <strong key={`${keyPrefix}-b${i}`}>{part.slice(2, -2)}</strong>;
    }
    // also strip stray single backticks for inline code-ish text
    return <React.Fragment key={`${keyPrefix}-t${i}`}>{part.replace(/`/g, "")}</React.Fragment>;
  });
}

function Markdown({ content }) {
  if (!content) return null;
  const lines = String(content).replace(/\r\n/g, "\n").split("\n");
  const blocks = [];
  let list = null; // { type: "ul" | "ol", items: [] }

  const flushList = () => {
    if (!list) return;
    const items = list.items.map((item, i) => <li key={`li-${blocks.length}-${i}`}>{renderInline(item, `li-${blocks.length}-${i}`)}</li>);
    blocks.push(list.type === "ol" ? <ol key={`ol-${blocks.length}`}>{items}</ol> : <ul key={`ul-${blocks.length}`}>{items}</ul>);
    list = null;
  };

  lines.forEach((raw) => {
    const line = raw.trim();
    if (line === "") { flushList(); return; }
    if (/^---+$/.test(line)) { flushList(); blocks.push(<hr key={`hr-${blocks.length}`} />); return; }

    const heading = line.match(/^(#{1,4})\s+(.*)$/);
    if (heading) {
      flushList();
      const level = heading[1].length;
      const text = heading[2];
      const key = `h-${blocks.length}`;
      if (level === 1) blocks.push(<h3 key={key} className="md-h1">{renderInline(text, key)}</h3>);
      else if (level === 2) blocks.push(<h4 key={key} className="md-h2">{renderInline(text, key)}</h4>);
      else blocks.push(<h5 key={key} className="md-h3">{renderInline(text, key)}</h5>);
      return;
    }

    const bullet = line.match(/^[-*]\s+(.*)$/);
    if (bullet) {
      if (!list || list.type !== "ul") { flushList(); list = { type: "ul", items: [] }; }
      list.items.push(bullet[1]);
      return;
    }

    const numbered = line.match(/^\d+[.)]\s+(.*)$/);
    if (numbered) {
      if (!list || list.type !== "ol") { flushList(); list = { type: "ol", items: [] }; }
      list.items.push(numbered[1]);
      return;
    }

    flushList();
    const key = `p-${blocks.length}`;
    blocks.push(<p key={key}>{renderInline(line, key)}</p>);
  });

  flushList();
  return <div className="markdown">{blocks}</div>;
}

export { Markdown };
