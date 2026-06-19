using P12Bridge.Core;
using Xunit;

namespace P12Bridge.Core.Tests;

public sealed class ProvisioningProfileTests
{
    [Fact]
    public void ParseResultReportsSuccessOnlyWhenProfileHasNoErrors()
    {
        var profile = new ProvisioningProfile(
            "profile-uuid",
            "Demo",
            "TEAM123456",
            "TEAM123456.com.example.app",
            "com.example.app",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            ProvisioningProfileType.AppStore,
            ProvisioningProfileStatus.Active,
            0,
            Array.Empty<string>());

        var result = ProvisioningProfileParseResult.Success(profile);

        Assert.True(result.IsSuccess);
        Assert.Equal("com.example.app", result.Profile?.BundleIdentifier);
    }

    [Fact]
    public void ParseResultFailureHasNoProfile()
    {
        var result = ProvisioningProfileParseResult.Failure(new ValidationIssue(
            ProvisioningProfileErrorCodes.PlistNotFound,
            ValidationSeverity.Error,
            "Missing plist."));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Profile);
    }
}
