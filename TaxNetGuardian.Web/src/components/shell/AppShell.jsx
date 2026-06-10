import React from "react";
import { Bell, LifeBuoy, RefreshCw, Search, Settings, Shield, UserCircle, Zap } from "lucide-react";

function Sidebar({ page, navigate, navItems }) {
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
      <button className="new-investigation"><Zap size={18} /> New Investigation</button>
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

function TopBar({ title, role, roles, setRole, onRefresh, onPipeline }) {
  return (
    <header className="topbar">
      <div>
        <p className="breadcrumb">Overview / {title}</p>
        <h2>{title}</h2>
      </div>
      <div className="command-search">
        <Search size={18} />
        <input placeholder="Search by NTN, CNIC, Case ID, provider..." />
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
