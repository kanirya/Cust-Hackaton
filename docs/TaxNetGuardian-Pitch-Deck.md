# TaxNetGuardian — Hackathon Pitch Deck

---

## Slide 1: The Problem

**"Pakistan loses billions in tax revenue every year because government data is siloed."**

- FBR, Excise, Property registries, NADRA, utilities, travel records — all disconnected.
- Manual audits are slow, biased, and miss cross-domain patterns.
- Citizens have no transparency into why they are flagged.
- No system connects the dots **automatically, fairly, and explainably**.

---

## Slide 2: Our Solution

**"TaxNetGuardian is an AI-powered tax intelligence platform that connects siloed government data, resolves identities, builds knowledge graphs, scores risk, and generates auditable cases — all with human oversight and citizen rights built in."**

> "We don't replace auditors — we give them superpowers."

---

## Slide 3: Architecture Overview

```
Government Data Providers (FBR, Excise, Property, NADRA, Utilities, Travel)
		|
   [Ingestion Pipeline]           <-- Worker processes queue jobs
		|
   [Identity Resolution Engine]   <-- Jaro-Winkler weighted matching
		|
   [Knowledge Graph Builder]      <-- Person -> Vehicles, Properties, Businesses, Travel
		|
   [Risk Scoring Engine]          <-- Deterministic, evidence-based (NOT LLM-generated)
		|
   [Case Management]              <-- Human review required for closure
		|
   [AI Explainer + RAG Policy]    <-- Grounded in tax law, not hallucination
		|
   [Audit Report (PDF)]           <-- Confidential, citation-backed
```

### Key Architecture Points

- **.NET Aspire** orchestrates everything — Postgres, LocalStack (AWS), Workers, API
- **7 independent workers** process jobs asynchronously via SQS queues
- **Multi-LLM gateway** — supports OpenAI, DeepSeek, Gemini, Claude, and local Ollama
- **PostgreSQL** for operational storage, **knowledge graphs** for intelligence

---

## Slide 4: Key Technical Innovations

### 1. Weighted Identity Resolution (Jaro-Winkler)

We don't just match on CNIC. We use a 6-factor weighted algorithm:

| Factor | Weight |
|--------|--------|
| Strong identifier (token match) | 40% |
| Phone linkage | 20% |
| Name similarity (Jaro-Winkler) | 15% |
| Father name | 10% |
| Address/city | 10% |
| Province | 5% |

If confidence < 0.90, it goes to **human review** — never auto-decided.

### 2. Knowledge Graph

Each person becomes a graph node connected to vehicles, properties, businesses, travel, and utilities. We extract features like asset centrality and cross-domain coverage to feed risk scoring.

### 3. Deterministic Risk Scoring

LLMs do NOT create the score. The score comes from rules and evidence. LLMs only explain it. This makes the system auditable and legally defensible.

Score bands:
- Low: 0–30
- Medium: 31–60
- High: 61–80
- Critical: 81–100

### 4. RAG Policy Engine

When the AI explains a case, it retrieves actual Pakistan tax law and policy documents. Every explanation has citations — no hallucinations.

### 5. Multi-Provider Model Gateway

We support OpenAI, DeepSeek, Gemini, Claude, and local models. If one provider is down or restricted, the system falls back gracefully to deterministic templates.

---

## Slide 5: Security and Fairness

- **JWT authentication** (AWS Cognito) for production
- **Role-based access**: Admin, Auditor, Senior Auditor
- **Rate limiting** per user
- **Audit log** — every action is recorded immutably
- **Citizen portal** — citizens can submit corrections and see why they were flagged
- **Human-in-the-loop** — no case can be closed without authorized auditor action
- **PII masking** — CNIC is never stored in plain text in audit logs

---

## Slide 6: Live Demo Flow (2–3 minutes)

| Step | Endpoint | What to Say |
|------|----------|-------------|
| 1 | `GET /api/health` | "System is up and all services are healthy" |
| 2 | `POST /api/pipeline/run` | "Watch: data ingested, identities resolved, graphs built, risk scored — all in one call" |
| 3 | `GET /api/dashboard` | "120 citizens processed, X critical cases, estimated recoverable tax: Rs Y million" |
| 4 | `GET /api/cases` | "This person owns 3 luxury vehicles, 2 properties, but declared only Rs X income" |
| 5 | `GET /api/graph/{entityId}` | "Here is the knowledge graph — person connected to assets, businesses, travel" |
| 6 | `GET /api/ai/explain/{caseId}` | "The AI explains WHY with citations from Pakistan tax law" |
| 7 | `GET /api/cases/{id}/report/pdf` | "One click — a professional, confidential audit report with QuestPDF" |
| 8 | `POST /api/citizen/corrections` | "The citizen can dispute: I sold that car, here is proof" |

---

## Slide 7: What Makes Us Different

| Other Solutions | TaxNetGuardian |
|----------------|---------------|
| Black-box AI scoring | Deterministic + explainable |
| Single data source | 6+ government providers fused |
| No citizen rights | Citizen portal with corrections |
| No audit trail | Immutable audit log |
| Single LLM dependency | Multi-provider with offline fallback |
| Manual case creation | Automated pipeline with human review |

---

## Slide 8: Tech Stack

- **.NET 10** with Aspire orchestration
- **PostgreSQL** for operational data
- **LocalStack** (SQS, S3, Secrets Manager, Cognito)
- **QuestPDF** for report generation
- **Serilog** structured logging
- **Multi-LLM**: OpenAI / DeepSeek / Gemini / Claude / Ollama
- **React** frontend (Vite)

---

## Slide 9: Impact and Scale

- Processes 120+ citizens in seconds (demo scale)
- Designed for millions (queue-based, worker-partitioned)
- Estimated recoverable tax visible per case
- Every decision is traceable back to evidence

---

## Slide 10: Future Roadmap

- Real FBR/NADRA API integration
- pgvector for semantic search at scale
- Real-time streaming with Kafka
- Mobile citizen app
- Multi-tenant for provincial tax authorities

---

## Presentation Tips

1. **Start with the problem, not the tech.** Judges care about impact first.
2. **Demo one flow end-to-end.** Do not jump around.
3. **Show the PDF report.** It is a visual "wow" moment.
4. **Emphasize "human-in-the-loop."** Judges love responsible AI.
5. **Say "LLMs don't score — they explain."** This is your strongest differentiator.
6. **Keep it under 5 minutes.** Rehearse to exactly 4:30.

---

## One-Line Pitch

> "TaxNetGuardian turns messy, siloed government signals into explainable, auditable tax intelligence cases — with AI that explains but never decides."

---

## Team Talking Points

If judges ask questions:

- **"Is AI making the decision?"** — No. Risk scoring is deterministic. AI only explains and cites policy.
- **"What about privacy?"** — CNIC is masked, audit logs exclude PII, citizen corrections are supported.
- **"How does it scale?"** — Queue-based workers, each service is independent, PostgreSQL for persistence.
- **"What if the LLM is wrong?"** — Guardrails: structured evidence IDs, policy citations, human-review warnings. No "fraud proven" language is ever generated.
- **"Is this real data?"** — Demo uses synthetic data that mirrors real Pakistani tax scenarios. The architecture is provider-agnostic and ready for real integrations.
