using System.Net;
using System.Security.Cryptography;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class AppStoreConnectProfileLookupServiceTests
{
    [Fact]
    public async Task LookupByBundleIdReturnsProfilesForMatchingBundleId()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var requestedUrls = new List<string>();
        var authorizationHeaders = new List<string?>();
        var service = new AppStoreConnectProfileLookupService(new HttpClient(new QueueHandler(
            request =>
            {
                requestedUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
                authorizationHeaders.Add(request.Headers.Authorization?.ToString());
                return BundleIdResponse();
            },
            request =>
            {
                requestedUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
                authorizationHeaders.Add(request.Headers.Authorization?.ToString());
                return JsonResponse(
                    """
                    {
                      "data": [
                        {
                          "type": "profiles",
                          "id": "profile-1",
                          "attributes": {
                            "name": "Demo App Store",
                            "platform": "IOS",
                            "uuid": "11111111-2222-3333-4444-555555555555",
                            "profileState": "ACTIVE",
                            "profileType": "IOS_APP_STORE",
                            "createdDate": "2026-06-01T08:00:00Z",
                            "expirationDate": "2027-06-01T08:00:00Z"
                          }
                        }
                      ]
                    }
                    """);
            })));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectProfileLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.True(result.IsSuccess);
        Assert.True(result.IsBundleIdFound);
        Assert.True(result.HasProfiles);
        Assert.Equal("bundle-id-1", result.BundleId?.Id);
        Assert.Equal("profile-1", result.Profiles[0].Id);
        Assert.Equal("Demo App Store", result.Profiles[0].Name);
        Assert.Equal("IOS", result.Profiles[0].Platform);
        Assert.Equal("11111111-2222-3333-4444-555555555555", result.Profiles[0].Uuid);
        Assert.Equal("ACTIVE", result.Profiles[0].ProfileState);
        Assert.Equal("IOS_APP_STORE", result.Profiles[0].ProfileType);
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T08:00:00Z"), result.Profiles[0].CreatedDate);
        Assert.Equal(DateTimeOffset.Parse("2027-06-01T08:00:00Z"), result.Profiles[0].ExpirationDate);
        Assert.Contains("filter%5Bidentifier%5D=com.example.demo", requestedUrls[0], StringComparison.Ordinal);
        Assert.Contains("/v1/bundleIds/bundle-id-1/profiles?limit=10", requestedUrls[1], StringComparison.Ordinal);
        Assert.All(authorizationHeaders, header => Assert.StartsWith("Bearer ", header, StringComparison.Ordinal));
        Assert.DoesNotContain(authorizationHeaders[0]!, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(authorizationHeaders[1]!, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupByBundleIdReturnsSuccessWithoutProfilesWhenBundleHasNoProfiles()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectProfileLookupService(new HttpClient(new QueueHandler(
            _ => BundleIdResponse(),
            _ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectProfileLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.True(result.IsSuccess);
        Assert.True(result.IsBundleIdFound);
        Assert.False(result.HasProfiles);
        Assert.Empty(result.Profiles);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task LookupByBundleIdReturnsSuccessWithoutBundleWhenBundleIdLookupIsEmpty()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var requestCount = 0;
        var service = new AppStoreConnectProfileLookupService(new HttpClient(new QueueHandler(request =>
        {
            requestCount++;
            return JsonResponse("""{"data":[]}""");
        })));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectProfileLookupRequest(
            CreateCredential(key),
            "com.example.missing"));

        Assert.True(result.IsSuccess);
        Assert.False(result.IsBundleIdFound);
        Assert.False(result.HasProfiles);
        Assert.Null(result.BundleId);
        Assert.Empty(result.Profiles);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task LookupByBundleIdRejectsMissingBundleIdentifier()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectProfileLookupService(new HttpClient(new QueueHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectProfileLookupRequest(
            CreateCredential(key),
            " "));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppStoreConnectBundleIdLookupErrorCodes.BundleIdMissing);
    }

    [Fact]
    public async Task LookupByBundleIdReusesCredentialValidation()
    {
        var service = new AppStoreConnectProfileLookupService(new HttpClient(new QueueHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectProfileLookupRequest(
            new AppleApiKeyCredential(" ", "issuer-id", "not used"),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.MissingKeyId);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AppleDeveloperAuthErrorCodes.AppleUnauthorized)]
    [InlineData(HttpStatusCode.Forbidden, AppleDeveloperAuthErrorCodes.AppleForbidden)]
    [InlineData(HttpStatusCode.InternalServerError, AppleDeveloperAuthErrorCodes.AppleApiUnavailable)]
    [InlineData(HttpStatusCode.BadRequest, AppleDeveloperAuthErrorCodes.UnexpectedAppleResponse)]
    public async Task LookupByBundleIdMapsAppleProfileErrors(HttpStatusCode statusCode, string expectedCode)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectProfileLookupService(new HttpClient(new QueueHandler(
            _ => BundleIdResponse(),
            _ => new HttpResponseMessage(statusCode))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectProfileLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public async Task LookupByBundleIdMapsProfileNetworkFailure()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectProfileLookupService(new HttpClient(new QueueHandler(
            _ => BundleIdResponse(),
            _ => throw new HttpRequestException("network down"))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectProfileLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.NetworkFailure);
    }

    [Fact]
    public async Task LookupByBundleIdRejectsMalformedProfileResponse()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectProfileLookupService(new HttpClient(new QueueHandler(
            _ => BundleIdResponse(),
            _ => JsonResponse("""{"data":[{"id":"profile-1"}]}"""))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectProfileLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppStoreConnectProfileLookupErrorCodes.ResponseMalformed);
    }

    [Theory]
    [InlineData(0, "limit=10")]
    [InlineData(-1, "limit=10")]
    [InlineData(1, "limit=1")]
    [InlineData(500, "limit=200")]
    public async Task LookupByBundleIdNormalizesProfileLimit(int requestedLimit, string expectedQueryPart)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string? profilesUrl = null;
        var service = new AppStoreConnectProfileLookupService(new HttpClient(new QueueHandler(
            _ => BundleIdResponse(),
            request =>
            {
                profilesUrl = request.RequestUri?.ToString();
                return JsonResponse("""{"data":[]}""");
            })));

        await service.LookupByBundleIdAsync(new AppStoreConnectProfileLookupRequest(
            CreateCredential(key),
            "com.example.demo",
            requestedLimit));

        Assert.Contains(expectedQueryPart, profilesUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupResultTextDoesNotExposePrivateKeyOrAuthorizationHeader()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credential = CreateCredential(key);
        string? authorizationHeader = null;
        var service = new AppStoreConnectProfileLookupService(new HttpClient(new QueueHandler(
            _ => BundleIdResponse(),
            request =>
            {
                authorizationHeader = request.Headers.Authorization?.ToString();
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            })));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectProfileLookupRequest(
            credential,
            "com.example.demo"));
        var text = result.ToString();

        Assert.DoesNotContain(credential.PrivateKeyPem, text, StringComparison.Ordinal);
        Assert.DoesNotContain(authorizationHeader!, text, StringComparison.Ordinal);
    }

    private static AppleApiKeyCredential CreateCredential(ECDsa key) =>
        new("ABC123DEFG", "issuer-id", key.ExportPkcs8PrivateKeyPem(), "Test account");

    private static HttpResponseMessage BundleIdResponse() =>
        JsonResponse(
            """
            {
              "data": [
                {
                  "type": "bundleIds",
                  "id": "bundle-id-1",
                  "attributes": {
                    "name": "Demo Bundle",
                    "identifier": "com.example.demo",
                    "platform": "IOS",
                    "seedId": "TEAM123456"
                  }
                }
              ]
            }
            """);

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses;

        public QueueHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            this.responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (responses.Count == 0)
            {
                throw new InvalidOperationException("No fake response configured.");
            }

            return Task.FromResult(responses.Dequeue()(request));
        }
    }
}
