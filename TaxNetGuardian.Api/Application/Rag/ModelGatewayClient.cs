using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TaxNetGuardian.Api;

public sealed class ModelGatewayClient
{
    private readonly ISecretProvider _secretProvider;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ModelGatewayClient()
        : this(new LocalStackSecretsManagerSecretProvider())
    {
    }

    public ModelGatewayClient(ISecretProvider secretProvider)
    {
        _secretProvider = secretProvider;
    }

    /// <summary>
    /// Global privacy off-switch for live model providers. External calls are allowed by
    /// default; set MODEL_GATEWAY_ALLOW_EXTERNAL=false (or 0/no) to force deterministic-only
    /// routing without code changes. The supplied <paramref name="requested"/> flag is ANDed in,
    /// so an individual caller can still opt out per request.
    /// </summary>
    public static bool ExternalProvidersAllowed(bool requested = true)
    {
        var raw = Environment.GetEnvironmentVariable("MODEL_GATEWAY_ALLOW_EXTERNAL");
        var globallyAllowed = string.IsNullOrWhiteSpace(raw)
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
        return globallyAllowed && requested;
    }

    public ModelGatewayConfig GetConfig()
        => GetConfigAsync().GetAwaiter().GetResult();

    public async Task<ModelGatewayConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var defaultProvider = Environment.GetEnvironmentVariable("MODEL_GATEWAY_DEFAULT_PROVIDER") ?? "auto";
        return new ModelGatewayConfig(defaultProvider, await ProviderStatusesAsync(cancellationToken));
    }

    public async Task<ModelProviderKeyUpdateResult> ConfigureProviderKeyAsync(
        string provider,
        ModelProviderKeyUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var metadata = ProviderSecretMetadata(normalizedProvider);
        if (metadata is null)
        {
            return new ModelProviderKeyUpdateResult(provider, "", false, false, null, $"Unsupported provider '{provider}'.");
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return new ModelProviderKeyUpdateResult(normalizedProvider, metadata.Value.SecretName, false, false, null, "API key was empty.");
        }

        if (_secretProvider is not IWritableSecretProvider writable)
        {
            return new ModelProviderKeyUpdateResult(normalizedProvider, metadata.Value.SecretName, false, false, null, "Configured secret provider is read-only.");
        }

        var secretPayload = JsonSerializer.Serialize(new
        {
            provider = normalizedProvider,
            apiKey = request.ApiKey.Trim(),
            model = string.IsNullOrWhiteSpace(request.Model) ? metadata.Value.DefaultModel : request.Model.Trim(),
            updatedAtUtc = DateTimeOffset.UtcNow,
            source = "TaxNetGuardian.Api"
        }, _jsonOptions);

        var result = await writable.PutSecretStringAsync(metadata.Value.SecretName, secretPayload, cancellationToken);
        return new ModelProviderKeyUpdateResult(
            normalizedProvider,
            metadata.Value.SecretName,
            result.Succeeded,
            result.Succeeded,
            result.VersionId,
            result.Succeeded ? null : result.Error);
    }

    public async Task<IReadOnlyList<ModelSecretDiagnostic>> GetSecretDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var secrets = new[]
        {
            ("openai", "OPENAI_API_KEY", "taxnet/dev/model-gateway/openai"),
            ("deepseek", "DEEPSEEK_API_KEY", "taxnet/dev/model-gateway/deepseek"),
            ("gemini", "GEMINI_API_KEY", "taxnet/dev/model-gateway/gemini"),
            ("claude", "CLAUDE_API_KEY", "taxnet/dev/model-gateway/claude")
        };

        var diagnostics = new List<ModelSecretDiagnostic>();
        foreach (var (provider, environmentVariable, secretName) in secrets)
        {
            var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
            var secretRead = _secretProvider switch
            {
                LocalStackSecretsManagerSecretProvider localStackSecrets => await localStackSecrets.GetSecretAsync(secretName, cancellationToken),
                AwsSecretsManagerSecretProvider awsSecrets => await awsSecrets.GetSecretAsync(secretName, cancellationToken),
                _ => new SecretReadResult(secretName, false, await _secretProvider.GetSecretStringAsync(secretName, cancellationToken), "", null, null)
            };
            var secretString = secretRead.SecretString;
            diagnostics.Add(new ModelSecretDiagnostic(
                provider,
                environmentVariable,
                secretName,
                !string.IsNullOrWhiteSpace(environmentValue),
                !string.IsNullOrWhiteSpace(secretString),
                secretString?.Length ?? 0,
                !string.IsNullOrWhiteSpace(secretString) && !string.IsNullOrWhiteSpace(ExtractApiKey(secretString)),
                secretRead.Endpoint,
                secretRead.StatusCode,
                secretRead.Error is null ? null : secretRead.Error.Length <= 180 ? secretRead.Error : secretRead.Error[..180]));
        }

        return diagnostics;
    }

    public async Task<ModelGatewayProviderResult> InvokeAsync(
        string preferredProvider,
        bool allowExternalProvider,
        string taskType,
        string prompt,
        IReadOnlyList<RagChunk> contextChunks,
        CancellationToken cancellationToken = default)
    {
        var selected = await SelectProviderAsync(preferredProvider, allowExternalProvider, cancellationToken);
        if (selected.Provider.Equals("deterministic-template", StringComparison.OrdinalIgnoreCase))
        {
            return new ModelGatewayProviderResult("Deterministic template fallback", selected.Route, selected.Model, "", false, "No external provider selected or configured.");
        }

        var systemPrompt = BuildSystemPrompt(taskType);
        var groundedPrompt = BuildGroundedPrompt(prompt, contextChunks);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            return selected.Provider.ToLowerInvariant() switch
            {
                "openai" => await InvokeOpenAiCompatibleAsync(http, selected, systemPrompt, groundedPrompt, cancellationToken),
                "deepseek" => await InvokeOpenAiCompatibleAsync(http, selected, systemPrompt, groundedPrompt, cancellationToken),
                "ollama" => await InvokeOpenAiCompatibleAsync(http, selected, systemPrompt, groundedPrompt, cancellationToken),
                "gemini" => await InvokeGeminiAsync(http, selected, systemPrompt, groundedPrompt, cancellationToken),
                "claude" => await InvokeClaudeAsync(http, selected, systemPrompt, groundedPrompt, cancellationToken),
                _ => new ModelGatewayProviderResult("Deterministic template fallback", "offline-template", "deterministic-template", "", false, $"Unsupported provider {selected.Provider}.")
            };
        }
        catch (Exception ex)
        {
            return new ModelGatewayProviderResult(selected.Provider, selected.Route, selected.Model, "", false, ex.Message);
        }
    }

    // Streams an OpenAI-compatible chat completion (Ollama / vLLM / LM Studio) token-by-token.
    private async IAsyncEnumerable<string> StreamOpenAiCompatibleAsync(
        ProviderSelection selected,
        string systemPrompt,
        string groundedPrompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
        using var request = new HttpRequestMessage(HttpMethod.Post, selected.Endpoint);
        if (!string.IsNullOrWhiteSpace(selected.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", selected.ApiKey);
        }

        request.Content = JsonContent(new
        {
            model = selected.Model,
            temperature = 0.2,
            stream = true,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = groundedPrompt }
            }
        });

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line[5..].Trim();
            if (payload is "[DONE]")
            {
                break;
            }

            string? piece = null;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var content))
                {
                    piece = content.GetString();
                }
            }
            catch (JsonException)
            {
                piece = null;
            }

            if (!string.IsNullOrEmpty(piece))
            {
                yield return piece;
            }
        }
    }

    private async Task<ProviderSelection> SelectProviderAsync(string preferredProvider, bool allowExternalProvider, CancellationToken cancellationToken)
    {
        if (!ExternalProvidersAllowed(allowExternalProvider))
        {
            return ProviderSelection.Fallback("external provider disabled");
        }

        var providers = new[]
        {
            await OpenAiAsync(cancellationToken),
            await DeepSeekAsync(cancellationToken),
            await GeminiAsync(cancellationToken),
            await ClaudeAsync(cancellationToken),
            await OllamaAsync(cancellationToken)
        };

        if (!string.IsNullOrWhiteSpace(preferredProvider) && !preferredProvider.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var requested = providers.FirstOrDefault(x => x.Provider.Equals(preferredProvider, StringComparison.OrdinalIgnoreCase));
            return requested is not null && requested.HasKey ? requested : ProviderSelection.Fallback($"requested provider {preferredProvider} has no configured key");
        }

        var defaultProvider = Environment.GetEnvironmentVariable("MODEL_GATEWAY_DEFAULT_PROVIDER");
        if (!string.IsNullOrWhiteSpace(defaultProvider) && !defaultProvider.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var configured = providers.FirstOrDefault(x => x.Provider.Equals(defaultProvider, StringComparison.OrdinalIgnoreCase));
            if (configured is not null && configured.HasKey)
            {
                return configured;
            }
        }

        return providers.FirstOrDefault(x => x.HasKey) ?? ProviderSelection.Fallback("no provider keys configured");
    }

    private async Task<IReadOnlyList<ModelProviderStatus>> ProviderStatusesAsync(CancellationToken cancellationToken)
    {
        var providers = new[]
        {
            await OpenAiAsync(cancellationToken),
            await DeepSeekAsync(cancellationToken),
            await GeminiAsync(cancellationToken),
            await ClaudeAsync(cancellationToken),
            await OllamaAsync(cancellationToken)
        };

        return providers
            .Select(x => new ModelProviderStatus(x.Provider, x.HasKey, x.Route, x.Model, x.HasKey))
            .Append(new ModelProviderStatus("deterministic-template", true, "offline-template", "taxnet-template-v1", true))
            .ToArray();
    }

    private async Task<ModelGatewayProviderResult> InvokeOpenAiCompatibleAsync(HttpClient http, ProviderSelection selected, string systemPrompt, string prompt, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, selected.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", selected.ApiKey);
        request.Content = JsonContent(new
        {
            model = selected.Model,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = prompt }
            }
        });

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new ModelGatewayProviderResult(selected.Provider, selected.Route, selected.Model, "", false, $"HTTP {(int)response.StatusCode}: {body}");
        }

        using var json = JsonDocument.Parse(body);
        var output = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        return new ModelGatewayProviderResult(selected.Provider, selected.Route, selected.Model, output, true, null);
    }

    private async Task<ModelGatewayProviderResult> InvokeGeminiAsync(HttpClient http, ProviderSelection selected, string systemPrompt, string prompt, CancellationToken cancellationToken)
    {
        var endpoint = $"{selected.Endpoint.TrimEnd('/')}/v1beta/models/{selected.Model}:generateContent?key={Uri.EscapeDataString(selected.ApiKey)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = JsonContent(new
        {
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
            generationConfig = new { temperature = 0.2 }
        });

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new ModelGatewayProviderResult(selected.Provider, selected.Route, selected.Model, "", false, $"HTTP {(int)response.StatusCode}: {body}");
        }

        using var json = JsonDocument.Parse(body);
        var output = json.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
        return new ModelGatewayProviderResult(selected.Provider, selected.Route, selected.Model, output, true, null);
    }

    private async Task<ModelGatewayProviderResult> InvokeClaudeAsync(HttpClient http, ProviderSelection selected, string systemPrompt, string prompt, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, selected.Endpoint);
        request.Headers.Add("x-api-key", selected.ApiKey);
        request.Headers.Add("anthropic-version", Environment.GetEnvironmentVariable("CLAUDE_API_VERSION") ?? "2023-06-01");
        request.Content = JsonContent(new
        {
            model = selected.Model,
            max_tokens = 3000,
            temperature = 0.2,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = prompt } }
        });

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new ModelGatewayProviderResult(selected.Provider, selected.Route, selected.Model, "", false, $"HTTP {(int)response.StatusCode}: {body}");
        }

        using var json = JsonDocument.Parse(body);
        var output = json.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
        return new ModelGatewayProviderResult(selected.Provider, selected.Route, selected.Model, output, true, null);
    }

    private StringContent JsonContent(object value)
        => new(JsonSerializer.Serialize(value, _jsonOptions), Encoding.UTF8, "application/json");

    // Streams model output token-by-token. For Claude it parses Anthropic's SSE
    // (content_block_delta) and yields text deltas. For any other/unconfigured provider it
    // yields nothing, so callers fall back to streaming their deterministic text themselves.
    public async IAsyncEnumerable<string> StreamAsync(
        string preferredProvider,
        bool allowExternalProvider,
        string taskType,
        string prompt,
        IReadOnlyList<RagChunk> contextChunks,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var selected = await SelectProviderAsync(preferredProvider, allowExternalProvider, cancellationToken);
        var systemPrompt = BuildSystemPrompt(taskType);
        var groundedPrompt = BuildGroundedPrompt(prompt, contextChunks);

        // Local fine-tuned model (Ollama / OpenAI-compatible) streaming.
        if (selected.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            await foreach (var piece in StreamOpenAiCompatibleAsync(selected, systemPrompt, groundedPrompt, cancellationToken))
            {
                yield return piece;
            }

            yield break;
        }

        if (!selected.Provider.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        using var request = new HttpRequestMessage(HttpMethod.Post, selected.Endpoint);
        request.Headers.Add("x-api-key", selected.ApiKey);
        request.Headers.Add("anthropic-version", Environment.GetEnvironmentVariable("CLAUDE_API_VERSION") ?? "2023-06-01");
        request.Content = JsonContent(new
        {
            model = selected.Model,
            max_tokens = 3000,
            temperature = 0.2,
            system = systemPrompt,
            stream = true,
            messages = new[] { new { role = "user", content = groundedPrompt } }
        });

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line[5..].Trim();
            if (payload is "[DONE]")
            {
                break;
            }

            string? piece = null;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("type", out var type) &&
                    type.GetString() == "content_block_delta" &&
                    doc.RootElement.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("text", out var text))
                {
                    piece = text.GetString();
                }
            }
            catch (JsonException)
            {
                piece = null;
            }

            if (!string.IsNullOrEmpty(piece))
            {
                yield return piece;
            }
        }
    }

    private static string BuildSystemPrompt(string taskType)
    {
        // Shared analyst charter: domain framing, evidence discipline, calibrated language, safeguards.
        const string baseCharter = """
            You are TaxNet Guardian's senior tax-compliance intelligence analyst, supporting auditors at
            a Pakistani revenue authority (FBR context). You reason over consolidated records linked by
            CNIC — Pakistan's stable national identity number — across tax filings (filer/non-filer, NTN,
            declared income), vehicle registration (Excise), property, utility consumption, SECP business
            directorships, and travel history.

            Operating principles:
            - Ground every claim strictly in the supplied evidence, signals, and RAG policy context. Never
              invent figures, records, names, or laws. If the data is insufficient, say so plainly.
            - Quantify. When you cite a mismatch, state the concrete numbers (e.g. declared income vs.
              asset value vs. utility spend) and the magnitude of the gap.
            - Use calibrated, professional language. These are risk indicators that require human review,
              not proof. Never state or imply that fraud, evasion, or guilt is proven or certain.
            - Reference the relevant evidence IDs and policy citations (use the [n] markers from the RAG
              context) inline where they support a point.
            - Respect privacy: treat CNIC and identity tokens as sensitive; refer to masked identifiers.
            - Always preserve the human-in-the-loop and citizen-correction safeguards.

            Formatting: write clean, well-structured Markdown with short section headings and tight bullet
            points. Be thorough but not verbose — every sentence should carry analytic weight.
            """;

        var taskGuidance = taskType?.Trim().ToLowerInvariant() switch
        {
            "cnicinvestigation" => """
                Task: CNIC-linked investigation. Assess all records sharing the subject's CNIC together,
                even when names differ across systems. Sections: **Assessment**, **CNIC linkage**,
                **Key mismatches** (quantified), **Evidence to verify**, **Recommended next steps**,
                **Human review note**.
                """,
            "auditexplanation" => """
                Task: Auditor explanation. Explain why the case was flagged and what drives the risk score.
                Sections: **Summary**, **Risk drivers** (each tied to evidence), **What to verify**,
                **Recommended action**, **Human review note**.
                """,
            "citizenexplanation" => """
                Task: Citizen-facing explanation. Use plain, respectful, non-accusatory language a taxpayer
                can understand. Explain which records appear inconsistent, that this is under review (not a
                finding of wrongdoing), and how to submit a correction. Avoid internal jargon and scores.
                """,
            "reportdraft" => """
                Task: Formal audit report draft. Produce a structured, citable document. Sections:
                **Executive summary**, **Subject & linkage**, **Findings** (each with evidence IDs &
                citations), **Recommended disposition**, **Limitations & human-review disclaimer**.
                """,
            "policyquestion" => """
                Task: Policy question. Answer strictly from the provided policy context and cite the [n]
                sources. If the context does not cover the question, say so rather than guessing.
                """,
            _ => $"Task: {taskType}. Apply the operating principles above and structure the answer clearly."
        };

        return baseCharter + "\n\n" + taskGuidance;
    }

    private static string BuildGroundedPrompt(string prompt, IReadOnlyList<RagChunk> contextChunks)
    {
        if (contextChunks.Count == 0)
        {
            return $"""
                   {prompt}

                   RAG policy context: (none retrieved — rely only on the evidence and signals above, and
                   do not fabricate citations.)
                   """;
        }

        var context = string.Join("\n\n", contextChunks.Take(6).Select((chunk, index) => $"[{index + 1}] {chunk.Title} ({chunk.Citation.SourceType}, {chunk.Citation.ChunkId})\n{chunk.Text}"));
        return $"""
               {prompt}

               RAG policy context (cite these as [n] where relevant):
               {context}
               """;
    }

    private sealed record ProviderSelection(string Provider, bool HasKey, string ApiKey, string Endpoint, string Model, string Route)
    {
        public static ProviderSelection Fallback(string reason)
            => new("deterministic-template", true, "", "offline://template", "taxnet-template-v1", $"offline-template: {reason}");
    }

    private async Task<ProviderSelection> OpenAiAsync(CancellationToken cancellationToken)
    {
        var credentials = await ResolveProviderCredentialsAsync("OPENAI_API_KEY", "taxnet/dev/model-gateway/openai", cancellationToken);
        return new(
            "openai",
            !string.IsNullOrWhiteSpace(credentials.ApiKey),
            credentials.ApiKey ?? "",
            Environment.GetEnvironmentVariable("OPENAI_API_BASE_URL") ?? "https://api.openai.com/v1/chat/completions",
            credentials.Model ?? Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini",
            "openai-chat-completions");
    }

    private async Task<ProviderSelection> DeepSeekAsync(CancellationToken cancellationToken)
    {
        var credentials = await ResolveProviderCredentialsAsync("DEEPSEEK_API_KEY", "taxnet/dev/model-gateway/deepseek", cancellationToken);
        return new(
            "deepseek",
            !string.IsNullOrWhiteSpace(credentials.ApiKey),
            credentials.ApiKey ?? "",
            Environment.GetEnvironmentVariable("DEEPSEEK_API_BASE_URL") ?? "https://api.deepseek.com/chat/completions",
            credentials.Model ?? Environment.GetEnvironmentVariable("DEEPSEEK_MODEL") ?? "deepseek-chat",
            "deepseek-openai-compatible");
    }

    private async Task<ProviderSelection> GeminiAsync(CancellationToken cancellationToken)
    {
        var credentials = await ResolveProviderCredentialsAsync("GEMINI_API_KEY", "taxnet/dev/model-gateway/gemini", cancellationToken);
        return new(
            "gemini",
            !string.IsNullOrWhiteSpace(credentials.ApiKey),
            credentials.ApiKey ?? "",
            Environment.GetEnvironmentVariable("GEMINI_API_BASE_URL") ?? "https://generativelanguage.googleapis.com",
            credentials.Model ?? Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-1.5-flash",
            "gemini-generate-content");
    }

    private async Task<ProviderSelection> ClaudeAsync(CancellationToken cancellationToken)
    {
        var credentials = await ResolveProviderCredentialsAsync("CLAUDE_API_KEY", "taxnet/dev/model-gateway/claude", cancellationToken);
        return new(
            "claude",
            !string.IsNullOrWhiteSpace(credentials.ApiKey),
            credentials.ApiKey ?? "",
            Environment.GetEnvironmentVariable("CLAUDE_API_BASE_URL") ?? "https://api.anthropic.com/v1/messages",
            credentials.Model ?? Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "claude-haiku-4-5-20251001",
            "claude-messages");
    }

    // Local fine-tuned model served by Ollama (or any OpenAI-compatible local server such as vLLM
    // or LM Studio). This is the "our own LLM" path: train a small base model on the exported
    // teacher data, register it in Ollama, and route to it here. It is opt-in via OLLAMA_ENABLED so
    // it never hijacks auto-routing; no API key is required for a local server.
    private Task<ProviderSelection> OllamaAsync(CancellationToken cancellationToken)
    {
        var enabledRaw = Environment.GetEnvironmentVariable("OLLAMA_ENABLED");
        var enabled = !string.IsNullOrWhiteSpace(enabledRaw)
            && (enabledRaw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || enabledRaw == "1"
                || enabledRaw.Equals("yes", StringComparison.OrdinalIgnoreCase));
        var baseUrl = (Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434/v1").TrimEnd('/');
        var endpoint = baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/chat/completions";
        var model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "taxnet-guardian";
        return Task.FromResult(new ProviderSelection(
            "ollama",
            enabled,
            "ollama-local",
            endpoint,
            model,
            "ollama-openai-compatible"));
    }

    private async Task<string?> ResolveApiKeyAsync(string environmentVariable, string secretName, CancellationToken cancellationToken)
        => (await ResolveProviderCredentialsAsync(environmentVariable, secretName, cancellationToken)).ApiKey;

    private async Task<(string? ApiKey, string? Model)> ResolveProviderCredentialsAsync(string environmentVariable, string secretName, CancellationToken cancellationToken)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return (environmentValue, null);
        }

        var secretString = await _secretProvider.GetSecretStringAsync(secretName, cancellationToken);
        if (string.IsNullOrWhiteSpace(secretString))
        {
            return (null, null);
        }

        return (ExtractApiKey(secretString), ExtractModel(secretString));
    }

    private static string? ExtractApiKey(string secretString)
    {
        var trimmed = secretString.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }

        try
        {
            using var json = JsonDocument.Parse(trimmed);
            foreach (var name in new[] { "apiKey", "api_key", "key", "value", "secret" })
            {
                if (json.RootElement.TryGetProperty(name, out var property))
                {
                    var value = property.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? ExtractModel(string secretString)
    {
        var trimmed = secretString.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(trimmed);
            foreach (var name in new[] { "model", "modelName", "deployment" })
            {
                if (json.RootElement.TryGetProperty(name, out var property))
                {
                    var value = property.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string NormalizeProvider(string provider)
        => (provider ?? "").Trim().ToLowerInvariant() switch
        {
            "openai" or "open-ai" => "openai",
            "deepseek" or "deep-seek" => "deepseek",
            "gemini" or "google" => "gemini",
            "claude" or "anthropic" => "claude",
            "ollama" or "local" or "taxnet" or "custom" => "ollama",
            var value => value
        };

    private static (string SecretName, string DefaultModel)? ProviderSecretMetadata(string provider)
        => provider switch
        {
            "openai" => ("taxnet/dev/model-gateway/openai", Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini"),
            "deepseek" => ("taxnet/dev/model-gateway/deepseek", Environment.GetEnvironmentVariable("DEEPSEEK_MODEL") ?? "deepseek-chat"),
            "gemini" => ("taxnet/dev/model-gateway/gemini", Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-1.5-flash"),
            "claude" => ("taxnet/dev/model-gateway/claude", Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "claude-haiku-4-5-20251001"),
            _ => null
        };
}

public sealed record ModelProviderKeyUpdateRequest(string ApiKey, string? Model);

public sealed record ModelProviderKeyUpdateResult(
    string Provider,
    string SecretName,
    bool Succeeded,
    bool AvailableForRouting,
    string? VersionId,
    string? Error);

public sealed record ModelSecretDiagnostic(
    string Provider,
    string EnvironmentVariable,
    string SecretName,
    bool HasEnvironmentValue,
    bool SecretReadable,
    int SecretStringLength,
    bool SecretHasApiKey,
    string Endpoint,
    int? StatusCode,
    string? Error);

