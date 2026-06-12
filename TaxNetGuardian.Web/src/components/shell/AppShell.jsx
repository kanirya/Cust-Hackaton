import React from "react";
import { Bell, LifeBuoy, RefreshCw, Search, Settings, Shield, UserCircle, Zap } from "lucide-react";

function Sidebar({ page, navigate, navItems, onNewInvestigation }) {
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
        <button><Settings size={17} /> Settings</button>
        <button><LifeBuoy size={17} /> Support</button>
      </div>
    </aside>
  );
}

function TopBar({ title, role, roles, setRole, onRefresh, onPipeline, searchQuery, setSearchQuery, onSearch, searchResults, onOpenResult, onClearSearch }) {
  return (
    <header className="topbar">
      <div>
        <p className="breadcrumb">Overview / {title}</p>
        <h2>{title}</h2>
      </div>
      <div className="command-search" style={{ position: "relative" }}>
        <Search size={18} />
        <input
          placeholder="Search by CNIC, NTN, name, or Case ID..."
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          onKeyDown={(e) => { if (e.key === "Enter") onSearch(searchQuery); }}
        />
        <button className="search-go" onClick={() => onSearch(searchQuery)} style={{ padding: "4px 12px", cursor: "pointer", borderRadius: 8 }}>Search</button>
        {searchResults && (
          <div style={{ position: "absolute", top: "calc(100% + 8px)", left: 0, right: 0, background: "#fff", border: "1px solid #e2e6ee", borderRadius: 12, boxShadow: "0 16px 38px rgba(20,30,60,0.20)", zIndex: 60, maxHeight: 440, overflowY: "auto", padding: 10 }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "2px 4px" }}>
              <strong style={{ fontSize: 13 }}>{searchResults.total} identity match{searchResults.total === 1 ? "" : "es"} for "{searchResults.query}"</strong>
              <button onClick={onClearSearch} style={{ cursor: "pointer", border: "none", background: "transparent", fontSize: 16, lineHeight: 1 }}>X</button>
            </div>
            <p style={{ fontSize: 11, color: "#67708a", margin: "2px 4px 8px" }}>{searchResults.explanation}</p>
            {searchResults.matches.map((m) => (
              <button key={m.personId} onClick={() => onOpenResult(m)} style={{ display: "block", width: "100%", textAlign: "left", padding: "9px 11px", marginBottom: 7, border: "1px solid #eef1f6", borderRadius: 9, background: "#f9fafc", cursor: "pointer" }}>
                <div style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap" }}>
                  <strong style={{ fontSize: 13 }}>{m.fullName}</strong>
                  {m.urduName && <span style={{ fontSize: 12, color: "#3b4763" }} dir="rtl">{m.urduName}</span>}
                  <span style={{ fontFamily: "monospace", fontSize: 11, color: "#67708a" }}>{m.cnicMasked}</span>
                  {m.case && <span className={`risk-pill ${String(m.case.riskBand).toLowerCase()}`}>{m.case.riskBand} {m.case.score}</span>}
                  <span style={{ fontSize: 10, color: "#8a93a8" }}>matched on {m.matchedOn}</span>
                </div>
                <div style={{ fontSize: 11, color: "#525c75", marginTop: 3 }}>
                  {m.city}, {m.province}{m.ntn ? ` - NTN ${m.ntn}` : ""} - {m.linkedRecords.total} linked records (tax {m.linkedRecords.tax}, vehicle {m.linkedRecords.vehicles}, property {m.linkedRecords.properties}, utility {m.linkedRecords.utilities}, business {m.linkedRecords.businesses}, travel {m.linkedRecords.travel})
                  {m.entity ? ` - entity confidence ${Math.round(m.entity.matchConfidence * 100)}%` : ""}
                </div>
                {m.possibleSameIdentity && m.possibleSameIdentity.length > 0 && (
                  <div style={{ fontSize: 11, color: "#a8430f", marginTop: 3 }}>
                    Possible same person: {m.possibleSameIdentity.map((v) => `${v.fullName} (${v.cnicMasked})`).join("; ")}
                  </div>
                )}
              </button>
            ))}
          </div>
        )}
        <kbd>Ctrl K</kbd>
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

export { Sidebar, TopBar };
