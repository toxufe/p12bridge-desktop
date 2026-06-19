using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class AppStoreConnectBuildLookupService : IAppStoreConnectBuildLookupService
{
    private const int DefaultLimit = 5;
    private const int MinimumLimit = 1;
    private const int MaximumLimit = 10;

    private readonly HttpClient httpClient;
    private readonly AppStoreConnectAppLookupService appLookupService;
    private readonly AppleDeveloperAuthService authService;

    public AppStoreConnectBuildLookupService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        appLookupService = new AppStoreConnectAppLookupService(this.httpClient);
        authService = new AppleDeveloperAuthService(this.httpClient);
    }

    public async Task<AppStoreConnectBuildLookupResult> LookupByBundleIdAsync(
        AppStoreConnectBuildLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        var appResult = await appLookupService.LookupByBundleIdAsync(
            new AppStoreConnectAppLookupRequest(request.Credential, request.BundleIdentifier),
            cancellationToken);

        if (!appResult.IsSuccess)
        {
            return AppStoreConnectBuildLookupResult.Failure(
                appResult.CheckedEndpoint,
                string.Empty,
                appResult.Issues);
        }

        if (appResult.App is null)
        {
            return AppStoreConnectBuildLookupResult.Success(
                appResult.CheckedEndpoint,
                string.Empty,
                null,
                Array.Empty<AppStoreConnectBuild>());
        }

        var buildsEndpoint = BuildEndpoint(appResult.App.Id, request.Limit);
        var tokenResult = authService.CreateToken(request.Credential);
        if (!tokenResult.IsSuccess)
        {
            return AppStoreConnectBuildLookupResult.Failure(
                appResult.CheckedEndpoint,
                buildsEndpoint,
                tokenResult.Issues);
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, buildsEndpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);

        try
        {
            using var response = await httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if ((int)response.StatusCode < 200 || (int)response.StatusCode > 299)
            {
                return MapResponse(appResult.CheckedEndpoint, buildsEndpoint, response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return ParseSuccess(appResult.CheckedEndpoint, buildsEndpoint, appResult.App, stream);
        }
        catch (HttpRequestException)
        {
            return AppStoreConnectBuildLookupResult.Failure(
                appResult.CheckedEndpoint,
                buildsEndpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.NetworkFailure,
                    ValidationSeverity.Error,
                    "Could not reach App Store Connect.",
                    "Check the network connection and retry."));
        }
        catch (JsonException)
        {
            return MalformedResponse(appResult.CheckedEndpoint, buildsEndpoint);
        }
    }

    private static string BuildEndpoint(string appId, int limit) =>
        $"https://api.appstoreconnect.apple.com/v1/apps/{Uri.EscapeDataString(appId)}/builds?limit={NormalizeLimit(limit)}&sort=-uploadedDate";

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultLimit;
        }

        return Math.Clamp(limit, MinimumLimit, MaximumLimit);
    }

    private static AppStoreConnectBuildLookupResult ParseSuccess(
        string appEndpoint,
        string buildsEndpoint,
        AppStoreConnectApp app,
        Stream responseStream)
    {
        using var document = JsonDocument.Parse(responseStream);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return MalformedResponse(appEndpoint, buildsEndpoint);
        }

        var builds = new List<AppStoreConnectBuild>();
        foreach (var buildElement in data.EnumerateArray())
        {
            var id = ReadString(buildElement, "id");
            if (!buildElement.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
            {
                return MalformedResponse(appEndpoint, buildsEndpoint);
            }

            var version = ReadString(attributes, "version");
            var processingState = ReadString(attributes, "processingState");
            if (string.IsNullOrWhiteSpace(id)
                || string.IsNullOrWhiteSpace(version)
                || string.IsNullOrWhiteSpace(processingState))
            {
                return MalformedResponse(appEndpoint, buildsEndpoint);
            }

            builds.Add(new AppStoreConnectBuild(
                id,
                version,
                processingState,
                ReadDateTimeOffset(attributes, "uploadedDate"),
                ReadNullableBoolean(attributes, "expired")));
        }

        return AppStoreConnectBuildLookupResult.Success(appEndpoint, buildsEndpoint, app, builds);
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

    private static AppStoreConnectBuildLookupResult MalformedResponse(string appEndpoint, string buildsEndpoint) =>
        AppStoreConnectBuildLookupResult.Failure(
            appEndpoint,
            buildsEndpoint,
            new ValidationIssue(
                AppStoreConnectBuildLookupErrorCodes.ResponseMalformed,
                ValidationSeverity.Error,
                "App Store Connect build response is invalid.",
                "Retry later or check the Apple API response."));

    private static AppStoreConnectBuildLookupResult MapResponse(
        string appEndpoint,
        string buildsEndpoint,
        HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized => AppStoreConnectBuildLookupResult.Failure(
                appEndpoint,
                buildsEndpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleUnauthorized,
                    ValidationSeverity.Error,
                    "App Store Connect rejected the API token.",
                    "Verify the Key ID, Issuer ID, private key, and API key status.")),
            HttpStatusCode.Forbidden => AppStoreConnectBuildLookupResult.Failure(
                appEndpoint,
                buildsEndpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleForbidden,
                    ValidationSeverity.Error,
                    "The API key does not have permission for build lookup.",
                    "Review the API key role and access in App Store Connect.")),
            _ when (int)statusCode >= 500 => AppStoreConnectBuildLookupResult.Failure(
                appEndpoint,
                buildsEndpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleApiUnavailable,
                    ValidationSeverity.Error,
                    "App Store Connect is unavailable or returned a server error.",
                    "Retry later or check Apple system status.")),
            _ => AppStoreConnectBuildLookupResult.Failure(
                appEndpoint,
                buildsEndpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.UnexpectedAppleResponse,
                    ValidationSeverity.Error,
                    "App Store Connect returned an unexpected response.",
                    "Retry later or review the Apple response."))
        };
}
