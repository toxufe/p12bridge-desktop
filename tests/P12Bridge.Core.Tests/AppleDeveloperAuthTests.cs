using P12Bridge.Core;
using Xunit;

namespace P12Bridge.Core.Tests;

public sealed class AppleDeveloperAuthTests
{
    [Fact]
    public void TokenResultReportsSuccessOnlyWithTokenAndNoErrors()
    {
        var result = AppleDeveloperTokenResult.Success("header.payload.signature", DateTimeOffset.Parse("2026-06-19T00:20:00Z"));

        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeOffset.Parse("2026-06-19T00:20:00Z"), result.ExpiresAt);
    }

    [Fact]
    public void TokenResultFailureHasNoToken()
    {
        var result = AppleDeveloperTokenResult.Failure(new ValidationIssue(
            AppleDeveloperAuthErrorCodes.MissingPrivateKey,
            ValidationSeverity.Error,
            "Private key is required."));

        Assert.False(result.IsSuccess);
        Assert.Equal(string.Empty, result.Token);
    }

    [Fact]
    public void ConnectionResultReportsSuccessOnlyWithoutErrorIssues()
    {
        var success = AppleDeveloperConnectionResult.Success("https://api.appstoreconnect.apple.com/v1/apps?limit=1");
        var failure = AppleDeveloperConnectionResult.Failure(
            "https://api.appstoreconnect.apple.com/v1/apps?limit=1",
            new ValidationIssue(
                AppleDeveloperAuthErrorCodes.AppleUnauthorized,
                ValidationSeverity.Error,
                "Apple rejected the token."));

        Assert.True(success.IsSuccess);
        Assert.False(failure.IsSuccess);
    }

    [Fact]
    public void SensitiveAuthModelsRedactSecretValuesInStringOutput()
    {
        var credential = new AppleApiKeyCredential(
            "ABC123DEFG",
            "issuer-id",
            "-----BEGIN PRIVATE KEY-----\nsecret\n-----END PRIVATE KEY-----",
            "Demo");
        var token = AppleDeveloperTokenResult.Success("header.payload.signature", DateTimeOffset.Parse("2026-06-19T00:20:00Z"));

        Assert.DoesNotContain("secret", credential.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("header.payload.signature", token.ToString(), StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", credential.ToString(), StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", token.ToString(), StringComparison.Ordinal);
    }
}
