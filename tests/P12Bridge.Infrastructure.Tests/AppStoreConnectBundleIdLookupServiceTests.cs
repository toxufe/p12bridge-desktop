using System.Net;
using System.Security.Cryptography;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class AppStoreConnectBundleIdLookupServiceTests
{
    [Fact]
    public async Task LookupByIdentifierSendsBearerTokenAndReturnsMatchingBundleId()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string? authorizationHeader = null;
        string? requestedUrl = null;
        var service = new AppStoreConnectBundleIdLookupService(new HttpClient(new FakeHandler(request =>
        {
            authorizationHeader = request.Headers.Authorization?.ToString();
            requestedUrl = request.RequestUri?.ToString();
            return JsonResponse(
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
        })));

        var result = await service.LookupByIdentifierAsync(new AppStoreConnectBundleIdLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.True(result.IsSuccess);
        Assert.True(result.IsFound);
        Assert.StartsWith("Bearer ", authorizationHeader, StringComparison.Ordinal);
        Assert.Contains("filter%5Bidentifier%5D=com.example.demo", requestedUrl, StringComparison.Ordinal);
        Assert.Equal("bundle-id-1", result.BundleId?.Id);
        Assert.Equal("Demo Bundle", result.BundleId?.Name);
        Assert.Equal("com.example.demo", result.BundleId?.Identifier);
        Assert.Equal("IOS", result.BundleId?.Platform);
        Assert.Equal("TEAM123456", result.BundleId?.SeedId);
        Assert.DoesNotContain(authorizationHeader!, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupByIdentifierReturnsSuccessWithoutBundleWhenAppleReturnsEmptyData()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectBundleIdLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupByIdentifierAsync(new AppStoreConnectBundleIdLookupRequest(
            CreateCredential(key),
            "com.example.missing"));

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFound);
        Assert.Null(result.BundleId);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task LookupByIdentifierRejectsMissingBundleIdentifier()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectBundleIdLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupByIdentifierAsync(new AppStoreConnectBundleIdLookupRequest(
            CreateCredential(key),
            " "));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppStoreConnectBundleIdLookupErrorCodes.BundleIdMissing);
    }

    [Fact]
    public async Task LookupByIdentifierReusesCredentialValidation()
    {
        var service = new AppStoreConnectBundleIdLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupByIdentifierAsync(new AppStoreConnectBundleIdLookupRequest(
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
    public async Task LookupByIdentifierMapsAppleErrors(HttpStatusCode statusCode, string expectedCode)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectBundleIdLookupService(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(statusCode))));

        var result = await service.LookupByIdentifierAsync(new AppStoreConnectBundleIdLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public async Task LookupByIdentifierMapsNetworkFailure()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectBundleIdLookupService(new HttpClient(new FakeHandler(_ =>
            throw new HttpRequestException("network down"))));

        var result = await service.LookupByIdentifierAsync(new AppStoreConnectBundleIdLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.NetworkFailure);
    }

    [Fact]
    public async Task LookupByIdentifierRejectsMalformedAppleResponse()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectBundleIdLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[{"id":"bundle-id-1"}]}"""))));

        var result = await service.LookupByIdentifierAsync(new AppStoreConnectBundleIdLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppStoreConnectBundleIdLookupErrorCodes.ResponseMalformed);
    }

    [Fact]
    public async Task LookupResultTextDoesNotExposePrivateKeyOrAuthorizationHeader()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credential = CreateCredential(key);
        string? authorizationHeader = null;
        var service = new AppStoreConnectBundleIdLookupService(new HttpClient(new FakeHandler(request =>
        {
            authorizationHeader = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        })));

        var result = await service.LookupByIdentifierAsync(new AppStoreConnectBundleIdLookupRequest(
            credential,
            "com.example.demo"));
        var text = result.ToString();

        Assert.DoesNotContain(credential.PrivateKeyPem, text, StringComparison.Ordinal);
        Assert.DoesNotContain(authorizationHeader!, text, StringComparison.Ordinal);
    }

    private static AppleApiKeyCredential CreateCredential(ECDsa key) =>
        new("ABC123DEFG", "issuer-id", key.ExportPkcs8PrivateKeyPem(), "Test account");

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> send;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        {
            this.send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
