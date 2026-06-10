const state = {
  selectedCaseId: null,
  selectedEntityId: null,
  role: localStorage.getItem("taxnet.role") || "taxnet-admin"
};

document.addEventListener("DOMContentLoaded", () => {
  const page = document.body.dataset.page;
  if (page === "auditor") initAuditor();
  if (page === "sandbox") initSandbox();
  if (page === "citizen") initCitizen();
});

async function api(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      "X-Demo-Role": state.role,
      "X-Demo-User": "hackathon-demo-user",
      ...(options.headers || {})
    }
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `Request failed: ${response.status}`);
  }

  return response.json();
}

function money(value) {
  if (value === null || value === undefined) return "n/a";
  return `PKR ${Number(value).toLocaleString()}`;
}

function pct(value) {
  return `${Math.round(Number(value) * 100)}%`;
}

function riskClass(risk) {
  return String(risk || "").toLowerCase();
}

function el(tag, attrs = {}, children = []) {
  const node = document.createElement(tag);
  Object.entries(attrs).forEach(([key, value]) => {
    if (key === "class") node.className = value;
    else if (key === "text") node.textContent = value;
    else if (key.startsWith("on") && typeof value === "function") node.addEventListener(key.slice(2), value);
    else node.setAttribute(key, value);
  });
  children.forEach(child => node.append(child));
  return node;
}

function setHtml(id, html) {
  const target = document.getElementById(id);
  if (target) target.innerHTML = html;
}

async function initAuditor() {
  const roleSelect = document.getElementById("roleSelect");
  roleSelect.value = state.role;
  roleSelect.addEventListener("change", () => {
    state.role = roleSelect.value;
    localStorage.setItem("taxnet.role", state.role);
    loadAuditor();
  });

  document.getElementById("refreshButton").addEventListener("click", loadAuditor);
  document.getElementById("riskFilter").addEventListener("change", loadCases);
  document.getElementById("askButton").addEventListener("click", askAssistant);
  document.getElementById("reportButton").addEventListener("click", generateReport);
  document.getElementById("runPipelineButton").addEventListener("click", async () => {
    await api("/api/ingestion/run", { method: "POST", body: "{}" });
    await loadAuditor();
  });

  await loadAuditor();
}

async function loadAuditor() {
  await Promise.all([
    loadMetrics(),
    loadCases(),
    loadWorkers(),
    loadRag(),
    loadAuthz()
  ]);
}

async function loadMetrics() {
  const summary = await api("/api/dashboard/summary");
  const metrics = [
    ["Total profiles", summary.totalProfiles, "Synthetic records in Gov Data Sandbox"],
    ["Flagged cases", summary.totalCases, "Cases requiring monitoring or review"],
    ["Critical cases", summary.criticalCases, "Urgent human review queue"],
    ["Recoverable estimate", money(summary.estimatedRecoverableTax), "Model-based opportunity estimate"]
  ];

  document.getElementById("metrics").innerHTML = metrics.map(([label, value, caption]) => `
    <div class="panel metric">
      <span>${label}</span>
      <strong>${value}</strong>
      <small>${caption}</small>
    </div>
  `).join("");
}

async function loadCases() {
  const risk = document.getElementById("riskFilter")?.value || "";
  const data = await api(`/api/cases${risk ? `?riskBand=${encodeURIComponent(risk)}` : ""}`);
  const body = document.getElementById("casesTable");

  body.innerHTML = data.items.map(item => `
    <tr class="clickable" data-case="${item.id}" data-entity="${item.entityId}">
      <td><strong>${item.id}</strong><br><span class="subtle">${item.status}</span></td>
      <td>${item.city}<br><span class="subtle">${item.province}</span></td>
      <td><span class="badge ${riskClass(item.riskBand)}">${item.riskBand}</span></td>
      <td><strong>${item.score}</strong><br><span class="subtle">${pct(item.confidence)} confidence</span></td>
      <td>${Array.from(item.topReasons).join("<br>")}</td>
    </tr>
  `).join("");

  body.querySelectorAll("tr").forEach(row => {
    row.addEventListener("click", () => selectCase(row.dataset.case, row.dataset.entity));
  });

  if (!state.selectedCaseId && data.items.length) {
    await selectCase(data.items[0].id, data.items[0].entityId);
  }
}

