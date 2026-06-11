# Model Gateway and RAG Runtime

TaxNet Guardian now supports real-time provider routing for OpenAI, DeepSeek, Gemini, and Claude. If no key is configured, the gateway stays runnable through the deterministic template fallback.

## Provider Environment Variables

```powershell
# Provider selection
$env:MODEL_GATEWAY_DEFAULT_PROVIDER="auto" # auto, openai, deepseek, gemini, claude

# OpenAI
$env:OPENAI_API_KEY="..."
$env:OPENAI_MODEL="gpt-4o-mini"
$env:OPENAI_API_BASE_URL="https://api.openai.com/v1/chat/completions"

# DeepSeek
$env:DEEPSEEK_API_KEY="..."
$env:DEEPSEEK_MODEL="deepseek-chat"
$env:DEEPSEEK_API_BASE_URL="https://api.deepseek.com/chat/completions"

# Gemini
$env:GEMINI_API_KEY="..."
$env:GEMINI_MODEL="gemini-1.5-flash"
$env:GEMINI_API_BASE_URL="https://generativelanguage.googleapis.com"

# Claude
$env:CLAUDE_API_KEY="..."
$env:CLAUDE_MODEL="claude-3-5-haiku-latest"
$env:CLAUDE_API_BASE_URL="https://api.anthropic.com/v1/messages"
$env:CLAUDE_API_VERSION="2023-06-01"
```

## Runtime Behavior

- `GET /api/system/model-gateway` shows provider key availability and selected model names.
- `POST /api/system/model-gateway/invoke` accepts `preferredProvider` and `allowExternalProvider`.
- If `allowExternalProvider` is `false`, the local deterministic route is used.
- If `allowExternalProvider` is `true`, the gateway chooses the requested provider when its key exists.
- If provider call fails, the gateway records the error and returns a deterministic fallback with RAG citations.

## RAG

RAG retrieval now uses a hybrid score:

- keyword overlap
- phrase match
- source type boost
- tag boost
- recency boost
- chunk quality score

RAG calls return quality checks describing the retrieval path and top score.

## Demo Bootstrap

```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5191/api/demo/bootstrap" `
  -Method Post `
  -Headers @{ "X-Demo-Role"="taxnet-admin"; "X-Demo-User"="demo" }
```

This seeds sandbox data, indexes policy RAG content, and enqueues worker messages.
