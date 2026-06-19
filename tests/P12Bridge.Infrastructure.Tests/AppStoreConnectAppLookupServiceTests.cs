using System.Net;
using System.Security.Cryptography;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class AppStoreConnectAppLookupServiceTests
{
    [Fact]
    public async Task LookupByBundleIdSendsBearerTokenAndReturnsMatchingApp()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string? authorizationHeader = null;
        string? requestedUrl = null;
        var service = new AppStoreConnectAppLookupService(new HttpClient(new FakeHandler(request =>
        {
            authorizationHeader = request.Headers.Authorization?.ToString();
            requestedUrl = request.RequestUri?.ToString();
            return JsonResponse(
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
        })));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectAppLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.True(result.IsSuccess);
        Assert.True(result.IsFound);
        Assert.StartsWith("Bearer ", authorizationHeader, StringComparison.Ordinal);
        Assert.Contains("filter%5BbundleId%5D=com.example.demo", requestedUrl, StringComparison.Ordinal);
        Assert.Equal("1234567890", result.App?.Id);
        Assert.Equal("Demo App", result.App?.Name);
        Assert.Equal("com.example.demo", result.App?.BundleIdentifier);
        Assert.Equal("DEMO-SKU", result.App?.Sku);
        Assert.DoesNotContain(authorizationHeader!, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupByBundleIdReturnsSuccessWithoutAppWhenAppleReturnsEmptyData()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectAppLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectAppLookupRequest(
            CreateCredential(key),
            "com.example.missing"));

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFound);
        Assert.Null(result.App);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task LookupByBundleIdRejectsMissingBundleIdentifier()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectAppLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectAppLookupRequest(
            CreateCredential(key),
            " "));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppStoreConnectAppLookupErrorCodes.BundleIdMissing);
    }

    [Fact]
    public async Task LookupByBundleIdReusesCredentialValidation()
    {
        var service = new AppStoreConnectAppLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectAppLookupRequest(
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
    public async Task LookupByBundleIdMapsAppleErrors(HttpStatusCode statusCode, string expectedCode)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectAppLookupService(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(statusCode))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectAppLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public async Task LookupByBundleIdMapsNetworkFailure()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectAppLookupService(new HttpClient(new FakeHandler(_ =>
            throw new HttpRequestException("network down"))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectAppLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.NetworkFailure);
    }

    [Fact]
    public async Task LookupByBundleIdRejectsMalformedAppleResponse()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectAppLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[{"id":"123"}]}"""))));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectAppLookupRequest(
            CreateCredential(key),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppStoreConnectAppLookupErrorCodes.ResponseMalformed);
    }

    [Fact]
    public async Task LookupResultTextDoesNotExposePrivateKeyOrAuthorizationHeader()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credential = CreateCredential(key);
        string? authorizationHeader = null;
        var service = new AppStoreConnectAppLookupService(new HttpClient(new FakeHandler(request =>
        {
            authorizationHeader = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        })));

        var result = await service.LookupByBundleIdAsync(new AppStoreConnectAppLookupRequest(
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