async function selectCase(caseId, entityId) {
  state.selectedCaseId = caseId;
  state.selectedEntityId = entityId;
  await Promise.all([loadCaseDetail(caseId), loadGraph(entityId)]);
}

async function loadCaseDetail(caseId) {
  const data = await api(`/api/cases/${caseId}`);
  const c = data.caseItem;
  const p = data.person;
  const explanation = data.explanation;

  document.getElementById("caseHint").textContent = `${p.fullName} | ${p.city} | ${c.score.riskBand}`;
  document.getElementById("caseDetail").innerHTML = `
    <div class="stack">
      <div class="kv"><span>Subject</span><strong>${p.fullName}</strong></div>
      <div class="kv"><span>Masked CNIC</span><strong>${p.cnicMasked}</strong></div>
      <div class="kv"><span>Risk score</span><strong>${c.score.score}/100 <span class="badge ${riskClass(c.score.riskBand)}">${c.score.riskBand}</span></strong></div>
      <div class="progress"><i style="width:${c.score.score}%"></i></div>
      <div class="reason"><strong>AI explanation</strong><p>${explanation.summary}</p></div>
      ${c.score.components.filter(x => x.score > 0).map(x => `
        <div class="reason">
          <strong>${x.name} <span class="badge">${x.score}/${x.maxScore}</span></strong>
          <p>${x.explanation}</p>
        </div>
      `).join("")}
      <div class="cards">
        ${c.evidence.slice(0, 6).map(ev => `
          <div class="mini-card">
            <h4>${ev.title}</h4>
            <p>${ev.description}</p>
            <p><strong>${ev.source}</strong></p>
          </div>
        `).join("")}
      </div>
    </div>
  `;
}

async function loadGraph(entityId) {
  const graph = await api(`/api/graph/entities/${entityId}/neighborhood`);
  drawGraph(graph);
}

function drawGraph(graph) {
  const svg = document.getElementById("graphSvg");
  svg.innerHTML = "";
  const width = 720;
  const height = 430;
  const center = { x: 360, y: 215 };
  const radius = 150;
  const nodes = graph.nodes.map((node, index) => {
    if (index === 0) return { ...node, x: center.x, y: center.y };
    const angle = (Math.PI * 2 * (index - 1)) / Math.max(1, graph.nodes.length - 1) - Math.PI / 2;
    return { ...node, x: center.x + radius * Math.cos(angle), y: center.y + radius * Math.sin(angle) };
  });
  const byId = new Map(nodes.map(n => [n.id, n]));
  const colorByType = {
    Person: "#0f766e",
    Vehicle: "#2563eb",
    Property: "#7c3aed",
    UtilityMeter: "#b45309",
    Business: "#0f766e",
    TravelEvent: "#db2777",
    Case: "#b42318"
  };

  graph.edges.forEach(edge => {
    const a = byId.get(edge.source);
    const b = byId.get(edge.target);
    if (!a || !b) return;
    svg.append(svgEl("line", { x1: a.x, y1: a.y, x2: b.x, y2: b.y, class: "edge" }));
    const midX = (a.x + b.x) / 2;
    const midY = (a.y + b.y) / 2;
    svg.append(svgEl("text", { x: midX, y: midY - 4, "text-anchor": "middle" }, edge.type.replaceAll("_", " ")));
  });

  nodes.forEach(node => {
    const group = svgEl("g", {});
    group.append(svgEl("circle", {
      cx: node.x,
      cy: node.y,
      r: node.type === "Person" ? 28 : 21,
      fill: colorByType[node.type] || "#475467",
      class: "node"
    }));
    group.append(svgEl("text", {
      x: node.x,
      y: node.y + 42,
      "text-anchor": "middle"
    }, truncate(node.label, 22)));
    group.append(svgEl("text", {
      x: node.x,
      y: node.y + 4,
      "text-anchor": "middle",
      fill: "#fff",
      style: "fill:#fff;font-weight:800;font-size:10px"
    }, node.type.slice(0, 2).toUpperCase()));
    svg.append(group);
  });
}

