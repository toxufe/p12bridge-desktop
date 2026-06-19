using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class AppStoreConnectBundleIdLookupService : IAppStoreConnectBundleIdLookupService
{
    private readonly HttpClient httpClient;
    private readonly AppleDeveloperAuthService authService;

    public AppStoreConnectBundleIdLookupService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        authService = new AppleDeveloperAuthService(this.httpClient);
    }

    public async Task<AppStoreConnectBundleIdLookupResult> LookupByIdentifierAsync(
        AppStoreConnectBundleIdLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint(request.BundleIdentifier);
        if (string.IsNullOrWhiteSpace(request.BundleIdentifier))
        {
            return AppStoreConnectBundleIdLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppStoreConnectBundleIdLookupErrorCodes.BundleIdMissing,
                    ValidationSeverity.Error,
                    "IPA Bundle ID is required.",
                    "Inspect an IPA before checking Bundle ID."));
        }

        var tokenResult = authService.CreateToken(request.Credential);
        if (!tokenResult.IsSuccess)
        {
            return AppStoreConnectBundleIdLookupResult.Failure(endpoint, tokenResult.Issues);
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
            return AppStoreConnectBundleIdLookupResult.Failure(
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
        $"https://api.appstoreconnect.apple.com/v1/bundleIds?filter%5Bidentifier%5D={Uri.EscapeDataString(bundleIdentifier ?? string.Empty)}&limit=1";

    private static AppStoreConnectBundleIdLookupResult ParseSuccess(string endpoint, Stream responseStream)
    {
        using var document = JsonDocument.Parse(responseStream);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return MalformedResponse(endpoint);
        }

        if (data.GetArrayLength() == 0)
        {
            return AppStoreConnectBundleIdLookupResult.Success(endpoint, null);
        }

        var bundleElement = data[0];
        var id = ReadString(bundleElement, "id");
        if (!bundleElement.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
        {
            return MalformedResponse(endpoint);
        }

        var name = ReadString(attributes, "name");
        var identifier = ReadString(attributes, "identifier");
        var platform = ReadString(attributes, "platform");
        var seedId = ReadString(attributes, "seedId");
        if (string.IsNullOrWhiteSpace(id)
            || string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(identifier)
            || string.IsNullOrWhiteSpace(platform))
        {
            return MalformedResponse(endpoint);
        }

        return AppStoreConnectBundleIdLookupResult.Success(
            endpoint,
            new AppStoreConnectBundleId(id, name, identifier, platform, seedId));
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static AppStoreConnectBundleIdLookupResult MalformedResponse(string endpoint) =>
        AppStoreConnectBundleIdLookupResult.Failure(
            endpoint,
            new ValidationIssue(
                AppStoreConnectBundleIdLookupErrorCodes.ResponseMalformed,
                ValidationSeverity.Error,
                "App Store Connect Bundle ID response is invalid.",
                "Retry later or check the Apple API response."));

    private static AppStoreConnectBundleIdLookupResult MapResponse(string endpoint, HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized => AppStoreConnectBundleIdLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleUnauthorized,
                    ValidationSeverity.Error,
                    "App Store Connect rejected the API token.",
                    "Verify the Key ID, Issuer ID, private key, and API key status.")),
            HttpStatusCode.Forbidden => AppStoreConnectBundleIdLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleForbidden,
                    ValidationSeverity.Error,
                    "The API key does not have permission for Bundle ID lookup.",
                    "Review the API key role and access in App Store Connect.")),
            _ when (int)statusCode >= 500 => AppStoreConnectBundleIdLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleApiUnavailable,
                    ValidationSeverity.Error,
                    "App Store Connect is unavailable or returned a server error.",
                    "Retry later or check Apple system status.")),
            _ => AppStoreConnectBundleIdLookupResult.Failure(
                endpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.UnexpectedAppleResponse,
                    ValidationSeverity.Error,
                    "App Store Connect returned an unexpected response.",
                    "Retry later or review the Apple response."))
        };
}
