using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class AppStoreConnectProfileLookupService : IAppStoreConnectProfileLookupService
{
    private const int DefaultLimit = 10;
    private const int MinimumLimit = 1;
    private const int MaximumLimit = 200;

    private readonly HttpClient httpClient;
    private readonly AppStoreConnectBundleIdLookupService bundleIdLookupService;
    private readonly AppleDeveloperAuthService authService;

    public AppStoreConnectProfileLookupService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        bundleIdLookupService = new AppStoreConnectBundleIdLookupService(this.httpClient);
        authService = new AppleDeveloperAuthService(this.httpClient);
    }

    public async Task<AppStoreConnectProfileLookupResult> LookupByBundleIdAsync(
        AppStoreConnectProfileLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        var bundleIdResult = await bundleIdLookupService.LookupByIdentifierAsync(
            new AppStoreConnectBundleIdLookupRequest(request.Credential, request.BundleIdentifier),
            cancellationToken);

        if (!bundleIdResult.IsSuccess)
        {
            return AppStoreConnectProfileLookupResult.Failure(
                bundleIdResult.CheckedEndpoint,
                string.Empty,
                bundleIdResult.Issues);
        }

        if (bundleIdResult.BundleId is null)
        {
            return AppStoreConnectProfileLookupResult.Success(
                bundleIdResult.CheckedEndpoint,
                string.Empty,
                null,
                Array.Empty<AppStoreConnectProfile>());
        }

        var profilesEndpoint = BuildEndpoint(bundleIdResult.BundleId.Id, request.Limit);
        var tokenResult = authService.CreateToken(request.Credential);
        if (!tokenResult.IsSuccess)
        {
            return AppStoreConnectProfileLookupResult.Failure(
                bundleIdResult.CheckedEndpoint,
                profilesEndpoint,
                tokenResult.Issues);
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, profilesEndpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);

        try
        {
            using var response = await httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if ((int)response.StatusCode < 200 || (int)response.StatusCode > 299)
            {
                return MapResponse(bundleIdResult.CheckedEndpoint, profilesEndpoint, response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return ParseSuccess(bundleIdResult.CheckedEndpoint, profilesEndpoint, bundleIdResult.BundleId, stream);
        }
        catch (HttpRequestException)
        {
            return AppStoreConnectProfileLookupResult.Failure(
                bundleIdResult.CheckedEndpoint,
                profilesEndpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.NetworkFailure,
                    ValidationSeverity.Error,
                    "Could not reach App Store Connect.",
                    "Check the network connection and retry."));
        }
        catch (JsonException)
        {
            return MalformedResponse(bundleIdResult.CheckedEndpoint, profilesEndpoint);
        }
    }

    private static string BuildEndpoint(string bundleId, int limit) =>
        $"https://api.appstoreconnect.apple.com/v1/bundleIds/{Uri.EscapeDataString(bundleId)}/profiles?limit={NormalizeLimit(limit)}";

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultLimit;
        }

        return Math.Clamp(limit, MinimumLimit, MaximumLimit);
    }

    private static AppStoreConnectProfileLookupResult ParseSuccess(
        string bundleIdEndpoint,
        string profilesEndpoint,
        AppStoreConnectBundleId bundleId,
        Stream responseStream)
    {
        using var document = JsonDocument.Parse(responseStream);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return MalformedResponse(bundleIdEndpoint, profilesEndpoint);
        }

        var profiles = new List<AppStoreConnectProfile>();
        foreach (var profileElement in data.EnumerateArray())
        {
            var id = ReadString(profileElement, "id");
            if (!profileElement.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
            {
                return MalformedResponse(bundleIdEndpoint, profilesEndpoint);
            }

            var name = ReadString(attributes, "name");
            var platform = ReadString(attributes, "platform");
            var uuid = ReadString(attributes, "uuid");
            var profileState = ReadString(attributes, "profileState");
            var profileType = ReadString(attributes, "profileType");
            if (string.IsNullOrWhiteSpace(id)
                || string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(platform)
                || string.IsNullOrWhiteSpace(uuid)
                || string.IsNullOrWhiteSpace(profileState)
                || string.IsNullOrWhiteSpace(profileType))
            {
                return MalformedResponse(bundleIdEndpoint, profilesEndpoint);
            }

            profiles.Add(new AppStoreConnectProfile(
                id,
                name,
                platform,
                uuid,
                profileState,
                profileType,
                ReadDateTimeOffset(attributes, "createdDate"),
                ReadDateTimeOffset(attributes, "expirationDate")));
        }

        return AppStoreConnectProfileLookupResult.Success(bundleIdEndpoint, profilesEndpoint, bundleId, profiles);
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

    private static AppStoreConnectProfileLookupResult MalformedResponse(
        string bundleIdEndpoint,
        string profilesEndpoint) =>
        AppStoreConnectProfileLookupResult.Failure(
            bundleIdEndpoint,
            profilesEndpoint,
            new ValidationIssue(
                AppStoreConnectProfileLookupErrorCodes.ResponseMalformed,
                ValidationSeverity.Error,
                "App Store Connect profile response is invalid.",
                "Retry later or check the Apple API response."));

    private static AppStoreConnectProfileLookupResult MapResponse(
        string bundleIdEndpoint,
        string profilesEndpoint,
        HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized => AppStoreConnectProfileLookupResult.Failure(
                bundleIdEndpoint,
                profilesEndpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleUnauthorized,
                    ValidationSeverity.Error,
                    "App Store Connect rejected the API token.",
                    "Verify the Key ID, Issuer ID, private key, and API key status.")),
            HttpStatusCode.Forbidden => AppStoreConnectProfileLookupResult.Failure(
                bundleIdEndpoint,
                profilesEndpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleForbidden,
                    ValidationSeverity.Error,
                    "The API key does not have permission for profile lookup.",
                    "Review the API key role and access in App Store Connect.")),
            _ when (int)statusCode >= 500 => AppStoreConnectProfileLookupResult.Failure(
                bundleIdEndpoint,
                profilesEndpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.AppleApiUnavailable,
                    ValidationSeverity.Error,
                    "App Store Connect is unavailable or returned a server error.",
                    "Retry later or check Apple system status.")),
            _ => AppStoreConnectProfileLookupResult.Failure(
                bundleIdEndpoint,
                profilesEndpoint,
                new ValidationIssue(
                    AppleDeveloperAuthErrorCodes.UnexpectedAppleResponse,
                    ValidationSeverity.Error,
                    "App Store Connect returned an unexpected response.",
                    "Retry later or review the Apple response."))
        };
}
