using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class AppleDeveloperAuthServiceTests
{
    [Fact]
    public void CreateTokenGeneratesJwtWithExpectedHeaderAndPayload()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credential = CreateCredential(key);
        var service = new AppleDeveloperAuthService();
        var now = DateTimeOffset.Parse("2026-06-19T00:00:00Z");

        var result = service.CreateToken(credential, now);

        Assert.True(result.IsSuccess);
        Assert.Equal(now.AddMinutes(20), result.ExpiresAt);

        var parts = result.Token.Split('.');
        Assert.Equal(3, parts.Length);

        using var header = JsonDocument.Parse(DecodeBase64Url(parts[0]));
        using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
        var signature = Convert.FromBase64String(PadBase64Url(parts[2]).Replace('-', '+').Replace('_', '/'));

        Assert.Equal("ES256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("ABC123DEFG", header.RootElement.GetProperty("kid").GetString());
        Assert.Equal("JWT", header.RootElement.GetProperty("typ").GetString());
        Assert.Equal("issuer-id", payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal("appstoreconnect-v1", payload.RootElement.GetProperty("aud").GetString());
        Assert.Equal(now.ToUnixTimeSeconds(), payload.RootElement.GetProperty("iat").GetInt64());
        Assert.Equal(now.AddMinutes(20).ToUnixTimeSeconds(), payload.RootElement.GetProperty("exp").GetInt64());
        Assert.Equal(64, signature.Length);
    }

    [Fact]
    public void CreateTokenRejectsMissingKeyId()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppleDeveloperAuthService();

        var result = service.CreateToken(CreateCredential(key, keyId: " "));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.MissingKeyId);
    }

    [Fact]
    public void CreateTokenRejectsMissingIssuerId()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppleDeveloperAuthService();

        var result = service.CreateToken(CreateCredential(key, issuerId: string.Empty));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.MissingIssuerId);
    }

    [Fact]
    public void CreateTokenRejectsMissingPrivateKey()
    {
        var service = new AppleDeveloperAuthService();

        var result = service.CreateToken(new AppleApiKeyCredential("ABC123DEFG", "issuer-id", string.Empty));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.MissingPrivateKey);
    }

    [Fact]
    public void CreateTokenRejectsInvalidPrivateKey()
    {
        var service = new AppleDeveloperAuthService();

        var result = service.CreateToken(new AppleApiKeyCredential(
            "ABC123DEFG",
            "issuer-id",
            "not a private key"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.InvalidPrivateKey);
    }

    [Fact]
    public async Task CheckConnectionSendsBearerTokenAndReportsSuccess()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string? authorizationHeader = null;
        var service = new AppleDeveloperAuthService(new HttpClient(new FakeHandler(request =>
        {
            authorizationHeader = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));

        var result = await service.CheckConnectionAsync(CreateCredential(key));

        Assert.True(result.IsSuccess);
        Assert.StartsWith("Bearer ", authorizationHeader, StringComparison.Ordinal);
        Assert.DoesNotContain(authorizationHeader!, result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AppleDeveloperAuthErrorCodes.AppleUnauthorized)]
    [InlineData(HttpStatusCode.Forbidden, AppleDeveloperAuthErrorCodes.AppleForbidden)]
    [InlineData(HttpStatusCode.InternalServerError, AppleDeveloperAuthErrorCodes.AppleApiUnavailable)]
    [InlineData(HttpStatusCode.BadRequest, AppleDeveloperAuthErrorCodes.UnexpectedAppleResponse)]
    public async Task CheckConnectionMapsAppleResponsesToStableErrors(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppleDeveloperAuthService(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(statusCode))));

        var result = await service.CheckConnectionAsync(CreateCredential(key));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public async Task CheckConnectionMapsNetworkFailureToStableError()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AppleDeveloperAuthService(new HttpClient(new FakeHandler(_ =>
            throw new HttpRequestException("network down"))));

        var result = await service.CheckConnectionAsync(CreateCredential(key));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.NetworkFailure);
    }

    [Fact]
    public async Task CheckConnectionDoesNotExposePrivateKeyOrTokenInResultText()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credential = CreateCredential(key);
        string? authorizationHeader = null;
        var service = new AppleDeveloperAuthService(new HttpClient(new FakeHandler(request =>
        {
            authorizationHeader = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        })));

        var result = await service.CheckConnectionAsync(credential);
        var resultText = result.ToString();

        Assert.DoesNotContain(credential.PrivateKeyPem, resultText, StringComparison.Ordinal);
        Assert.DoesNotContain(authorizationHeader!, resultText, StringComparison.Ordinal);
    }

    private static AppleApiKeyCredential CreateCredential(
        ECDsa key,
        string keyId = "ABC123DEFG",
        string issuerId = "issuer-id") =>
        new(keyId, issuerId, key.ExportPkcs8PrivateKeyPem(), "Test account");

    private static string DecodeBase64Url(string value) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64Url(value).Replace('-', '+').Replace('_', '/')));

    private static string PadBase64Url(string value) =>
        value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '=');

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
