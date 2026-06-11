using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TaxNetGuardian.Api;

public sealed class JwtClaimsProjectionMiddleware
{
    private static readonly SemaphoreSlim JwksLock = new(1, 1);
    private static JsonDocument? CachedJwks;
    private static DateTimeOffset CachedJwksUntilUtc;

    private readonly RequestDelegate _next;
    private readonly TaxNetPlatformOptions _options;
    private readonly ILogger<JwtClaimsProjectionMiddleware> _logger;

    public JwtClaimsProjectionMiddleware(
        RequestDelegate next,
        TaxNetPlatformOptions options,
        ILogger<JwtClaimsProjectionMiddleware> logger)
    {
        _next = next;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Auth.RequireJwt ||
            !context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var token = context.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        var projection = await TryProjectClaimsAsync(token);
        if (projection.Principal is not null)
        {
            context.User = projection.Principal;
        }
        else
        {
            _logger.LogWarning("JWT claims projection failed: {Error}", projection.Error);
        }

        await _next(context);
    }

    private async Task<JwtProjectionResult> TryProjectClaimsAsync(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return JwtProjectionResult.Failed("Token does not have JWT header, payload, and signature segments.");
        }

        try
        {
            using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            if (!await VerifySignatureAsync(parts, header.RootElement))
            {
                return JwtProjectionResult.Failed("JWT signature verification failed.");
            }

            var claims = new List<Claim>();
            foreach (var property in payload.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    claims.AddRange(property.Value.EnumerateArray().Select(value => new Claim(property.Name, value.ToString())));
                }
                else
                {
                    claims.Add(new Claim(property.Name, property.Value.ToString()));
                }
            }

            if (payload.RootElement.TryGetProperty("exp", out var exp) &&
                exp.TryGetInt64(out var expSeconds) &&
                DateTimeOffset.FromUnixTimeSeconds(expSeconds) <= DateTimeOffset.UtcNow)
            {
                return JwtProjectionResult.Failed("Token is expired.");
            }

            if (!string.IsNullOrWhiteSpace(_options.Auth.Audience) &&
                !claims.Any(x => x.Type == "aud" && x.Value.Equals(_options.Auth.Audience, StringComparison.OrdinalIgnoreCase)))
            {
                return JwtProjectionResult.Failed("Token audience does not match configured audience.");
            }

            if (!string.IsNullOrWhiteSpace(_options.Auth.Authority) &&
                !claims.Any(x => x.Type == "iss" && x.Value.StartsWith(_options.Auth.Authority, StringComparison.OrdinalIgnoreCase)))
            {
                return JwtProjectionResult.Failed("Token issuer does not match configured authority.");
            }

            return JwtProjectionResult.Succeeded(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")));
        }
        catch (Exception ex)
        {
            return JwtProjectionResult.Failed(ex.Message);
        }
    }

    private async Task<bool> VerifySignatureAsync(string[] tokenParts, JsonElement header)
    {
        var algorithm = header.TryGetProperty("alg", out var alg) ? alg.GetString() : null;
        var keyId = header.TryGetProperty("kid", out var kid) ? kid.GetString() : null;
        if (!string.Equals(algorithm, "RS256", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(keyId))
        {
            return false;
        }

        var jwks = await GetJwksAsync();
        var key = jwks.RootElement.GetProperty("keys")
            .EnumerateArray()
            .FirstOrDefault(x =>
                x.TryGetProperty("kid", out var jwkKid) &&
                string.Equals(jwkKid.GetString(), keyId, StringComparison.Ordinal));

        if (key.ValueKind == JsonValueKind.Undefined ||
            !key.TryGetProperty("n", out var modulusElement) ||
            !key.TryGetProperty("e", out var exponentElement))
        {
            return false;
        }

        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = Base64UrlDecode(modulusElement.GetString() ?? ""),
            Exponent = Base64UrlDecode(exponentElement.GetString() ?? "")
        });

        var signedBytes = Encoding.ASCII.GetBytes($"{tokenParts[0]}.{tokenParts[1]}");
        var signature = Base64UrlDecode(tokenParts[2]);
        return rsa.VerifyData(signedBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private async Task<JsonDocument> GetJwksAsync()
    {
        if (CachedJwks is not null && CachedJwksUntilUtc > DateTimeOffset.UtcNow)
        {
            return CachedJwks;
        }

        await JwksLock.WaitAsync();
        try
        {
            if (CachedJwks is not null && CachedJwksUntilUtc > DateTimeOffset.UtcNow)
            {
                return CachedJwks;
            }

            var discoveryUrl = $"{_options.Auth.Authority.TrimEnd('/')}/.well-known/openid-configuration";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var discovery = await JsonDocument.ParseAsync(await http.GetStreamAsync(discoveryUrl));
            var jwksUri = discovery.RootElement.TryGetProperty("jwks_uri", out var uri)
                ? uri.GetString()
                : $"{_options.Auth.Authority.TrimEnd('/')}/.well-known/jwks.json";

            CachedJwks?.Dispose();
            CachedJwks = await JsonDocument.ParseAsync(await http.GetStreamAsync(jwksUri));
            CachedJwksUntilUtc = DateTimeOffset.UtcNow.AddMinutes(30);
            return CachedJwks;
        }
        finally
        {
            JwksLock.Release();
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private sealed record JwtProjectionResult(ClaimsPrincipal? Principal, string Error)
    {
        public static JwtProjectionResult Succeeded(ClaimsPrincipal principal) => new(principal, "");
        public static JwtProjectionResult Failed(string error) => new(null, error);
    }
}
