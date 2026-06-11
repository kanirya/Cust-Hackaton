using System.Text;
using System.Text.Json;

namespace TaxNetGuardian.Api;

public sealed class LocalStackSecretsManagerSecretProvider : ISecretProvider
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string?> GetSecretStringAsync(string secretName, CancellationToken cancellationToken = default)
        => (await GetSecretAsync(secretName, cancellationToken)).SecretString;

    public async Task<SecretReadResult> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return new SecretReadResult(secretName, false, null, "", null, "Secret name was empty.");
        }

        var endpoint = Environment.GetEnvironmentVariable("LOCALSTACK_ENDPOINT") ?? "http://localhost:4566";
        var mode = Environment.GetEnvironmentVariable("TAXNET_SECRET_PROVIDER") ?? Environment.GetEnvironmentVariable("SECRET_PROVIDER_MODE");
        if (string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return new SecretReadResult(secretName, false, null, endpoint, null, "Secret provider disabled.");
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.TryAddWithoutValidation("X-Amz-Target", "secretsmanager.GetSecretValue");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, string> { ["SecretId"] = secretName }, _jsonOptions),
                Encoding.UTF8,
                "application/x-amz-json-1.1");

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var failureBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return new SecretReadResult(secretName, false, null, endpoint, (int)response.StatusCode, failureBody);
            }

            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var secretString = body.RootElement.TryGetProperty("SecretString", out var secretValue)
                ? secretValue.GetString()
                : null;

            return new SecretReadResult(secretName, !string.IsNullOrWhiteSpace(secretString), secretString, endpoint, (int)response.StatusCode, null);
        }
        catch (Exception ex)
        {
            return new SecretReadResult(secretName, false, null, endpoint, null, ex.Message);
        }
    }
}
