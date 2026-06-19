using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class AppStoreConnectRemotePreflightServiceTests
{
    [Fact]
    public async Task CheckReturnsAggregateSummary()
    {
        var service = new AppStoreConnectRemotePreflightService(
            new FakeAppLookup(AppStoreConnectAppLookupResult.Success("apps", App())),
            new FakeBundleIdLookup(AppStoreConnectBundleIdLookupResult.Success("bundleIds", BundleId())),
            new FakeBuildLookup(AppStoreConnectBuildLookupResult.Success("apps", "builds", App(), new[] { Build("build-1"), Build("build-2") })),
            new FakeProfileLookup(AppStoreConnectProfileLookupResult.Success("bundleIds", "profiles", BundleId(), new[] { Profile("profile-1") })),
            new FakeCertificateLookup(AppStoreConnectCertificateLookupResult.Success("certificates", new[] { Certificate("certificate-1") })),
            new FakeDeviceLookup(AppStoreConnectDeviceLookupResult.Success("devices", new[] { Device("device-1"), Device("device-2"), Device("device-3") })));

        var result = await service.CheckAsync(new AppStoreConnectRemotePreflightRequest(
            Credential(),
            " com.example.demo "));

        Assert.True(result.IsSuccess);
        Assert.False(result.HasWarnings);
        Assert.Equal("com.example.demo", result.BundleIdentifier);
        Assert.True(result.Summary.AppFound);
        Assert.True(result.Summary.BundleIdFound);
        Assert.Equal(2, result.Summary.BuildCount);
        Assert.Equal(1, result.Summary.ProfileCount);
        Assert.Equal(1, result.Summary.CertificateCount);
        Assert.Equal(3, result.Summary.DeviceCount);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task CheckRejectsMissingBundleIdentifier()
    {
        var service = new AppStoreConnectRemotePreflightService(
            new FakeAppLookup(AppStoreConnectAppLookupResult.Success("apps", App())),
            new FakeBundleIdLookup(AppStoreConnectBundleIdLookupResult.Success("bundleIds", BundleId())),
            new FakeBuildLookup(AppStoreConnectBuildLookupResult.Success("apps", "builds", App(), Array.Empty<AppStoreConnectBuild>())),
            new FakeProfileLookup(AppStoreConnectProfileLookupResult.Success("bundleIds", "profiles", BundleId(), Array.Empty<AppStoreConnectProfile>())),
            new FakeCertificateLookup(AppStoreConnectCertificateLookupResult.Success("certificates", Array.Empty<AppStoreConnectCertificate>())),
            new FakeDeviceLookup(AppStoreConnectDeviceLookupResult.Success("devices", Array.Empty<AppStoreConnectDevice>())));

        var result = await service.CheckAsync(new AppStoreConnectRemotePreflightRequest(Credential(), " "));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppStoreConnectRemotePreflightErrorCodes.BundleIdMissing);
    }

    [Fact]
    public async Task CheckReturnsWarningsWhenRemoteAppOrBundleIdAreMissing()
    {
        var service = new AppStoreConnectRemotePreflightService(
            new FakeAppLookup(AppStoreConnectAppLookupResult.Success("apps", null)),
            new FakeBundleIdLookup(AppStoreConnectBundleIdLookupResult.Success("bundleIds", null)),
            new FakeBuildLookup(AppStoreConnectBuildLookupResult.Success("apps", "builds", null, Array.Empty<AppStoreConnectBuild>())),
            new FakeProfileLookup(AppStoreConnectProfileLookupResult.Success("bundleIds", "profiles", null, Array.Empty<AppStoreConnectProfile>())),
            new FakeCertificateLookup(AppStoreConnectCertificateLookupResult.Success("certificates", Array.Empty<AppStoreConnectCertificate>())),
            new FakeDeviceLookup(AppStoreConnectDeviceLookupResult.Success("devices", Array.Empty<AppStoreConnectDevice>())));

        var result = await service.CheckAsync(new AppStoreConnectRemotePreflightRequest(
            Credential(),
            "com.example.missing"));

        Assert.True(result.IsSuccess);
        Assert.True(result.HasWarnings);
        Assert.False(result.Summary.AppFound);
        Assert.False(result.Summary.BundleIdFound);
        Assert.Contains(result.Issues, issue =>
            issue.Code == AppStoreConnectRemotePreflightErrorCodes.AppMissing
            && issue.Severity == ValidationSeverity.Warning);
        Assert.Contains(result.Issues, issue =>
            issue.Code == AppStoreConnectRemotePreflightErrorCodes.BundleIdNotRegistered
            && issue.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public async Task CheckAggregatesDownstreamFailures()
    {
        var unauthorized = new ValidationIssue(
            AppleDeveloperAuthErrorCodes.AppleUnauthorized,
            ValidationSeverity.Error,
            "Unauthorized.",
            "Check credentials.");
        var malformed = new ValidationIssue(
            AppStoreConnectDeviceLookupErrorCodes.ResponseMalformed,
            ValidationSeverity.Error,
            "Malformed.",
            "Retry.");
        var service = new AppStoreConnectRemotePreflightService(
            new FakeAppLookup(AppStoreConnectAppLookupResult.Failure("apps", unauthorized)),
            new FakeBundleIdLookup(AppStoreConnectBundleIdLookupResult.Success("bundleIds", BundleId())),
            new FakeBuildLookup(AppStoreConnectBuildLookupResult.Success("apps", "builds", App(), Array.Empty<AppStoreConnectBuild>())),
            new FakeProfileLookup(AppStoreConnectProfileLookupResult.Success("bundleIds", "profiles", BundleId(), Array.Empty<AppStoreConnectProfile>())),
            new FakeCertificateLookup(AppStoreConnectCertificateLookupResult.Success("certificates", Array.Empty<AppStoreConnectCertificate>())),
            new FakeDeviceLookup(AppStoreConnectDeviceLookupResult.Failure("devices", malformed)));

        var result = await service.CheckAsync(new AppStoreConnectRemotePreflightRequest(
            Credential(),
            "com.example.demo"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == AppleDeveloperAuthErrorCodes.AppleUnauthorized);
        Assert.Contains(result.Issues, issue => issue.Code == AppStoreConnectDeviceLookupErrorCodes.ResponseMalformed);
    }

    [Fact]
    public async Task CheckResultTextDoesNotExposePrivateKey()
    {
        var credential = Credential();
        var service = new AppStoreConnectRemotePreflightService(
            new FakeAppLookup(AppStoreConnectAppLookupResult.Success("apps", App())),
            new FakeBundleIdLookup(AppStoreConnectBundleIdLookupResult.Success("bundleIds", BundleId())),
            new FakeBuildLookup(AppStoreConnectBuildLookupResult.Success("apps", "builds", App(), Array.Empty<AppStoreConnectBuild>())),
            new FakeProfileLookup(AppStoreConnectProfileLookupResult.Success("bundleIds", "profiles", BundleId(), Array.Empty<AppStoreConnectProfile>())),
            new FakeCertificateLookup(AppStoreConnectCertificateLookupResult.Success("certificates", Array.Empty<AppStoreConnectCertificate>())),
            new FakeDeviceLookup(AppStoreConnectDeviceLookupResult.Success("devices", Array.Empty<AppStoreConnectDevice>())));

        var result = await service.CheckAsync(new AppStoreConnectRemotePreflightRequest(
            credential,
            "com.example.demo"));

        Assert.DoesNotContain(credential.PrivateKeyPem, result.ToString(), StringComparison.Ordinal);
    }

    private static AppleApiKeyCredential Credential() =>
        new("ABC123DEFG", "issuer-id", "-----BEGIN PRIVATE KEY-----\ntest\n-----END PRIVATE KEY-----");

    private static AppStoreConnectApp App() =>
        new("app-1", "Demo", "com.example.demo", "DEMO");

    private static AppStoreConnectBundleId BundleId() =>
        new("bundle-1", "Demo", "com.example.demo", "IOS", "TEAMID");

    private static AppStoreConnectBuild Build(string id) =>
        new(id, "1.0.0", "VALID", DateTimeOffset.Parse("2026-06-20T10:00:00Z"), false);

    private static AppStoreConnectProfile Profile(string id) =>
        new(id, "Demo Profile", "IOS", "profile-uuid", "ACTIVE", "IOS_APP_STORE", null, null);

    private static AppStoreConnectCertificate Certificate(string id) =>
        new(id, "Certificate", "Distribution", "IOS_DISTRIBUTION", "serial", "IOS", null, true);

    private static AppStoreConnectDevice Device(string id) =>
        new(id, "Device", "IOS", "udid", "IPHONE", "ENABLED", "iPhone", null);

    private sealed class FakeAppLookup : IAppStoreConnectAppLookupService
    {
        private readonly AppStoreConnectAppLookupResult result;

        public FakeAppLookup(AppStoreConnectAppLookupResult result)
        {
            this.result = result;
        }

        public Task<AppStoreConnectAppLookupResult> LookupByBundleIdAsync(
            AppStoreConnectAppLookupRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FakeBundleIdLookup : IAppStoreConnectBundleIdLookupService
    {
        private readonly AppStoreConnectBundleIdLookupResult result;

        public FakeBundleIdLookup(AppStoreConnectBundleIdLookupResult result)
        {
            this.result = result;
        }

        public Task<AppStoreConnectBundleIdLookupResult> LookupByIdentifierAsync(
            AppStoreConnectBundleIdLookupRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FakeBuildLookup : IAppStoreConnectBuildLookupService
    {
        private readonly AppStoreConnectBuildLookupResult result;

        public FakeBuildLookup(AppStoreConnectBuildLookupResult result)
        {
            this.result = result;
        }

        public Task<AppStoreConnectBuildLookupResult> LookupByBundleIdAsync(
            AppStoreConnectBuildLookupRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FakeProfileLookup : IAppStoreConnectProfileLookupService
    {
        private readonly AppStoreConnectProfileLookupResult result;

        public FakeProfileLookup(AppStoreConnectProfileLookupResult result)
        {
            this.result = result;
        }

        public Task<AppStoreConnectProfileLookupResult> LookupByBundleIdAsync(
            AppStoreConnectProfileLookupRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FakeCertificateLookup : IAppStoreConnectCertificateLookupService
    {
        private readonly AppStoreConnectCertificateLookupResult result;

        public FakeCertificateLookup(AppStoreConnectCertificateLookupResult result)
        {
            this.result = result;
        }

        public Task<AppStoreConnectCertificateLookupResult> LookupAsync(
            AppStoreConnectCertificateLookupRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FakeDeviceLookup : IAppStoreConnectDeviceLookupService
    {
        private readonly AppStoreConnectDeviceLookupResult result;

        public FakeDeviceLookup(AppStoreConnectDeviceLookupResult result)
        {
            this.result = result;
        }

        public Task<AppStoreConnectDeviceLookupResult> LookupAsync(
            AppStoreConnectDeviceLookupRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
