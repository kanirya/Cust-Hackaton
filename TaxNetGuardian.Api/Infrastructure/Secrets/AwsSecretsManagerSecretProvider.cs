using Amazon;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace TaxNetGuardian.Api;

public sealed class AwsSecretsManagerSecretProvider : ISecretProvider
{
    private readonly TaxNetPlatformOptions _options;

    public AwsSecretsManagerSecretProvider(TaxNetPlatformOptions options)
    {
        _options = options;
    }

    public async Task<string?> GetSecretStringAsync(string secretName, CancellationToken cancellationToken = default)
        => (await GetSecretAsync(secretName, cancellationToken)).SecretString;

    public async Task<SecretReadResult> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return new SecretReadResult(secretName, false, null, "", null, "Secret name was empty.");
        }

        try
        {
            using var client = CreateClient();
            var response = await client.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = secretName
            }, cancellationToken);

            return new SecretReadResult(secretName, !string.IsNullOrWhiteSpace(response.SecretString), response.SecretString, DescribeEndpoint(), (int)response.HttpStatusCode, null);
        }
        catch (Exception ex)
        {
            return new SecretReadResult(secretName, false, null, DescribeEndpoint(), null, ex.Message);
        }
    }

    private AmazonSecretsManagerClient CreateClient()
    {
        var region = RegionEndpoint.GetBySystemName(
            Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
            ?? _options.Aws.Region);

        var config = new AmazonSecretsManagerConfig { RegionEndpoint = region };
        if (_options.Aws.ShouldUseLocalStack())
        {
            config.ServiceURL = _options.Aws.EffectiveLocalStackEndpoint();
            config.AuthenticationRegion = region.SystemName;
            return new AmazonSecretsManagerClient(new BasicAWSCredentials("test", "test"), config);
        }

        return new AmazonSecretsManagerClient(config);
    }

    private string DescribeEndpoint()
        => _options.Aws.ShouldUseLocalStack()
            ? _options.Aws.EffectiveLocalStackEndpoint()
            : $"aws-secretsmanager:{Environment.GetEnvironmentVariable("AWS_REGION") ?? _options.Aws.Region}";
}

public static class SecretProviderFactory
{
    public static ISecretProvider Create(TaxNetPlatformOptions options)
        => options.Aws.ShouldUseLocalStack()
            ? new LocalStackSecretsManagerSecretProvider()
            : new AwsSecretsManagerSecretProvider(options);
}
