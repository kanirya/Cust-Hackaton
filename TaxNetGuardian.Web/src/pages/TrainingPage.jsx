import React, { useState } from "react";
import {
  Activity,
  BrainCircuit,
  Cpu,
  Database,
  FlaskConical,
  Gauge,
  GitBranch,
  Layers3,
  Play,
  Server,
  Sparkles,
  Zap
} from "lucide-react";
import { EmptyState, Panel } from "../components/common/Primitives.jsx";

function pct(value) {
  return `${Math.round(Number(value || 0) * 100)}%`;
}

function num(value) {
  return Number(value || 0).toLocaleString();
}

const MODES = [
  { key: "BigLlm", label: "Frontier LLM", icon: Sparkles, hint: "Always call the big model (Claude). Captures every response as training data." },
  { key: "Auto", label: "Hybrid (Auto)", icon: GitBranch, hint: "Use the retrieval model when confident, fall back to the frontier LLM otherwise." },
  { key: "CustomModel", label: "Retrieval Model", icon: Cpu, hint: "Serve cached distilled answers from the local retrieval model. Instant, zero cost." },
  { key: "LocalLlm", label: "Fine-tuned (Ollama)", icon: BrainCircuit, hint: "Serve from a real fine-tuned local LLM via Ollama. The path to replacing the big model." }
];

