using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class AppStoreConnectCertificateLookupService : IAppStoreConnectCertificateLookupService
{
    private const int DefaultLimit = 10;
    private const int MinimumLimit = 1;
    private const int MaximumLimit = 200;

    private readonly HttpClient httpClient;
    private readonly AppleDeveloperAuthService authService;

    public AppStoreConnectCertificateLookupService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        authService = new AppleDeveloperAuthService(this.httpClient);
    }

    public async Task<AppStoreConnectCertificateLookupResult> LookupAsync(
        AppStoreConnectCertificateLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint(request.Limit);
        var tokenResult = authService.CreateToken(request.Credential);
        if (!tokenResult.IsSuccess)
        {
            return AppStoreConnectCertificateLookupResult.Failure(endpoint, tokenResult.Issues);
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);

        try
        {
            using var response = await httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if ((int)response.StatusCode < 200 || (int)response.StatusCode > 299)
            {
                return MapResponse(endpoint, response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return ParseSuccess(endpoint, stream);
        }
        catch (HttpRequestException)
        {
            return AppStoreConnectCertificateLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.NetworkFailure,
                    ValidationSeverity.Error,
                    "Could not reach App Store Connect.",
                    "Check the network connection and retry."));
        }
        catch (JsonException)
        {
            return MalformedResponse(endpoint);
        }
    }

    private static string BuildEndpoint(int limit) =>
        $"https://api.appstoreconnect.apple.com/v1/certificates?limit={NormalizeLimit(limit)}";

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultLimit;
        }

        return Math.Clamp(limit, MinimumLimit, MaximumLimit);
    }

    private static AppStoreConnectCertificateLookupResult ParseSuccess(string endpoint, Stream responseStream)
    {
        using var document = JsonDocument.Parse(responseStream);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return MalformedResponse(endpoint);
        }

        var certificates = new List<AppStoreConnectCertificate>();
        foreach (var certificateElement in data.EnumerateArray())
        {
            var id = ReadString(certificateElement, "id");
            if (!certificateElement.TryGetProperty("attributes", out var attributes)
                || attributes.ValueKind != JsonValueKind.Object)
            {
                return MalformedResponse(endpoint);
            }

            var certificateType = ReadString(attributes, "certificateType");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(certificateType))
            {
                return MalformedResponse(endpoint);
            }

            certificates.Add(new AppStoreConnectCertificate(
                id,
                ReadString(attributes, "name"),
                ReadString(attributes, "displayName"),
                certificateType,
                ReadString(attributes, "serialNumber"),
                ReadString(attributes, "platform"),
                ReadDateTimeOffset(attributes, "expirationDate"),
                ReadNullableBoolean(attributes, "activated")));
        }

        return AppStoreConnectCertificateLookupResult.Success(endpoint, certificates);
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(value.GetString(), out var parsed))
        {
            return null;
        }

        return parsed;
    }

    private static bool? ReadNullableBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static AppStoreConnectCertificateLookupResult MalformedResponse(string endpoint) =>
        AppStoreConnectCertificateLookupResult.Failure(
            endpoint,
            new ValidationIssue(
                AppStoreConnectCertificateLookupErrorCodes.ResponseMalformed,
                ValidationSeverity.Error,
                "App Store Connect certificate response is invalid.",
                "Retry later or check the Apple API response."));

    private static AppStoreConnectCertificateLookupResult MapResponse(string endpoint, HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized => AppStoreConnectCertificateLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleUnauthorized,
                    ValidationSeverity.Error,
                    "App Store Connect rejected the API token.",
                    "Verify the Key ID, Issuer ID, private key, and API key status.")),
            HttpStatusCode.Forbidden => AppStoreConnectCertificateLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleForbidden,
                    ValidationSeverity.Error,
                    "The API key does not have permission for certificate lookup.",
                    "Review the API key role and access in App Store Connect.")),
            _ when (int)statusCode >= 500 => AppStoreConnectCertificateLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleApiUnavailable,
                    ValidationSeverity.Error,
                    "App Store Connect is unavailable or returned a server error.",
                    "Retry later or check Apple system status.")),
            _ => AppStoreConnectCertificateLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.UnexpectedAppleResponse,
                    ValidationSeverity.Error,
                    "App Store Connect returned an unexpected response.",
                    "Retry later or review the Apple response."))
        };
}
