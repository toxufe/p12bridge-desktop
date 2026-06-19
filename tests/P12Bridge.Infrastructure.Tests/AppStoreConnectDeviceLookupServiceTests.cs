using System.Net;
using System.Security.Cryptography;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class AppStoreConnectDeviceLookupServiceTests
{
    [Fact]
    public async Task LookupReturnsDevices()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string? authorizationHeader = null;
        string? requestedUrl = null;
        var service = new AppStoreConnectDeviceLookupService(new HttpClient(new FakeHandler(request =>
        {
            authorizationHeader = request.Headers.Authorization?.ToString();
            requestedUrl = request.RequestUri?.ToString();
            return JsonResponse(
                """
                {
                  "data": [
                    {
                      "type": "devices",
                      "id": "device-1",
                      "attributes": {
                        "name": "Alice iPhone",
                        "platform": "IOS",
                        "udid": "00008110-001C195E0E91801E",
                        "deviceClass": "IPHONE",
                        "status": "ENABLED",
                        "model": "iPhone 15",
                        "addedDate": "2026-06-01T08:00:00Z"
                      }
                    }
                  ]
                }
                """);
        })));

        var result = await service.LookupAsync(new AppStoreConnectDeviceLookupRequest(CreateCredential(key)));

        Assert.True(result.IsSuccess);
        Assert.True(result.HasDevices);
        Assert.StartsWith("Bearer ", authorizationHeader, StringComparison.Ordinal);
        Assert.Contains("/v1/devices?limit=10", requestedUrl, StringComparison.Ordinal);
        Assert.Equal("device-1", result.Devices[0].Id);
        Assert.Equal("Alice iPhone", result.Devices[0].Name);
        Assert.Equal("IOS", result.Devices[0].Platform);
        Assert.Equal("00008110-001C195E0E91801E", result.Devices[0].Udid);
        Assert.Equal("IPHONE", result.Devices[0].DeviceClass);
        Assert.Equal("ENABLED", result.Devices[0].Status);
        Assert.Equal("iPhone 15", result.Devices[0].Model);
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T08:00:00Z"), result.Devices[0].AddedDate);
        Assert.DoesNotContain(authorizationHeader!, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupReturnsSuccessWithoutDevicesWhenAppleReturnsEmptyData()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectDeviceLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupAsync(new AppStoreConnectDeviceLookupRequest(CreateCredential(key)));

        Assert.True(result.IsSuccess);
        Assert.False(result.HasDevices);
        Assert.Empty(result.Devices);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task LookupReusesCredentialValidation()
    {
        var service = new AppStoreConnectDeviceLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupAsync(new AppStoreConnectDeviceLookupRequest(
            new AppleApiKeyCredential(" ", "issuer-id", "not used")));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.MissingKeyId);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AppleDeveloperAuthErrorCodes.AppleUnauthorized)]
    [InlineData(HttpStatusCode.Forbidden, AppleDeveloperAuthErrorCodes.AppleForbidden)]
    [InlineData(HttpStatusCode.InternalServerError, AppleDeveloperAuthErrorCodes.AppleApiUnavailable)]
    [InlineData(HttpStatusCode.BadRequest, AppleDeveloperAuthErrorCodes.UnexpectedAppleResponse)]
    public async Task LookupMapsAppleErrors(HttpStatusCode statusCode, string expectedCode)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectDeviceLookupService(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(statusCode))));

        var result = await service.LookupAsync(new AppStoreConnectDeviceLookupRequest(CreateCredential(key)));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public async Task LookupMapsNetworkFailure()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectDeviceLookupService(new HttpClient(new FakeHandler(_ =>
            throw new HttpRequestException("network down"))));

        var result = await service.LookupAsync(new AppStoreConnectDeviceLookupRequest(CreateCredential(key)));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.NetworkFailure);
    }

    [Fact]
    public async Task LookupRejectsMalformedAppleResponse()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectDeviceLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[{"id":"device-1"}]}"""))));

        var result = await service.LookupAsync(new AppStoreConnectDeviceLookupRequest(CreateCredential(key)));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppStoreConnectDeviceLookupErrorCodes.ResponseMalformed);
    }

    [Theory]
    [InlineData(0, "limit=10")]
    [InlineData(-1, "limit=10")]
    [InlineData(1, "limit=1")]
    [InlineData(500, "limit=200")]
    public async Task LookupNormalizesLimit(int requestedLimit, string expectedQueryPart)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string? requestedUrl = null;
        var service = new AppStoreConnectDeviceLookupService(new HttpClient(new FakeHandler(request =>
        {
            requestedUrl = request.RequestUri?.ToString();
            return JsonResponse("""{"data":[]}""");
        })));

        await service.LookupAsync(new AppStoreConnectDeviceLookupRequest(CreateCredential(key), requestedLimit));

        Assert.Contains(expectedQueryPart, requestedUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupResultTextDoesNotExposePrivateKeyOrAuthorizationHeader()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credential = CreateCredential(key);
        string? authorizationHeader = null;
        var service = new AppStoreConnectDeviceLookupService(new HttpClient(new FakeHandler(request =>
        {
            authorizationHeader = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        })));

        var result = await service.LookupAsync(new AppStoreConnectDeviceLookupRequest(credential));
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