function ModelTraining({ customModel, trainModel, setInferenceMode, testCustomModel, examples, exportTrainingData, refreshTraining }) {
  const [testPrompt, setTestPrompt] = useState("Investigate a non-filer with high-value assets and zero declared income.");
  const [testTask, setTestTask] = useState("CnicInvestigation");
  const [testResult, setTestResult] = useState(null);
  const [testing, setTesting] = useState(false);
  const [training, setTraining] = useState(false);

  if (!customModel) {
    return <EmptyState title="Loading model training workspace…" />;
  }

  const metrics = customModel.metrics || {};
  const mode = customModel.inferenceMode || "BigLlm";
  const runs = customModel.runs || [];
  const perTask = metrics.perTaskSimilarity || {};

  async function handleTrain() {
    setTraining(true);
    try { await trainModel(); } finally { setTraining(false); }
  }

  async function handleTest() {
    setTesting(true);
    setTestResult(null);
    try { setTestResult(await testCustomModel(testTask, testPrompt)); }
    finally { setTesting(false); }
  }

  const trainedPctOfCorpus = customModel.totalExamples
    ? (customModel.totalExamples - customModel.untrainedExamples) / customModel.totalExamples
    : 0;

  return (
    <div className="page-stack">
      <div className="hero-strip">
        <div>
          <p className="eyebrow">Knowledge distillation</p>
          <h3>Train your own TaxNet model from the frontier LLM, then switch over.</h3>
        </div>
        <button onClick={handleTrain} disabled={training || customModel.training}>
          <Play size={16} /> {training || customModel.training ? "Training…" : "Train now"}
        </button>
      </div>

      {/* Inference routing: the model switch */}
      <Panel title="Inference Routing" subtitle="Choose which model serves live AI requests across the platform." icon={Server}>
        <div className="mode-switch mode-switch-4">
          {MODES.map((m) => {
            const Icon = m.icon;
            const active = mode === m.key;
            const disabled =
              (m.key === "CustomModel" || m.key === "Auto") ? !customModel.ready
              : m.key === "LocalLlm" ? !customModel.localLlm?.enabled
              : false;
            return (
              <button
                key={m.key}
                className={`mode-card ${active ? "active" : ""}`}
                onClick={() => setInferenceMode(m.key)}
                disabled={disabled}
                title={disabled ? (m.key === "LocalLlm" ? "Enable Ollama (OLLAMA_ENABLED=true) and fine-tune a model first" : "Train the custom model first") : m.hint}
              >
                <div className="mode-head"><Icon size={18} /> <strong>{m.label}</strong>{active && <span className="mode-live">LIVE</span>}</div>
                <p>{m.hint}</p>
              </button>
            );
          })}
        </div>
        <div className="local-llm-row">
          <span className={`risk-pill ${customModel.localLlm?.enabled ? "low" : "medium"}`}>
            Ollama {customModel.localLlm?.enabled ? "enabled" : "not enabled"}
          </span>
          <span className="qs-hint">model: <code>{customModel.localLlm?.model || "taxnet-guardian"}</code> · {customModel.localLlm?.baseUrl}</span>
          <button className="inline-action" onClick={() => exportTrainingData("chat")}><Database size={14} /> Export JSONL (chat)</button>
          <button className="inline-action" onClick={() => exportTrainingData("instruction")}><Database size={14} /> Export JSONL (instruction)</button>
        </div>
        {!customModel.ready && (
          <p className="settings-note">The retrieval model is not trained yet. Run the system through the Frontier LLM to collect teacher examples, then train.</p>
        )}
        <p className="settings-note">
          To build a <strong>real fine-tuned model</strong>: export the JSONL, fine-tune a small base model
          (e.g. Llama 3.1 8B) with LoRA, register it in Ollama as <code>{customModel.localLlm?.model || "taxnet-guardian"}</code>,
          set <code>OLLAMA_ENABLED=true</code>, then select <strong>Fine-tuned (Ollama)</strong> above. See
          <code>docs/Custom_Model_FineTuning.md</code>.
        </p>
      </Panel>

      {/* Headline metrics */}
      <div className="metric-grid">
        <TrainMetric icon={Database} label="Teacher examples" value={num(customModel.totalExamples)} sub={`${num(customModel.untrainedExamples)} new since last run`} />
        <TrainMetric icon={Layers3} label="Model version" value={customModel.activeVersion ? `v${customModel.activeVersion}` : "—"} sub={customModel.ready ? "Active & ready" : "Not trained"} />
        <TrainMetric icon={Gauge} label="Validation accuracy" value={pct(metrics.validationSimilarity)} sub="Response fidelity on hold-out" risk={metrics.validationSimilarity >= 0.7 ? "low" : metrics.validationSimilarity >= 0.5 ? "medium" : "critical"} />
        <TrainMetric icon={Activity} label="Coverage" value={pct(metrics.coverage)} sub="Prompts the model can answer" />
      </div>
      <div className="metric-grid">
        <TrainMetric icon={BrainCircuit} label="Vocabulary" value={num(customModel.vocabularySize)} sub="Learned terms" />
        <TrainMetric icon={Cpu} label="Indexed examples" value={num(customModel.indexedExamples)} sub="In active model" />
        <TrainMetric icon={Zap} label="Avg inference" value={`${(metrics.avgLatencyMs || 0).toFixed(1)} ms`} sub="Local, no API cost" risk="low" />
        <TrainMetric icon={Sparkles} label="Groundedness" value={pct(metrics.groundednessScore)} sub="Domain-anchor overlap" />
      </div>

      <div className="system-grid">
        <Panel title="Training Progress" subtitle="How much of the collected corpus is folded into the active model." icon={GitBranch}>
          <div className="dist-row">
            <div><span>Corpus trained</span><strong>{pct(trainedPctOfCorpus)}</strong></div>
            <div className="bar low"><i style={{ width: `${Math.min(100, trainedPctOfCorpus * 100)}%` }} /></div>
          </div>
          <div className="train-stat-rows">
            <div className="settings-row"><span>Total training tokens</span><strong>{num(customModel.totalTrainingTokens)}</strong></div>
            <div className="settings-row"><span>Examples by task</span><strong>{Object.keys(customModel.examplesByTask || {}).length} task types</strong></div>
            <div className="settings-row"><span>Teacher providers</span><strong>{Object.keys(customModel.examplesByTeacher || {}).join(", ") || "—"}</strong></div>
            <div className="settings-row"><span>Auto-serve threshold</span><strong>{pct(customModel.autoConfidenceThreshold)} confidence</strong></div>
          </div>
          <div className="task-bars">
            {Object.entries(perTask).map(([task, sim]) => (
              <div className="dist-row" key={task}>
                <div><span>{task}</span><strong>{pct(sim)}</strong></div>
                <div className={`bar ${sim >= 0.7 ? "low" : sim >= 0.5 ? "medium" : "high"}`}><i style={{ width: `${Math.min(100, sim * 100)}%` }} /></div>
              </div>
            ))}
            {Object.keys(perTask).length === 0 && <p className="settings-note">Per-task accuracy appears after the first run with a hold-out split.</p>}
          </div>
        </Panel>

        <Panel title="Test Playground" subtitle="Preview the custom model's answer for any prompt (does not change routing)." icon={FlaskConical}>
          <div className="rag-feed">
            <div className="feed-controls">
              <label>Task<select value={testTask} onChange={(e) => setTestTask(e.target.value)}>
                <option>CnicInvestigation</option>
                <option>AuditExplanation</option>
                <option>CitizenExplanation</option>
                <option>ReportDraft</option>
                <option>PolicyQuestion</option>
              </select></label>
            </div>
            <textarea className="textarea-large compact-textarea" value={testPrompt} onChange={(e) => setTestPrompt(e.target.value)} />
            <div className="upload-strip">
              <button onClick={handleTest} disabled={testing || !customModel.ready}><FlaskConical size={15} /> {testing ? "Running…" : "Run custom model"}</button>
              {testResult && <span className={`risk-pill ${testResult.confidence >= 0.55 ? "low" : testResult.confidence >= 0.35 ? "medium" : "high"}`}>confidence {pct(testResult.confidence)}</span>}
            </div>
            {testResult && (
              <div className="result-box">
                {testResult.ok
                  ? <p style={{ whiteSpace: "pre-wrap" }}>{(testResult.response || "").slice(0, 1400)}</p>
                  : <p>No confident local answer — this prompt would route to the frontier LLM.</p>}
                <small>custom model v{testResult.version}</small>
              </div>
            )}
          </div>
        </Panel>
      </div>

      <Panel title="Training Runs" subtitle="Version history with hold-out evaluation metrics." icon={Layers3}>
        <div className="data-table">
          <table>
            <thead><tr><th>Version</th><th>Status</th><th>Examples</th><th>Val. accuracy</th><th>Coverage</th><th>Vocab</th><th>Duration</th><th>Trigger</th></tr></thead>
            <tbody>
              {runs.map((r) => (
                <tr key={r.id}>
                  <td><strong>v{r.version}</strong></td>
                  <td><span className={`risk-pill ${r.status === "Succeeded" ? "low" : "critical"}`}>{r.status}</span></td>
                  <td>{num(r.totalExamples)} <small>{r.trainCount} train / {r.validationCount} val</small></td>
                  <td>{r.metrics ? pct(r.metrics.validationSimilarity) : "—"}</td>
                  <td>{r.metrics ? pct(r.metrics.coverage) : "—"}</td>
                  <td>{num(r.vocabularySize)}</td>
                  <td>{(r.durationMs || 0).toFixed(0)} ms</td>
                  <td><small>{r.triggeredBy}</small></td>
                </tr>
              ))}
            </tbody>
          </table>
          {runs.length === 0 && <EmptyState title="No training runs yet" />}
        </div>
      </Panel>

      <Panel title="Captured Teacher Examples" subtitle="Frontier-LLM responses being distilled into the local model." icon={Database}>
        <div className="job-list">
          {(examples?.items || []).slice(0, 12).map((ex) => (
            <article className="job-row training-example" key={ex.id}>
              <span className={`risk-pill ${ex.trainedIntoVersion ? "low" : "medium"}`}>{ex.trainedIntoVersion ? `v${ex.trainedIntoVersion}` : "new"}</span>
              <div>
                <strong>{ex.taskType} · {ex.teacherProvider} · {ex.promptTokens + ex.responseTokens} tok</strong>
                <small>{ex.responsePreview}</small>
              </div>
            </article>
          ))}
          {(!examples || (examples.items || []).length === 0) && <EmptyState title="No teacher examples captured yet" />}
        </div>
      </Panel>
    </div>
  );
}

function TrainMetric({ icon: Icon, label, value, sub, risk }) {
  return (
    <article className="metric-card">
      <div className="metric-icon"><Icon size={18} /></div>
      <span>{label}</span>
      <strong>{value}</strong>
      {sub && <em className={risk || ""}>{sub}</em>}
    </article>
  );
}

export { ModelTraining };
