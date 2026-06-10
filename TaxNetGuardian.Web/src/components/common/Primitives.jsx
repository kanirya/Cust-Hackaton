import React from "react";
import { Command } from "lucide-react";

function riskClass(value) {
  return String(value || "low").toLowerCase();
}

function Metric({ icon: Icon, label, value, trend, risk = "low" }) {
  return (
    <article className="metric-card">
      <div className="metric-icon"><Icon size={20} /></div>
      <span>{label}</span>
      <strong>{value}</strong>
      <em className={risk}>{trend}</em>
    </article>
  );
}

function Panel({ title, subtitle, icon: Icon, children }) {
  return (
    <section className="panel">
      <header>
        <div>
          <h3>{Icon && <Icon size={17} />} {title}</h3>
          {subtitle && <p>{subtitle}</p>}
        </div>
      </header>
      <div className="panel-body">{children}</div>
    </section>
  );
}

function FeedItem({ icon: Icon, title, text }) {
  return (
    <article className="feed-item">
      <Icon size={18} />
      <div><strong>{title}</strong><p>{text}</p></div>
    </article>
  );
}

function RadialScore({ value, band }) {
  const circumference = 2 * Math.PI * 42;
  const offset = circumference - (circumference * value) / 100;
  return (
    <div className="radial">
      <svg viewBox="0 0 104 104">
        <circle cx="52" cy="52" r="42" />
        <circle cx="52" cy="52" r="42" style={{ strokeDasharray: circumference, strokeDashoffset: offset }} className={riskClass(band)} />
      </svg>
      <strong>{value}</strong>
    </div>
  );
}

function InfoRows({ rows }) {
  return <div className="info-rows">{rows.map(([k, v]) => <div key={k}><span>{k}</span><strong>{v}</strong></div>)}</div>;
}

function JsonPreview({ value }) {
  return <pre className="json-preview">{JSON.stringify(value, null, 2)}</pre>;
}

function EmptyState({ title }) {
  return <div className="empty-state"><Command size={24} /><strong>{title}</strong></div>;
}

export { EmptyState, FeedItem, InfoRows, JsonPreview, Metric, Panel, RadialScore };