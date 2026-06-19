using System.Net;
using System.Security.Cryptography;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class AppStoreConnectCertificateLookupServiceTests
{
    [Fact]
    public async Task LookupReturnsCertificatesAndDoesNotExposeCertificateContent()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string? authorizationHeader = null;
        string? requestedUrl = null;
        const string certificateContent = "MIIC_PUBLIC_CERTIFICATE_CONTENT";
        var service = new AppStoreConnectCertificateLookupService(new HttpClient(new FakeHandler(request =>
        {
            authorizationHeader = request.Headers.Authorization?.ToString();
            requestedUrl = request.RequestUri?.ToString();
            return JsonResponse(
                $$"""
                {
                  "data": [
                    {
                      "type": "certificates",
                      "id": "certificate-1",
                      "attributes": {
                        "name": "iOS Distribution",
                        "displayName": "Apple Distribution: Demo Team",
                        "certificateType": "IOS_DISTRIBUTION",
                        "serialNumber": "ABCDEF123456",
                        "platform": "IOS",
                        "expirationDate": "2027-06-01T08:00:00Z",
                        "activated": true,
                        "certificateContent": "{{certificateContent}}"
                      }
                    }
                  ]
                }
                """);
        })));

        var result = await service.LookupAsync(new AppStoreConnectCertificateLookupRequest(CreateCredential(key)));

        Assert.True(result.IsSuccess);
        Assert.True(result.HasCertificates);
        Assert.StartsWith("Bearer ", authorizationHeader, StringComparison.Ordinal);
        Assert.Contains("/v1/certificates?limit=10", requestedUrl, StringComparison.Ordinal);
        Assert.Equal("certificate-1", result.Certificates[0].Id);
        Assert.Equal("iOS Distribution", result.Certificates[0].Name);
        Assert.Equal("Apple Distribution: Demo Team", result.Certificates[0].DisplayName);
        Assert.Equal("IOS_DISTRIBUTION", result.Certificates[0].CertificateType);
        Assert.Equal("ABCDEF123456", result.Certificates[0].SerialNumber);
        Assert.Equal("IOS", result.Certificates[0].Platform);
        Assert.Equal(DateTimeOffset.Parse("2027-06-01T08:00:00Z"), result.Certificates[0].ExpirationDate);
        Assert.True(result.Certificates[0].Activated);
        Assert.DoesNotContain(certificateContent, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(authorizationHeader!, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupReturnsSuccessWithoutCertificatesWhenAppleReturnsEmptyData()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectCertificateLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupAsync(new AppStoreConnectCertificateLookupRequest(CreateCredential(key)));

        Assert.True(result.IsSuccess);
        Assert.False(result.HasCertificates);
        Assert.Empty(result.Certificates);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task LookupReusesCredentialValidation()
    {
        var service = new AppStoreConnectCertificateLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[]}"""))));

        var result = await service.LookupAsync(new AppStoreConnectCertificateLookupRequest(
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
        var service = new AppStoreConnectCertificateLookupService(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(statusCode))));

        var result = await service.LookupAsync(new AppStoreConnectCertificateLookupRequest(CreateCredential(key)));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public async Task LookupMapsNetworkFailure()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectCertificateLookupService(new HttpClient(new FakeHandler(_ =>
            throw new HttpRequestException("network down"))));

        var result = await service.LookupAsync(new AppStoreConnectCertificateLookupRequest(CreateCredential(key)));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.NetworkFailure);
    }

    [Fact]
    public async Task LookupRejectsMalformedAppleResponse()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppStoreConnectCertificateLookupService(new HttpClient(new FakeHandler(_ => JsonResponse("""{"data":[{"id":"certificate-1"}]}"""))));

        var result = await service.LookupAsync(new AppStoreConnectCertificateLookupRequest(CreateCredential(key)));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppStoreConnectCertificateLookupErrorCodes.ResponseMalformed);
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
        var service = new AppStoreConnectCertificateLookupService(new HttpClient(new FakeHandler(request =>
        {
            requestedUrl = request.RequestUri?.ToString();
            return JsonResponse("""{"data":[]}""");
        })));

        await service.LookupAsync(new AppStoreConnectCertificateLookupRequest(CreateCredential(key), requestedLimit));

        Assert.Contains(expectedQueryPart, requestedUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupResultTextDoesNotExposePrivateKeyOrAuthorizationHeader()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credential = CreateCredential(key);
        string? authorizationHeader = null;
        var service = new AppStoreConnectCertificateLookupService(new HttpClient(new FakeHandler(request =>
        {
            authorizationHeader = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        })));

        var result = await service.LookupAsync(new AppStoreConnectCertificateLookupRequest(credential));
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
