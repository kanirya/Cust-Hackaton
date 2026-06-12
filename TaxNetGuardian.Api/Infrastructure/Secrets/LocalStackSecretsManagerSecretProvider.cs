using System.Text;
using System.Text.Json;

namespace TaxNetGuardian.Api;

public sealed class LocalStackSecretsManagerSecretProvider : IWritableSecretProvider
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

    public async Task<SecretWriteResult> PutSecretStringAsync(string secretName, string secretString, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return new SecretWriteResult(secretName, false, "", null, null, "Secret name was empty.");
        }

        var endpoint = Environment.GetEnvironmentVariable("LOCALSTACK_ENDPOINT") ?? "http://localhost:4566";
        var mode = Environment.GetEnvironmentVariable("TAXNET_SECRET_PROVIDER") ?? Environment.GetEnvironmentVariable("SECRET_PROVIDER_MODE");
        if (string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return new SecretWriteResult(secretName, false, endpoint, null, null, "Secret provider disabled.");
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var putResult = await SendSecretWriteAsync(http, endpoint, "secretsmanager.PutSecretValue", new Dictionary<string, string>
            {
                ["SecretId"] = secretName,
                ["SecretString"] = secretString,
                ["ClientRequestToken"] = Guid.NewGuid().ToString()
            }, cancellationToken);

            if (putResult.Succeeded || putResult.StatusCode != 400)
            {
                return putResult;
            }

            return await SendSecretWriteAsync(http, endpoint, "secretsmanager.CreateSecret", new Dictionary<string, string>
            {
                ["Name"] = secretName,
                ["SecretString"] = secretString
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            return new SecretWriteResult(secretName, false, endpoint, null, null, ex.Message);
        }
    }

    private async Task<SecretWriteResult> SendSecretWriteAsync(
        HttpClient http,
        string endpoint,
        string target,
        Dictionary<string, string> payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("X-Amz-Target", target);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/x-amz-json-1.1");

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new SecretWriteResult(payload.GetValueOrDefault("SecretId") ?? payload.GetValueOrDefault("Name") ?? "", false, endpoint, (int)response.StatusCode, null, body);
        }

        string? versionId = null;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("VersionId", out var version))
            {
                versionId = version.GetString();
            }
        }
        catch (JsonException)
        {
            versionId = null;
        }

        return new SecretWriteResult(payload.GetValueOrDefault("SecretId") ?? payload.GetValueOrDefault("Name") ?? "", true, endpoint, (int)response.StatusCode, versionId, null);
    }
}