function svgEl(name, attrs = {}, text = null) {
  const node = document.createElementNS("http://www.w3.org/2000/svg", name);
  Object.entries(attrs).forEach(([key, value]) => node.setAttribute(key, value));
  if (text !== null) node.textContent = text;
  return node;
}

function truncate(text, max) {
  return text.length > max ? `${text.slice(0, max - 1)}...` : text;
}

async function askAssistant() {
  if (!state.selectedCaseId) return;
  const question = document.getElementById("assistantQuestion").value;
  const result = await api(`/api/assistant/cases/${state.selectedCaseId}/ask`, {
    method: "POST",
    body: JSON.stringify({ question })
  });
  document.getElementById("assistantAnswer").innerHTML = `
    <strong>${result.riskBand} | ${result.score}/100</strong>
    <p>${result.answer}</p>
    <p><strong>Evidence:</strong> ${result.evidenceIds.join(", ")}</p>
  `;
}

async function generateReport() {
  if (!state.selectedCaseId) return;
  const report = await api(`/api/reports/cases/${state.selectedCaseId}`, { method: "POST", body: "{}" });
  document.getElementById("assistantAnswer").innerHTML = `
    <strong>Report generated</strong>
    <p>${report.reportId}</p>
    <p>${report.caseSummary}</p>
    <p><strong>Watermark:</strong> ${report.watermark}</p>
  `;
}

async function loadWorkers() {
  const data = await api("/api/system/workers");
  document.getElementById("workers").innerHTML = data.workers.map(worker => `
    <div class="mini-card">
      <h4>${worker.name}</h4>
      <p>${worker.queueName}</p>
      <p><span class="badge ${worker.status === "Healthy" ? "healthy" : ""}">${worker.status}</span> Queue depth: ${worker.queueDepth}</p>
    </div>
  `).join("");
}

async function loadRag() {
  const data = await api("/api/system/rag");
  document.getElementById("ragDocs").innerHTML = data.documents.map(doc => `
    <div class="mini-card">
      <h4>${doc.title}</h4>
      <p>${doc.summary}</p>
      <p><span class="badge">${doc.sourceType}</span></p>
    </div>
  `).join("");
}

async function loadAuthz() {
  const data = await api("/api/authz");
  document.getElementById("authz").innerHTML = data.roles.slice(0, 6).map(role => `
    <div class="mini-card">
      <h4>${role.role}</h4>
      <p>${role.description}</p>
      <p>${role.scopes.slice(0, 3).join(", ")}</p>
    </div>
  `).join("");
}

async function initSandbox() {
  document.getElementById("sandboxRefresh").addEventListener("click", loadSandbox);
  document.getElementById("generateButton").addEventListener("click", generateSandbox);
  await loadSandbox();
}

async function loadSandbox() {
  const [providers, profiles] = await Promise.all([
    api("/api/sandbox/providers"),
    api("/api/sandbox/profiles?limit=80")
  ]);

  document.getElementById("providers").innerHTML = providers.map(provider => `
    <div class="mini-card">
      <h4>${provider.providerCode} | ${provider.name}</h4>
      <p><span class="badge ${provider.status === "Healthy" ? "healthy" : "high"}">${provider.status}</span> ${provider.mode}</p>
      <p>Latency ${provider.latencyMs}ms</p>
      <p>${provider.credentialSecretName}</p>
    </div>
  `).join("");

  const body = document.getElementById("profilesTable");
  body.innerHTML = profiles.items.map(profile => `
    <tr class="clickable" data-profile="${profile.id}">
      <td><strong>${profile.id}</strong></td>
      <td>${profile.fullName}<br><span class="subtle">${profile.cnicMasked}</span></td>
      <td>${profile.city}<br><span class="subtle">${profile.province}</span></td>
      <td><span class="badge ${riskClass(profile.expectedRiskBand)}">${profile.expectedRiskBand}</span></td>
      <td>${profile.vehicleCount} vehicles<br>${profile.propertyCount} properties<br>${profile.businessCount} businesses</td>
    </tr>
  `).join("");

  body.querySelectorAll("tr").forEach(row => {
    row.addEventListener("click", () => loadProfile(row.dataset.profile));
  });

  if (profiles.items.length) await loadProfile(profiles.items[0].id);
}

