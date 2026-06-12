namespace TaxNetGuardian.Api;

public interface ISecretProvider
{
    Task<string?> GetSecretStringAsync(string secretName, CancellationToken cancellationToken = default);
}

public interface IWritableSecretProvider : ISecretProvider
{
    Task<SecretWriteResult> PutSecretStringAsync(string secretName, string secretString, CancellationToken cancellationToken = default);
}

public sealed record SecretReadResult(
    string SecretName,
    bool Found,
    string? SecretString,
    string Endpoint,
    int? StatusCode,
    string? Error);

public sealed record SecretWriteResult(
    string SecretName,
    bool Succeeded,
    string Endpoint,
    int? StatusCode,
    string? VersionId,
    string? Error);
