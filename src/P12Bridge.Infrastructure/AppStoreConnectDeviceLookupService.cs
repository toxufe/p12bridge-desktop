using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class AppStoreConnectDeviceLookupService : IAppStoreConnectDeviceLookupService
{
    private const int DefaultLimit = 10;
    private const int MinimumLimit = 1;
    private const int MaximumLimit = 200;

    private readonly HttpClient httpClient;
    private readonly AppleDeveloperAuthService authService;

    public AppStoreConnectDeviceLookupService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        authService = new AppleDeveloperAuthService(this.httpClient);
    }

    public async Task<AppStoreConnectDeviceLookupResult> LookupAsync(
        AppStoreConnectDeviceLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint(request.Limit);
        var tokenResult = authService.CreateToken(request.Credential);
        if (!tokenResult.IsSuccess)
        {
            return AppStoreConnectDeviceLookupResult.Failure(endpoint, tokenResult.Issues);
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
            return AppStoreConnectDeviceLookupResult.Failure(
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
        $"https://api.appstoreconnect.apple.com/v1/devices?limit={NormalizeLimit(limit)}";

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultLimit;
        }

        return Math.Clamp(limit, MinimumLimit, MaximumLimit);
    }

    private static AppStoreConnectDeviceLookupResult ParseSuccess(string endpoint, Stream responseStream)
    {
        using var document = JsonDocument.Parse(responseStream);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return MalformedResponse(endpoint);
        }

        var devices = new List<AppStoreConnectDevice>();
        foreach (var deviceElement in data.EnumerateArray())
        {
            var id = ReadString(deviceElement, "id");
            if (!deviceElement.TryGetProperty("attributes", out var attributes)
                || attributes.ValueKind != JsonValueKind.Object)
            {
                return MalformedResponse(endpoint);
            }

            var udid = ReadString(attributes, "udid");
            var platform = ReadString(attributes, "platform");
            var status = ReadString(attributes, "status");
            if (string.IsNullOrWhiteSpace(id)
                || string.IsNullOrWhiteSpace(udid)
                || string.IsNullOrWhiteSpace(platform)
                || string.IsNullOrWhiteSpace(status))
            {
                return MalformedResponse(endpoint);
            }

            devices.Add(new AppStoreConnectDevice(
                id,
                ReadString(attributes, "name"),
                platform,
                udid,
                ReadString(attributes, "deviceClass"),
                status,
                ReadString(attributes, "model"),
                ReadDateTimeOffset(attributes, "addedDate")));
        }

        return AppStoreConnectDeviceLookupResult.Success(endpoint, devices);
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

    private static AppStoreConnectDeviceLookupResult MalformedResponse(string endpoint) =>
        AppStoreConnectDeviceLookupResult.Failure(
            endpoint,
            new ValidationIssue(
                AppStoreConnectDeviceLookupErrorCodes.ResponseMalformed,
                ValidationSeverity.Error,
                "App Store Connect device response is invalid.",
                "Retry later or check the Apple API response."));

    private static AppStoreConnectDeviceLookupResult MapResponse(string endpoint, HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized => AppStoreConnectDeviceLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleUnauthorized,
                    ValidationSeverity.Error,
                    "App Store Connect rejected the API token.",
                    "Verify the Key ID, Issuer ID, private key, and API key status.")),
            HttpStatusCode.Forbidden => AppStoreConnectDeviceLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleForbidden,
                    ValidationSeverity.Error,
                    "The API key does not have permission for device lookup.",
                    "Review the API key role and access in App Store Connect.")),
            _ when (int)statusCode >= 500 => AppStoreConnectDeviceLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleApiUnavailable,
                    ValidationSeverity.Error,
                    "App Store Connect is unavailable or returned a server error.",
                    "Retry later or check Apple system status.")),
            _ => AppStoreConnectDeviceLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.UnexpectedAppleResponse,
                    ValidationSeverity.Error,
                    "App Store Connect returned an unexpected response.",
                    "Retry later or review the Apple response."))
        };
}
