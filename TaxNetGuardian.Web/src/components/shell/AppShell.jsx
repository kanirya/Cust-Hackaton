import React, { useEffect, useRef, useState } from "react";
import { Bell, Bot, LifeBuoy, RefreshCw, Search, Settings, Shield, UserCircle, Zap } from "lucide-react";

function Sidebar({ page, navigate, navItems, onNewInvestigation, onOpenSettings }) {
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
      <button className="new-investigation" onClick={onNewInvestigation}><Zap size={18} /> New Investigation</button>
      <div className="user-card">
        <div className="avatar"><UserCircle size={26} /></div>
        <div>
          <strong>Tax Auditor</strong>
          <span>Region North</span>
        </div>
      </div>
      <div className="sidebar-footer">
        <button onClick={onOpenSettings} className={page === "settings" ? "active" : ""}><Settings size={17} /> Settings</button>
        <button><LifeBuoy size={17} /> Support</button>
      </div>
    </aside>
  );
}

function TopBar({ title, role, roles, setRole, onRefresh, onPipeline, onSearch, fetchSuggestions, onSelectHit, assistantOpen, onToggleAssistant, showAssistantToggle = true }) {
  const [term, setTerm] = useState("");
  const [busy, setBusy] = useState(false);
  const [suggestions, setSuggestions] = useState([]);
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const inputRef = useRef(null);
  const boxRef = useRef(null);

  // Ctrl+K focuses the global search box.
  useEffect(() => {
    function handleKey(e) {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        inputRef.current?.focus();
      }
    }
    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, []);

  // Debounced live suggestions as the auditor types.
  useEffect(() => {
    if (!fetchSuggestions) return undefined;
    const value = term.trim();
    if (value.length < 2) {
      setSuggestions([]);
      setOpen(false);
      return undefined;
    }
    let cancelled = false;
    const handle = setTimeout(async () => {
      const hits = await fetchSuggestions(value);
      if (cancelled) return;
      setSuggestions(hits);
      setActiveIndex(-1);
      setOpen(hits.length > 0);
    }, 180);
    return () => { cancelled = true; clearTimeout(handle); };
  }, [term, fetchSuggestions]);

  // Close the dropdown on outside click.
  useEffect(() => {
    function onClickOutside(e) {
      if (boxRef.current && !boxRef.current.contains(e.target)) setOpen(false);
    }
    window.addEventListener("mousedown", onClickOutside);
    return () => window.removeEventListener("mousedown", onClickOutside);
  }, []);

  async function pick(hit) {
    setOpen(false);
    setTerm("");
    setSuggestions([]);
    if (onSelectHit) await onSelectHit(hit);
  }

  async function submitSearch() {
    const value = term.trim();
    if (!value || busy || !onSearch) return;
    if (activeIndex >= 0 && suggestions[activeIndex]) {
      await pick(suggestions[activeIndex]);
      return;
    }
    setOpen(false);
    setBusy(true);
    try {
      await onSearch(value);
      setTerm("");
    } finally {
      setBusy(false);
    }
  }

  function onKeyDown(e) {
    if (!open || suggestions.length === 0) return;
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setActiveIndex((i) => Math.min(i + 1, suggestions.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setActiveIndex((i) => Math.max(i - 1, -1));
    } else if (e.key === "Escape") {
      setOpen(false);
    }
  }

  function confClass(c) {
    if (c >= 0.92) return "exact";
    if (c >= 0.75) return "strong";
    return "weak";
  }

  return (
    <header className="topbar">
      <div>
        <p className="breadcrumb">Overview / {title}</p>
        <h2>{title}</h2>
      </div>
      <div className="command-search-wrap" ref={boxRef}>
        <form
          className="command-search"
          onSubmit={(e) => { e.preventDefault(); submitSearch(); }}
        >
          <button type="submit" className="search-trigger" aria-label="Search" disabled={busy}>
            <Search size={18} />
          </button>
          <input
            ref={inputRef}
            value={term}
            onChange={(e) => setTerm(e.target.value)}
            onKeyDown={onKeyDown}
            onFocus={() => suggestions.length > 0 && setOpen(true)}
            placeholder="Search by CNIC, NTN, Case ID, name..."
          />
          <kbd>Ctrl K</kbd>
        </form>
        {open && suggestions.length > 0 && (
          <div className="search-suggest" role="listbox">
            {suggestions.map((hit, i) => (
              <button
                key={`${hit.personId || hit.caseId}-${i}`}
                type="button"
                className={`suggest-row ${i === activeIndex ? "active" : ""}`}
                onMouseEnter={() => setActiveIndex(i)}
                onClick={() => pick(hit)}
                role="option"
                aria-selected={i === activeIndex}
              >
                <div className="suggest-main">
                  <strong>{hit.fullName}</strong>
                  <span>{hit.cnicMasked}{hit.ntn ? ` · ${hit.ntn}` : ""} · {hit.city}</span>
                </div>
                <div className="suggest-meta">
                  {hit.riskBand && <span className={`risk-pill ${String(hit.riskBand).toLowerCase()}`}>{hit.riskBand}</span>}
                  <span className="suggest-match">{hit.matchType}</span>
                  <span className={`conf-badge ${confClass(hit.confidence)}`}>{Math.round(hit.confidence * 100)}%</span>
                </div>
              </button>
            ))}
          </div>
        )}
      </div>
      <div className="top-actions">
        <select value={role} onChange={(e) => setRole(e.target.value)} aria-label="Role">
          {roles.map((r) => <option key={r}>{r}</option>)}
        </select>
        <button onClick={onPipeline}><RefreshCw size={16} /> Pipeline</button>
        {showAssistantToggle && (
          <button
            className={`assistant-toggle ${assistantOpen ? "active" : ""}`}
            onClick={onToggleAssistant}
            aria-label="Toggle AI Assistant"
            title={assistantOpen ? "Hide AI Assistant" : "Show AI Assistant"}
          >
            <Bot size={16} />
          </button>
        )}
        <button onClick={onRefresh}><Bell size={16} /></button>
      </div>
    </header>
  );
}

export { Sidebar, TopBar };
