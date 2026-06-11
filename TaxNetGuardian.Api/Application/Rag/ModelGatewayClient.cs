using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TaxNetGuardian.Api;

public sealed class ModelGatewayClient
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ModelGatewayConfig GetConfig()
    {
        var defaultProvider = Environment.GetEnvironmentVariable("MODEL_GATEWAY_DEFAULT_PROVIDER") ?? "auto";
        return new ModelGatewayConfig(defaultProvider, ProviderStatuses());
    }

    public async Task<ModelGatewayProviderResult> InvokeAsync(
        string preferredProvider,
        bool allowExternalProvider,
        string taskType,
        string prompt,
        IReadOnlyList<RagChunk> contextChunks,
        CancellationToken cancellationToken = default)
    {
        var selected = SelectProvider(preferredProvider, allowExternalProvider);
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

    private ProviderSelection SelectProvider(string preferredProvider, bool allowExternalProvider)
    {
        if (!allowExternalProvider)
        {
            return ProviderSelection.Fallback("external provider disabled");
        }

        var providers = new[]
        {
            ProviderSelection.OpenAi(),
            ProviderSelection.DeepSeek(),
            ProviderSelection.Gemini(),
            ProviderSelection.Claude()
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

    private IReadOnlyList<ModelProviderStatus> ProviderStatuses()
    {
        var providers = new[]
        {
            ProviderSelection.OpenAi(),
            ProviderSelection.DeepSeek(),
            ProviderSelection.Gemini(),
            ProviderSelection.Claude()
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
        public static ProviderSelection OpenAi()
            => new("openai", Has("OPENAI_API_KEY"), Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "", Environment.GetEnvironmentVariable("OPENAI_API_BASE_URL") ?? "https://api.openai.com/v1/chat/completions", Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini", "openai-chat-completions");

        public static ProviderSelection DeepSeek()
            => new("deepseek", Has("DEEPSEEK_API_KEY"), Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "", Environment.GetEnvironmentVariable("DEEPSEEK_API_BASE_URL") ?? "https://api.deepseek.com/chat/completions", Environment.GetEnvironmentVariable("DEEPSEEK_MODEL") ?? "deepseek-chat", "deepseek-openai-compatible");

        public static ProviderSelection Gemini()
            => new("gemini", Has("GEMINI_API_KEY"), Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "", Environment.GetEnvironmentVariable("GEMINI_API_BASE_URL") ?? "https://generativelanguage.googleapis.com", Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-1.5-flash", "gemini-generate-content");

        public static ProviderSelection Claude()
            => new("claude", Has("CLAUDE_API_KEY"), Environment.GetEnvironmentVariable("CLAUDE_API_KEY") ?? "", Environment.GetEnvironmentVariable("CLAUDE_API_BASE_URL") ?? "https://api.anthropic.com/v1/messages", Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "claude-3-5-haiku-latest", "claude-messages");

        public static ProviderSelection Fallback(string reason)
            => new("deterministic-template", true, "", "offline://template", "taxnet-template-v1", $"offline-template: {reason}");

        private static bool Has(string name)
            => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));
    }
}
