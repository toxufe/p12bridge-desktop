using P12Bridge.Core;
using Xunit;

namespace P12Bridge.Core.Tests;

public sealed class UploadReadinessEvaluatorTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"p12bridge-readiness-{Guid.NewGuid():N}");
    private readonly UploadReadinessEvaluator evaluator = new();

    public UploadReadinessEvaluatorTests()
    {
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void EvaluateReportsReadyWhenAppStoreIpaAndProfilesMatch()
    {
        var profile = AppStoreProfile();
        var ipa = Metadata(profile);

        var result = evaluator.Evaluate(Request(ipa, profile));

        Assert.True(result.IsReady);
        Assert.Equal(UploadReadinessStatus.Ready, result.Status);
        Assert.Empty(result.Issues);
        Assert.All(result.Checks, check => Assert.Equal(UploadReadinessCheckStatus.Passed, check.Status));
    }

    [Fact]
    public void EvaluateReportsReadyWithWarningWhenImportedProfileIsMissing()
    {
        var profile = AppStoreProfile();
        var ipa = Metadata(profile);

        var result = evaluator.Evaluate(Request(ipa));

        Assert.False(result.IsReady);
        Assert.Equal(UploadReadinessStatus.ReadyWithWarnings, result.Status);
        Assert.Contains(result.Checks, check =>
            check.Code == UploadReadinessErrorCodes.ImportedProfileMissing
            && check.Status == UploadReadinessCheckStatus.Warning);
        Assert.Contains(result.Issues, issue =>
            issue.Code == UploadReadinessErrorCodes.ImportedProfileMissing
            && issue.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void EvaluateBlocksWhenIpaMetadataIsMissing()
    {
        var result = evaluator.Evaluate(Request(null));

        Assert.Equal(UploadReadinessStatus.Blocked, result.Status);
        AssertBlocked(result, UploadReadinessErrorCodes.IpaMetadataMissing);
    }

    [Fact]
    public void EvaluateBlocksWhenEmbeddedProfileIsMissing()
    {
        var ipa = Metadata(embeddedProfile: null, hasEmbeddedProfile: false);

        var result = evaluator.Evaluate(Request(ipa));

        Assert.Equal(UploadReadinessStatus.Blocked, result.Status);
        AssertBlocked(result, UploadReadinessErrorCodes.EmbeddedProfileMissing);
    }

    [Fact]
    public void EvaluateBlocksWhenBundleIdDoesNotMatchEmbeddedProfile()
    {
        var profile = AppStoreProfile(bundleIdentifier: "com.example.other");
        var ipa = Metadata(profile, bundleIdentifier: "com.example.demo");

        var result = evaluator.Evaluate(Request(ipa, profile));

        Assert.Equal(UploadReadinessStatus.Blocked, result.Status);
        AssertBlocked(result, UploadReadinessErrorCodes.EmbeddedProfileBundleIdMismatch);
    }

    [Fact]
    public void EvaluateBlocksWhenEmbeddedProfileIsExpired()
    {
        var profile = AppStoreProfile(status: ProvisioningProfileStatus.Expired);
        var ipa = Metadata(profile);

        var result = evaluator.Evaluate(Request(ipa, profile));

        Assert.Equal(UploadReadinessStatus.Blocked, result.Status);
        AssertBlocked(result, UploadReadinessErrorCodes.EmbeddedProfileExpired);
    }

    [Theory]
    [InlineData(ProvisioningProfileType.Development)]
    [InlineData(ProvisioningProfileType.AdHoc)]
    public void EvaluateBlocksWhenEmbeddedProfileIsNotAppStore(ProvisioningProfileType type)
    {
        var profile = AppStoreProfile(type: type);
        var ipa = Metadata(profile);

        var result = evaluator.Evaluate(Request(ipa, profile));

        Assert.Equal(UploadReadinessStatus.Blocked, result.Status);
        AssertBlocked(result, UploadReadinessErrorCodes.EmbeddedProfileTypeInvalid);
    }

    [Fact]
    public void EvaluateBlocksWhenImportedProfileBundleIdDiffers()
    {
        var embeddedProfile = AppStoreProfile();
        var importedProfile = AppStoreProfile(uuid: "imported-uuid", bundleIdentifier: "com.example.other");
        var ipa = Metadata(embeddedProfile);

        var result = evaluator.Evaluate(Request(ipa, importedProfile));

        Assert.Equal(UploadReadinessStatus.Blocked, result.Status);
        AssertBlocked(result, UploadReadinessErrorCodes.ImportedProfileBundleIdMismatch);
    }

    [Fact]
    public void EvaluateBlocksWhenImportedProfileTeamDiffers()
    {
        var embeddedProfile = AppStoreProfile();
        var importedProfile = AppStoreProfile(uuid: "imported-uuid", teamId: "OTHERTEAM");
        var ipa = Metadata(embeddedProfile);

        var result = evaluator.Evaluate(Request(ipa, importedProfile));

        Assert.Equal(UploadReadinessStatus.Blocked, result.Status);
        AssertBlocked(result, UploadReadinessErrorCodes.ImportedProfileTeamIdMismatch);
    }

    [Fact]
    public void EvaluateWarnsWhenImportedProfileUuidDiffersButMetadataMatches()
    {
        var embeddedProfile = AppStoreProfile(uuid: "embedded-uuid");
        var importedProfile = AppStoreProfile(uuid: "imported-uuid");
        var ipa = Metadata(embeddedProfile);

        var result = evaluator.Evaluate(Request(ipa, importedProfile));

        Assert.Equal(UploadReadinessStatus.ReadyWithWarnings, result.Status);
        Assert.Contains(result.Checks, check =>
            check.Code == UploadReadinessErrorCodes.ImportedProfileUuidMismatch
            && check.Status == UploadReadinessCheckStatus.Warning);
        Assert.DoesNotContain(result.Issues, issue => issue.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void EvaluateBlocksMissingReadableIpaFieldsAndSignatureMarker()
    {
        var profile = AppStoreProfile();
        var ipa = Metadata(
            profile,
            bundleIdentifier: " ",
            shortVersion: string.Empty,
            buildVersion: " ",
            hasCodeResources: false);

        var result = evaluator.Evaluate(Request(ipa, profile));

        Assert.Equal(UploadReadinessStatus.Blocked, result.Status);
        AssertBlocked(result, UploadReadinessErrorCodes.IpaBundleIdMissing);
        AssertBlocked(result, UploadReadinessErrorCodes.IpaVersionMissing);
        AssertBlocked(result, UploadReadinessErrorCodes.IpaBuildMissing);
        AssertBlocked(result, UploadReadinessErrorCodes.IpaSignatureMarkerMissing);
    }

    [Fact]
    public void EvaluateBlocksWhenPackagePathIsBlank()
    {
        var profile = AppStoreProfile();
        var ipa = Metadata(profile);

        var result = evaluator.Evaluate(Request(ipa, profile, " "));

        Assert.Equal(UploadReadinessStatus.Blocked, result.Status);
        AssertBlocked(result, UploadReadinessErrorCodes.PackagePathMissing);
    }

    [Fact]
    public void EvaluateBlocksWhenPackagePathDoesNotExist()
    {
        var profile = AppStoreProfile();
        var ipa = Metadata(profile);
        var missingPath = Path.Combine(tempDirectory, "missing.ipa");

        var result = evaluator.Evaluate(Request(ipa, profile, missingPath));

        Assert.Equal(UploadReadinessStatus.Blocked, result.Status);
        AssertBlocked(result, UploadReadinessErrorCodes.PackageNotFound);
    }

    [Fact]
    public void EvaluatePassesWhenPackagePathExists()
    {
        var profile = AppStoreProfile();
        var ipa = Metadata(profile);

        var result = evaluator.Evaluate(Request(ipa, profile));

        Assert.Contains(result.Checks, check =>
            check.Code == UploadReadinessErrorCodes.PackageNotFound
            && check.Status == UploadReadinessCheckStatus.Passed);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static void AssertBlocked(UploadReadinessResult result, string code)
    {
        Assert.Contains(result.Checks, check =>
            check.Code == code && check.Status == UploadReadinessCheckStatus.Blocked);
        Assert.Contains(result.Issues, issue =>
            issue.Code == code && issue.Severity == ValidationSeverity.Error);
    }

    private UploadReadinessRequest Request(
        IpaMetadata? ipa,
        ProvisioningProfile? importedProfile = null,
        string? packagePath = null) =>
        new(UploadTarget.AppStore, ipa, importedProfile, packagePath ?? ExistingIpaPath());

    private string ExistingIpaPath()
    {
        var path = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.ipa");
        File.WriteAllText(path, "ipa");
        return path;
    }

    private static IpaMetadata Metadata(
        ProvisioningProfile? embeddedProfile,
        string bundleIdentifier = "com.example.demo",
        string shortVersion = "1.0",
        string buildVersion = "42",
        bool hasEmbeddedProfile = true,
        bool hasCodeResources = true) =>
        new(
            1024,
            "Payload/Demo.app",
            bundleIdentifier,
            shortVersion,
            buildVersion,
            "Demo",
            hasEmbeddedProfile,
            embeddedProfile,
            new IpaSignaturePresence(hasCodeResources, hasEmbeddedProfile));

    private static ProvisioningProfile AppStoreProfile(
        string uuid = "profile-uuid",
        string teamId = "TEAM123456",
        string bundleIdentifier = "com.example.demo",
        ProvisioningProfileType type = ProvisioningProfileType.AppStore,
        ProvisioningProfileStatus status = ProvisioningProfileStatus.Active) =>
        new(
            uuid,
            "Demo App Store",
            teamId,
            $"{teamId}.{bundleIdentifier}",
            bundleIdentifier,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
            type,
            status,
            0,
            Array.Empty<string>());
}