async function loadProfile(id) {
  const profile = await api(`/api/sandbox/profiles/${id}`);
  document.getElementById("profileHint").textContent = `${profile.person.fullName} | ${profile.person.city}`;
  document.getElementById("profileDetail").innerHTML = `
    <div class="stack">
      <div class="kv"><span>Identity token</span><strong>${profile.person.identityToken.value}</strong></div>
      <div class="kv"><span>Masked CNIC</span><strong>${profile.person.cnicMasked}</strong></div>
      <div class="kv"><span>Expected band</span><strong>${profile.person.expectedRiskBand}</strong></div>
      ${recordGroup("Tax", profile.tax)}
      ${recordGroup("Vehicles", profile.vehicles)}
      ${recordGroup("Properties", profile.properties)}
      ${recordGroup("Utilities", profile.utilities)}
      ${recordGroup("Businesses", profile.businesses)}
      ${recordGroup("Travel", profile.travel)}
    </div>
  `;
}

function recordGroup(title, records) {
  const items = Array.from(records || []);
  if (!items.length) return `<div class="reason"><strong>${title}</strong><p>No records.</p></div>`;
  return `
    <div class="reason">
      <strong>${title}</strong>
      <pre style="white-space:pre-wrap;margin:8px 0 0;color:#475467;font-family:ui-monospace,Consolas,monospace;font-size:12px">${escapeHtml(JSON.stringify(items, null, 2))}</pre>
    </div>
  `;
}

async function generateSandbox() {
  state.role = "taxnet-sandbox-admin";
  const request = {
    count: Number(document.getElementById("generateCount").value),
    suspiciousPercent: Number(document.getElementById("generateSuspicious").value),
    noisePercent: Number(document.getElementById("generateNoise").value)
  };
  const result = await api("/api/sandbox/admin/generate", {
    method: "POST",
    body: JSON.stringify(request)
  });
  document.getElementById("generateStatus").textContent = `${result.profiles} profiles generated, ${result.cases} cases flagged.`;
  await loadSandbox();
}

async function initCitizen() {
  const summary = await api("/api/citizen/me", { headers: { "X-Demo-Role": "taxnet-citizen" } });
  document.getElementById("citizenSummary").innerHTML = `
    <div class="stack">
      <div class="kv"><span>Name</span><strong>${summary.person.fullName}</strong></div>
      <div class="kv"><span>Masked CNIC</span><strong>${summary.person.cnicMasked}</strong></div>
      <div class="kv"><span>Risk band</span><strong><span class="badge ${riskClass(summary.riskBand)}">${summary.riskBand}</span></strong></div>
      <div class="reason"><strong>Safe explanation</strong><p>${summary.safeSummary}</p></div>
    </div>
  `;

  document.getElementById("correctionType").innerHTML = summary.correctionOptions.map(option => `<option value="${option}">${option}</option>`).join("");
  document.getElementById("submitCorrection").addEventListener("click", async () => {
    const payload = {
      caseId: "case-P001",
      correctionType: document.getElementById("correctionType").value,
      message: document.getElementById("correctionMessage").value,
      evidenceFileIds: ["demo-upload-placeholder"]
    };
    const result = await api("/api/citizen/corrections", {
      method: "POST",
      headers: { "X-Demo-Role": "taxnet-citizen" },
      body: JSON.stringify(payload)
    });
    document.getElementById("correctionStatus").innerHTML = `
      <strong>${result.status}</strong>
      <p>${result.message}</p>
      <p>${result.correctionId}</p>
    `;
  });
}

function escapeHtml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}
