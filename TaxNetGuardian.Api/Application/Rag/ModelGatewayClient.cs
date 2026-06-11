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

    public ModelGatewayConfig GetConfig()
        => GetConfigAsync().GetAwaiter().GetResult();

    public async Task<ModelGatewayConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var defaultProvider = Environment.GetEnvironmentVariable("MODEL_GATEWAY_DEFAULT_PROVIDER") ?? "auto";
        return new ModelGatewayConfig(defaultProvider, await ProviderStatusesAsync(cancellationToken));
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
            var secretRead = _secretProvider is LocalStackSecretsManagerSecretProvider localStackSecrets
                ? await localStackSecrets.GetSecretAsync(secretName, cancellationToken)
                : new SecretReadResult(secretName, false, await _secretProvider.GetSecretStringAsync(secretName, cancellationToken), "", null, null);
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
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            return selected.Provider.ToLowerInvariant() switch
            {
                "openai" => await InvokeOpenAiCompatibleAsync(http, selected, systemPrompt, groundedPrompt, cancellationToken),
                "deepseek" => await InvokeOpenAiCompatibleAsync(http, selected, systemPrompt, groundedPrompt, cancellationToken),
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

    private async Task<ProviderSelection> SelectProviderAsync(string preferredProvider, bool allowExternalProvider, CancellationToken cancellationToken)
    {
        if (!allowExternalProvider)
        {
            return ProviderSelection.Fallback("external provider disabled");
        }

        var providers = new[]
        {
            await OpenAiAsync(cancellationToken),
            await DeepSeekAsync(cancellationToken),
            await GeminiAsync(cancellationToken),
            await ClaudeAsync(cancellationToken)
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
            await ClaudeAsync(cancellationToken)
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
            max_tokens = 900,
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

    private static string BuildSystemPrompt(string taskType)
        => $"""
           You are TaxNet Guardian's model gateway for {taskType}.
           Use only provided evidence and RAG context.
           Do not claim fraud is proven.
           Include human review and citizen correction safeguards.
           Keep explanations concise, auditable, and citation-aware.
           """;

    private static string BuildGroundedPrompt(string prompt, IReadOnlyList<RagChunk> contextChunks)
    {
        var context = string.Join("\n\n", contextChunks.Take(6).Select((chunk, index) => $"[{index + 1}] {chunk.Title} ({chunk.Citation.SourceType}, {chunk.Citation.ChunkId})\n{chunk.Text}"));
        return $"""
               User prompt:
               {prompt}

               RAG context:
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
        var key = await ResolveApiKeyAsync("OPENAI_API_KEY", "taxnet/dev/model-gateway/openai", cancellationToken);
        return new(
            "openai",
            !string.IsNullOrWhiteSpace(key),
            key ?? "",
            Environment.GetEnvironmentVariable("OPENAI_API_BASE_URL") ?? "https://api.openai.com/v1/chat/completions",
            Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini",
            "openai-chat-completions");
    }

    private async Task<ProviderSelection> DeepSeekAsync(CancellationToken cancellationToken)
    {
        var key = await ResolveApiKeyAsync("DEEPSEEK_API_KEY", "taxnet/dev/model-gateway/deepseek", cancellationToken);
        return new(
            "deepseek",
            !string.IsNullOrWhiteSpace(key),
            key ?? "",
            Environment.GetEnvironmentVariable("DEEPSEEK_API_BASE_URL") ?? "https://api.deepseek.com/chat/completions",
            Environment.GetEnvironmentVariable("DEEPSEEK_MODEL") ?? "deepseek-chat",
            "deepseek-openai-compatible");
    }

    private async Task<ProviderSelection> GeminiAsync(CancellationToken cancellationToken)
    {
        var key = await ResolveApiKeyAsync("GEMINI_API_KEY", "taxnet/dev/model-gateway/gemini", cancellationToken);
        return new(
            "gemini",
            !string.IsNullOrWhiteSpace(key),
            key ?? "",
            Environment.GetEnvironmentVariable("GEMINI_API_BASE_URL") ?? "https://generativelanguage.googleapis.com",
            Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-1.5-flash",
            "gemini-generate-content");
    }

    private async Task<ProviderSelection> ClaudeAsync(CancellationToken cancellationToken)
    {
        var key = await ResolveApiKeyAsync("CLAUDE_API_KEY", "taxnet/dev/model-gateway/claude", cancellationToken);
        return new(
            "claude",
            !string.IsNullOrWhiteSpace(key),
            key ?? "",
            Environment.GetEnvironmentVariable("CLAUDE_API_BASE_URL") ?? "https://api.anthropic.com/v1/messages",
            Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "claude-3-5-haiku-latest",
            "claude-messages");
    }

    private async Task<string?> ResolveApiKeyAsync(string environmentVariable, string secretName, CancellationToken cancellationToken)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        var secretString = await _secretProvider.GetSecretStringAsync(secretName, cancellationToken);
        if (string.IsNullOrWhiteSpace(secretString))
        {
            return null;
        }

        return ExtractApiKey(secretString);
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
}

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
