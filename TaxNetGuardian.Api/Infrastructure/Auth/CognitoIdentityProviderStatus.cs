using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.Runtime;

namespace TaxNetGuardian.Api;

public sealed class CognitoIdentityProviderStatus
{
    private readonly TaxNetPlatformOptions _options;

    public CognitoIdentityProviderStatus(TaxNetPlatformOptions options)
    {
        _options = options;
    }

    public async Task<CognitoStatusReport> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient();
            var response = await client.ListUserPoolsAsync(new ListUserPoolsRequest { MaxResults = 10 }, cancellationToken);
            return new CognitoStatusReport(
                true,
                _options.Aws.ShouldUseLocalStack() ? "LocalStack" : "AWS",
                DescribeEndpoint(),
                response.UserPools.Select(x => new CognitoPoolSummary(x.Id, x.Name)).ToArray(),
                null);
        }
        catch (Exception ex)
        {
            return new CognitoStatusReport(false, _options.Aws.ShouldUseLocalStack() ? "LocalStack" : "AWS", DescribeEndpoint(), [], ex.Message);
        }
    }

    private AmazonCognitoIdentityProviderClient CreateClient()
    {
        var region = RegionEndpoint.GetBySystemName(
            Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
            ?? _options.Aws.Region);

        var config = new AmazonCognitoIdentityProviderConfig { RegionEndpoint = region };
        if (_options.Aws.ShouldUseLocalStack())
        {
            config.ServiceURL = _options.Aws.EffectiveLocalStackEndpoint();
            config.AuthenticationRegion = region.SystemName;
            return new AmazonCognitoIdentityProviderClient(new BasicAWSCredentials("test", "test"), config);
        }

        return new AmazonCognitoIdentityProviderClient(config);
    }

    private string DescribeEndpoint()
        => _options.Aws.ShouldUseLocalStack()
            ? _options.Aws.EffectiveLocalStackEndpoint()
            : $"aws-cognito-idp:{Environment.GetEnvironmentVariable("AWS_REGION") ?? _options.Aws.Region}";
}

public sealed record CognitoStatusReport(
    bool Reachable,
    string Mode,
    string Endpoint,
    IReadOnlyList<CognitoPoolSummary> UserPools,
    string? Error);

public sealed record CognitoPoolSummary(string? Id, string? Name);
