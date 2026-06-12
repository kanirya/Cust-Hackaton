import React from "react";
import { KeyRound, LockKeyhole, ShieldCheck, ToggleLeft } from "lucide-react";
import { Panel } from "../components/common/Primitives.jsx";

function Settings({ flagItems, setFeatureFlag, authz, cognito }) {
  const categories = [...new Set((flagItems || []).map((f) => f.category))];

  return (
    <div className="page-stack">
      <div className="hero-strip">
        <div>
          <p className="eyebrow">Control plane</p>
          <h3>Settings, authentication, and feature flags.</h3>
        </div>
        <span className="risk-pill low">{(flagItems || []).filter((f) => f.enabled).length}/{(flagItems || []).length} features on</span>
      </div>

      <div className="system-grid">
        <Panel title="Authentication" subtitle="How users and services are identified." icon={LockKeyhole}>
          <div className="settings-rows">
            <div className="settings-row"><span>Mode</span><strong>{authz?.mode || "DevelopmentHeaders"}</strong></div>
            <div className="settings-row"><span>JWT configured</span><strong>{authz?.jwt?.configured ? "Yes" : "No"}</strong></div>
            <div className="settings-row"><span>Authority</span><strong>{authz?.jwt?.authority || "—"}</strong></div>
            <div className="settings-row"><span>Audience</span><strong>{authz?.jwt?.audience || "—"}</strong></div>
            <div className="settings-row"><span>Role header (dev)</span><strong>{authz?.headerRole || "X-Demo-Role"}</strong></div>
          </div>
          <p className="settings-note">
            Production target: Amazon Cognito user pools (JWT) for users and OAuth client credentials for services.
            Set <code>TaxNet:Auth:Mode = CognitoJwt</code> with an authority and audience to enforce real tokens.
          </p>
        </Panel>

        <Panel title="Cognito Status" subtitle="Identity provider readiness." icon={ShieldCheck}>
          <div className="settings-rows">
            <div className="settings-row"><span>Provider</span><strong>{cognito?.provider || "Amazon Cognito"}</strong></div>
            <div className="settings-row"><span>Reachable</span><strong>{cognito?.reachable ? "Yes" : "No / not configured"}</strong></div>
            <div className="settings-row"><span>User pool</span><strong>{cognito?.userPoolId || "—"}</strong></div>
            <div className="settings-row"><span>Endpoint</span><strong className="truncate">{cognito?.endpoint || "—"}</strong></div>
          </div>
          <p className="settings-note">{cognito?.message || "Cognito runs against LocalStack in dev; swap to AWS endpoints for production."}</p>
        </Panel>
      </div>

      <Panel title="Feature Flags" subtitle="Toggle application features on/off. Changes persist and apply across the UI immediately." icon={ToggleLeft}>
        <div className="flags-wrap">
          {categories.map((cat) => (
            <div className="flag-group" key={cat}>
              <div className="flag-group-title">{cat}</div>
              {(flagItems || []).filter((f) => f.category === cat).map((f) => (
                <div className="flag-row" key={f.key}>
                  <div className="flag-meta">
                    <strong>{f.label}</strong>
                    <span>{f.description}</span>
                    <code>{f.key}</code>
                  </div>
                  <button
                    className={`switch ${f.enabled ? "on" : ""}`}
                    role="switch"
                    aria-checked={f.enabled}
                    onClick={() => setFeatureFlag(f.key, !f.enabled)}
                    title={f.enabled ? "Disable" : "Enable"}
                  >
                    <i />
                  </button>
                </div>
              ))}
            </div>
          ))}
          {(!flagItems || flagItems.length === 0) && <p className="settings-note">No feature flags available.</p>}
        </div>
      </Panel>
    </div>
  );
}

export { Settings };
