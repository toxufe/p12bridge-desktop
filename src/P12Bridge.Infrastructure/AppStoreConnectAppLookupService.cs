using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class AppStoreConnectAppLookupService : IAppStoreConnectAppLookupService
{
    private readonly HttpClient httpClient;
    private readonly AppleDeveloperAuthService authService;

    public AppStoreConnectAppLookupService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        authService = new AppleDeveloperAuthService(this.httpClient);
    }

    public async Task<AppStoreConnectAppLookupResult> LookupByBundleIdAsync(
        AppStoreConnectAppLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint(request.BundleIdentifier);
        if (string.IsNullOrWhiteSpace(request.BundleIdentifier))
        {
            return AppStoreConnectAppLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppStoreConnectAppLookupErrorCodes.BundleIdMissing,
                    ValidationSeverity.Error,
                    "IPA Bundle ID is required.",
                    "Inspect an IPA before checking App Store Connect."));
        }

        var tokenResult = authService.CreateToken(request.Credential);
        if (!tokenResult.IsSuccess)
        {
            return AppStoreConnectAppLookupResult.Failure(endpoint, tokenResult.Issues.ToArray());
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
            return AppStoreConnectAppLookupResult.Failure(
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

    private static string BuildEndpoint(string bundleIdentifier) =>
        $"https://api.appstoreconnect.apple.com/v1/apps?filter%5BbundleId%5D={Uri.EscapeDataString(bundleIdentifier ?? string.Empty)}&limit=1";

    private static AppStoreConnectAppLookupResult ParseSuccess(string endpoint, Stream responseStream)
    {
        using var document = JsonDocument.Parse(responseStream);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return MalformedResponse(endpoint);
        }

        if (data.GetArrayLength() == 0)
        {
            return AppStoreConnectAppLookupResult.Success(endpoint, null);
        }

        var appElement = data[0];
        var id = ReadString(appElement, "id");
        if (!appElement.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
        {
            return MalformedResponse(endpoint);
        }

        var name = ReadString(attributes, "name");
        var bundleId = ReadString(attributes, "bundleId");
        var sku = ReadString(attributes, "sku");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(bundleId))
        {
            return MalformedResponse(endpoint);
        }

        return AppStoreConnectAppLookupResult.Success(
            endpoint,
            new AppStoreConnectApp(id, name, bundleId, sku));
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static AppStoreConnectAppLookupResult MalformedResponse(string endpoint) =>
        AppStoreConnectAppLookupResult.Failure(
            endpoint,
            new ValidationIssue(
                AppStoreConnectAppLookupErrorCodes.ResponseMalformed,
                ValidationSeverity.Error,
                "App Store Connect app response is invalid.",
                "Retry later or check the Apple API response."));

    private static AppStoreConnectAppLookupResult MapResponse(string endpoint, HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized => AppStoreConnectAppLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleUnauthorized,
                    ValidationSeverity.Error,
                    "App Store Connect rejected the API token.",
                    "Verify the Key ID, Issuer ID, private key, and API key status.")),
            HttpStatusCode.Forbidden => AppStoreConnectAppLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleForbidden,
                    ValidationSeverity.Error,
                    "The API key does not have permission for app lookup.",
                    "Review the API key role and access in App Store Connect.")),
            _ when (int)statusCode >= 500 => AppStoreConnectAppLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleApiUnavailable,
                    ValidationSeverity.Error,
                    "App Store Connect is unavailable or returned a server error.",
                    "Retry later or check Apple system status.")),
            _ => AppStoreConnectAppLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.UnexpectedAppleResponse,
                    ValidationSeverity.Error,
                    "App Store Connect returned an unexpected response.",
                    "Retry later or review the Apple response."))
        };
}
