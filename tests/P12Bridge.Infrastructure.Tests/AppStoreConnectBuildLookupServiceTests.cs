using System.Net;
using System.Security.Cryptography;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class AppStoreConnectBuildLookupServiceTests
{
    [Fact]
    public async Task LookupByBundleIdReturnsBuildsForMatchingApp()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var requestedUrls = new List<string>();
        var authorizationHeaders = new List<string?>();
        var service = new AppStoreConnectBuildLookupService(new HttpClient(new QueueHandler(
            request =>
            {
                requestedUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
                authorizationHeaders.Add(request.Headers.Authorization?.ToString());
                return AppResponse();
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
                          "type": "builds",
                          "id": "build-1",
                          "attributes": {
                            "version": "1.2.3",
                            "processingState": "VALID",
                            "uploadedDate": "2026-06-20T10:15:30Z",
                            "expired": false
                          }
                        }
                      ]
                    }
                    """);
            })));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectBuildLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.True(result.IsSuccess);
        Assert.True(result.IsAppFound);
        Assert.True(result.HasBuilds);
        Assert.Equal("1234567890", result.App?.Id);
        Assert.Equal("build-1", result.Builds[0].Id);
        Assert.Equal("1.2.3", result.Builds[0].Version);
        Assert.Equal("VALID", result.Builds[0].ProcessingState);
        Assert.Equal(DateTimeOffset.Parse("2026-06-20T10:15:30Z"), result.Builds[0].UploadedDate);
        Assert.False(result.Builds[0].Expired);
        Assert.Contains("filter%5BbundleId%5D=com.example.demo", requestedUrls[0], StringComparison.Ordinal);
        Assert.Contains("/v1/apps/1234567890/builds?limit=5&sort=-uploadedDate", requestedUrls[1], StringComparison.Ordinal);
        Assert.All(authorizationHeaders, header => Assert.StartsWith("Bearer ", header, StringComparison.Ordinal));
        Assert.DoesNotContain(authorizationHeaders[0]!, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(authorizationHeaders[1]!, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupByBundleIdReturnsSuccessWithoutBuildsWhenAppHasNoBuilds()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectBuildLookupService(new HttpClient(new QueueHandler(
            _ => AppResponse(),
            _ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectBuildLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.True(result.IsSuccess);
        Assert.True(result.IsAppFound);
        Assert.False(result.HasBuilds);
        Assert.Empty(result.Builds);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task LookupByBundleIdReturnsSuccessWithoutAppWhenAppLookupIsEmpty()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var requestCount = 0;
        var service = new AppStoreConnectBuildLookupService(new HttpClient(new QueueHandler(request =>
        {
            requestCount++;
            return JsonResponse("""{"data":[]}""");
        })));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectBuildLookupRequest(
            CreateCredential(key),
            "com.example.missing"));

        Assert.True(result.IsSuccess);
        Assert.False(result.IsAppFound);
        Assert.False(result.HasBuilds);
        Assert.Null(result.App);
        Assert.Empty(result.Builds);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task LookupByBundleIdRejectsMissingBundleIdentifier()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectBuildLookupService(new HttpClient(new QueueHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectBuildLookupRequest(
            CreateCredential(key),
            " "));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppStoreConnectAppLookupErrorCodes.BundleIdMissing);
    }

    [Fact]
    public async Task LookupByBundleIdReusesCredentialValidation()
    {
        var service = new AppStoreConnectBuildLookupService(new HttpClient(new QueueHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectBuildLookupRequest(
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
    public async Task LookupByBundleIdMapsAppleBuildErrors(HttpStatusCode statusCode, string expectedCode)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectBuildLookupService(new HttpClient(new QueueHandler(
            _ => AppResponse(),
            _ => new HttpResponseMessage(statusCode))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectBuildLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public async Task LookupByBundleIdMapsBuildNetworkFailure()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectBuildLookupService(new HttpClient(new QueueHandler(
            _ => AppResponse(),
            _ => throw new HttpRequestException("network down"))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectBuildLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.NetworkFailure);
    }

    [Fact]
    public async Task LookupByBundleIdRejectsMalformedBuildResponse()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectBuildLookupService(new HttpClient(new QueueHandler(
            _ => AppResponse(),
            _ => JsonResponse("""{"data":[{"id":"build-1"}]}"""))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectBuildLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppStoreConnectBuildLookupErrorCodes.ResponseMalformed);
    }

    [Theory]
    [InlineData(0, "limit=5")]
    [InlineData(-1, "limit=5")]
    [InlineData(1, "limit=1")]
    [InlineData(50, "limit=10")]
    public async Task LookupByBundleIdNormalizesBuildLimit(int requestedLimit, string expectedQueryPart)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string? buildsUrl = null;
        var service = new AppStoreConnectBuildLookupService(new HttpClient(new QueueHandler(
            _ => AppResponse(),
            request =>
            {
                buildsUrl = request.RequestUri?.ToString();
                return JsonResponse("""{"data":[]}""");
            })));

        await service.LookupByBundleIdAsync(new AppStoreConnectBuildLookupRequest(
            CreateCredential(key),
            "com.example.demo",
            requestedLimit));

        Assert.Contains(expectedQueryPart, buildsUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupResultTextDoesNotExposePrivateKeyOrAuthorizationHeader()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credential = CreateCredential(key);
        string? authorizationHeader = null;
        var service = new AppStoreConnectBuildLookupService(new HttpClient(new QueueHandler(
            _ => AppResponse(),
            request =>
            {
                authorizationHeader = request.Headers.Authorization?.ToString();
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            })));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectBuildLookupRequest(
            credential,
            "com.example.demo"));
        var text = result.ToString();

        Assert.DoesNotContain(credential.PrivateKeyPem, text, StringComparison.Ordinal);
        Assert.DoesNotContain(authorizationHeader!, text, StringComparison.Ordinal);
    }

    private static AppleApiKeyCredential CreateCredential(ECDsa key) =>
        new("ABC123DEFG", "issuer-id", key.ExportPkcs8PrivateKeyPem(), "Test account");

    private static HttpResponseMessage AppResponse() =>
        JsonResponse(
            """
            {
              "data": [
                {
                  "type": "apps",
                  "id": "1234567890",
                  "attributes": {
                    "name": "Demo App",
                    "bundleId": "com.example.demo",
                    "sku": "DEMO-SKU"
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
