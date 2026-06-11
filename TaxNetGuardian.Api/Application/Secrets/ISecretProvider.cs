namespace TaxNetGuardian.Api;

public interface ISecretProvider
{
    Task<string?> GetSecretStringAsync(string secretName, CancellationToken cancellationToken = default);
}

public sealed record SecretReadResult(
    string SecretName,
    bool Found,
    string? SecretString,
    string Endpoint,
    int? StatusCode,
    string? Error);
