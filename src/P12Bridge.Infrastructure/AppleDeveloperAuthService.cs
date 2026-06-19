using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class AppleDeveloperAuthService : IAppleDeveloperAuthService
{
    private static readonly Uri ConnectionCheckEndpoint = new("https://api.appstoreconnect.apple.com/v1/apps?limit=1");
    private readonly HttpClient httpClient;

    public AppleDeveloperAuthService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
    }

    public AppleDeveloperTokenResult CreateToken(AppleApiKeyCredential credential, DateTimeOffset? now = null)
    {
        var issues = ValidateCredential(credential);
        if (issues.Count > 0)
        {
            return AppleDeveloperTokenResult.Failure(issues.ToArray());
        }

        var issuedAt = now ?? DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(20);

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(credential.PrivateKeyPem.AsSpan());

            var header = new Dictionary<string, object>
            {
                ["alg"] = "ES256",
                ["kid"] = credential.KeyId,
                ["typ"] = "JWT"
            };
            var payload = new Dictionary<string, object>
            {
                ["iss"] = credential.IssuerId,
                ["iat"] = issuedAt.ToUnixTimeSeconds(),
                ["exp"] = expiresAt.ToUnixTimeSeconds(),
                ["aud"] = "appstoreconnect-v1"
            };

            var encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
            var encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
            var signingInput = $"{encodedHeader}.{encodedPayload}";
            var signature = ecdsa.SignData(
                Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            return AppleDeveloperTokenResult.Success(
                $"{signingInput}.{Base64UrlEncode(signature)}",
                expiresAt);
        }
        catch (ArgumentException)
        {
            return InvalidPrivateKey();
        }
        catch (CryptographicException)
        {
            return InvalidPrivateKey();
        }
    }

    public async Task<AppleDeveloperConnectionResult> CheckConnectionAsync(
        AppleApiKeyCredential credential,
        CancellationToken cancellationToken = default)
    {
        var tokenResult = CreateToken(credential);
        if (!tokenResult.IsSuccess)
        {
            return AppleDeveloperConnectionResult.Failure(
                ConnectionCheckEndpoint.ToString(),
                tokenResult.Issues.ToArray());
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, ConnectionCheckEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            return MapResponse(response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return AppleDeveloperConnectionResult.Failure(
                ConnectionCheckEndpoint.ToString(),
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.NetworkFailure,
                    ValidationSeverity.Error,
                    "Could not reach App Store Connect.",
                    "Check the network connection and retry."));
        }
    }

    private static List<ValidationIssue> ValidateCredential(AppleApiKeyCredential credential)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(credential.KeyId))
        {
            issues.Add(new ValidationIssue(
                AppleDeveloperAuthErrorCodes.MissingKeyId,
                ValidationSeverity.Error,
                "App Store Connect API Key ID is required.",
                "Enter the Key ID shown in App Store Connect."));
        }

        if (string.IsNullOrWhiteSpace(credential.IssuerId))
        {
            issues.Add(new ValidationIssue(
                AppleDeveloperAuthErrorCodes.MissingIssuerId,
                ValidationSeverity.Error,
                "App Store Connect Issuer ID is required.",
                "Enter the Issuer ID from App Store Connect API access settings."));
        }

        if (string.IsNullOrWhiteSpace(credential.PrivateKeyPem))
        {
            issues.Add(new ValidationIssue(
                AppleDeveloperAuthErrorCodes.MissingPrivateKey,
                ValidationSeverity.Error,
                "App Store Connect API private key is required.",
                "Import the .p8 private key downloaded from App Store Connect."));
        }

        return issues;
    }

    private static AppleDeveloperTokenResult InvalidPrivateKey() =>
        AppleDeveloperTokenResult.Failure(new ValidationIssue(
            AppleDeveloperAuthErrorCodes.InvalidPrivateKey,
            ValidationSeverity.Error,
            "App Store Connect API private key is invalid.",
            "Import the original .p8 private key downloaded from App Store Connect."));

    private static AppleDeveloperConnectionResult MapResponse(HttpStatusCode statusCode)
    {
        if ((int)statusCode >= 200 && (int)statusCode <= 299)
        {
            return AppleDeveloperConnectionResult.Success(ConnectionCheckEndpoint.ToString());
        }

        return statusCode switch
        {
            HttpStatusCode.Unauthorized => AppleDeveloperConnectionResult.Failure(
                ConnectionCheckEndpoint.ToString(),
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleUnauthorized,
                    ValidationSeverity.Error,
                    "App Store Connect rejected the API token.",
                    "Verify the Key ID, Issuer ID, private key, and API key status.")),
            HttpStatusCode.Forbidden => AppleDeveloperConnectionResult.Failure(
                ConnectionCheckEndpoint.ToString(),
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleForbidden,
                    ValidationSeverity.Error,
                    "The API key does not have permission for the connection check.",
                    "Review the API key role and access in App Store Connect.")),
            _ when (int)statusCode >= 500 => AppleDeveloperConnectionResult.Failure(
                ConnectionCheckEndpoint.ToString(),
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleApiUnavailable,
                    ValidationSeverity.Error,
                    "App Store Connect is unavailable or returned a server error.",
                    "Retry later or check Apple system status.")),
            _ => AppleDeveloperConnectionResult.Failure(
                ConnectionCheckEndpoint.ToString(),
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.UnexpectedAppleResponse,
                    ValidationSeverity.Error,
                    "App Store Connect returned an unexpected response.",
                    "Retry later or review the technical details from the Apple response."))
        };
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
