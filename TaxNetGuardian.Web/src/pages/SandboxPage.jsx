import React, { useEffect, useState } from "react";
import { ClipboardCheck, Database, FileText, KeyRound, Landmark, Upload, Users } from "lucide-react";
import { EmptyState, JsonPreview, Panel } from "../components/common/Primitives.jsx";

function riskClass(value) {
  return String(value || "low").toLowerCase();
}
function Sandbox({ providers, profiles, selectedProfile, openProfile, generateSandbox, datasetHub, datasetTemplates, feedDataset, updateProvider }) {
return (
    <div className="page-stack">
      <div className="hero-strip sandbox-hero">
        <div>
          <p className="eyebrow">Separate backend and UI</p>
          <h3>Government API emulator with real-provider readiness.</h3>
        </div>
        <button onClick={generateSandbox}><Database size={16} /> Generate 180 profiles</button>
      </div>
      <DatasetFeedCenter datasetHub={datasetHub} datasetTemplates={datasetTemplates} feedDataset={feedDataset} />
      <div className="provider-grid">
        {providers.map((provider) => (
          <article className="provider-card" key={provider.providerCode}>
            <div className="provider-icon"><Landmark size={20} /></div>
            <div>
              <strong>{provider.providerCode}</strong>
              <p>{provider.name}</p>
              <span className={`risk-pill ${provider.status === "Healthy" ? "low" : "medium"}`}>{provider.status}</span>
            </div>
            <small>{provider.credentialSecretName}</small>
            <button
              className="inline-action"
              onClick={() => updateProvider(provider.providerCode, {
                mode: "OfficialReady",
                baseUrl: `https://api.${provider.providerCode.toLowerCase().replaceAll("-", "")}.gov.pk`,
                credentialSecretName: provider.credentialSecretName,
                enabled: true,
                rateLimitPerMinute: provider.supportsBulkImport ? 120 : 60,
                notes: "Sandbox contract configured so official API credentials can be swapped through Secrets Manager."
              })}
            >
              <KeyRound size={14} /> Official-ready
            </button>
          </article>
        ))}
      </div>
      <div className="sandbox-layout">
        <Panel title="Synthetic Profiles" subtitle={`${profiles.length} profiles loaded from sandbox.`} icon={Users}>
          <div className="data-table compact">
            <table>
              <thead><tr><th>ID</th><th>Name</th><th>City</th><th>Expected</th><th>Assets</th></tr></thead>
              <tbody>
                {profiles.map((profile) => (
                  <tr key={profile.id} onClick={() => openProfile(profile.id)}>
                    <td>{profile.id}</td>
                    <td><strong>{profile.fullName}</strong><small>{profile.cnicMasked}</small></td>
                    <td>{profile.city}</td>
                    <td><span className={`risk-pill ${riskClass(profile.expectedRiskBand)}`}>{profile.expectedRiskBand}</span></td>
                    <td>{profile.vehicleCount} V - {profile.propertyCount} P - {profile.businessCount} B</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>
        <Panel title="Provider Record Preview" icon={Database}>
          {selectedProfile ? <JsonPreview value={selectedProfile} /> : <EmptyState title="Select a profile" />}
        </Panel>
      </div>
    </div>
  );
}

function DatasetFeedCenter({ datasetHub, datasetTemplates, feedDataset }) {
  const firstType = datasetTemplates?.[0]?.datasetType || "identity";
  const [datasetType, setDatasetType] = useState(firstType);
  const [format, setFormat] = useState("csv");
  const [fileName, setFileName] = useState("identity-seed.csv");
  const [content, setContent] = useState("");
  const [runPipeline, setRunPipeline] = useState(true);

  useEffect(() => {
    if (!datasetTemplates?.length) return;
    const current = datasetTemplates.find((template) => template.datasetType === datasetType) || datasetTemplates[0];
    setDatasetType(current.datasetType);
    setFileName(`${current.datasetType}-feed.${format}`);
    if (!content) setContent(current.csvExample);
  }, [datasetTemplates]);

  const selectedTemplate = datasetTemplates?.find((template) => template.datasetType === datasetType);

  function loadExample(nextType = datasetType, nextFormat = format) {
    const template = datasetTemplates?.find((item) => item.datasetType === nextType);
    if (!template) return;
    setDatasetType(nextType);
    setFormat(nextFormat);
    setFileName(`${nextType}-feed.${nextFormat}`);
    if (nextFormat === "json") {
      const [headerLine, ...rows] = template.csvExample.split("\n");
      const headers = headerLine.split(",");
      const records = rows.filter(Boolean).map((row) => {
        const values = row.split(",");
        return Object.fromEntries(headers.map((header, index) => [header, values[index] || ""]));
      });
      setContent(JSON.stringify({ records }, null, 2));
      return;
    }
    setContent(template.csvExample);
  }

  function handleFile(event) {
    const file = event.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      setContent(String(reader.result || ""));
      setFileName(file.name);
      setFormat(file.name.toLowerCase().endsWith(".json") ? "json" : "csv");
    };
    reader.readAsText(file);
  }

  async function submit() {
    await feedDataset({ datasetType, format, fileName, content, runPipeline });
  }

  return (
    <div className="feed-layout">
      <Panel title="Dataset Feed Console" subtitle="Upload CSV or JSON into the sandbox adapter, then score it through the same risk pipeline." icon={Upload}>
        <div className="feed-form">
          <div className="feed-controls">
            <label>Dataset<select value={datasetType} onChange={(e) => loadExample(e.target.value, format)}>
              {datasetTemplates?.map((template) => <option key={template.datasetType} value={template.datasetType}>{template.datasetType}</option>)}
            </select></label>
            <label>Format<select value={format} onChange={(e) => loadExample(datasetType, e.target.value)}>
              <option value="csv">CSV</option>
              <option value="json">JSON</option>
            </select></label>
            <label>File name<input value={fileName} onChange={(e) => setFileName(e.target.value)} /></label>
            <label className="checkbox-line"><input type="checkbox" checked={runPipeline} onChange={(e) => setRunPipeline(e.target.checked)} /> Run risk pipeline</label>
          </div>
          <div className="upload-strip">
            <input type="file" accept=".csv,.json,text/csv,application/json" onChange={handleFile} />
            <button onClick={() => loadExample()}><FileText size={15} /> Load template</button>
            <button onClick={submit}><Upload size={15} /> Feed dataset</button>
          </div>
          <textarea className="textarea-large" value={content} onChange={(e) => setContent(e.target.value)} spellCheck="false" />
        </div>
      </Panel>
      <Panel title="Feed Health" subtitle={`${datasetHub?.totals?.records || 0} records received across ${datasetHub?.totals?.batches || 0} batches.`} icon={ClipboardCheck}>
        {selectedTemplate && (
          <div className="template-card">
            <strong>{selectedTemplate.description}</strong>
            <p>{selectedTemplate.columns.join(", ")}</p>
          </div>
        )}
        <div className="job-list">
          {(datasetHub?.jobs || []).slice(0, 6).map((job) => (
            <article key={job.id} className="job-row">
              <span className={`risk-pill ${job.status === "Failed" ? "critical" : job.status === "SucceededWithWarnings" ? "medium" : "low"}`}>{job.status}</span>
              <div><strong>{job.source}</strong><small>{job.recordsCreated}/{job.recordsProcessed} applied</small></div>
            </article>
          ))}
          {(!datasetHub?.jobs || datasetHub.jobs.length === 0) && <EmptyState title="No dataset feeds yet" />}
        </div>
      </Panel>
    </div>
  );
}

export { Sandbox };
