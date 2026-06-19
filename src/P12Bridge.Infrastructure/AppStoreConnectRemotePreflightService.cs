using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class AppStoreConnectRemotePreflightService : IAppStoreConnectRemotePreflightService
{
    private readonly IAppStoreConnectAppLookupService appLookupService;
    private readonly IAppStoreConnectBundleIdLookupService bundleIdLookupService;
    private readonly IAppStoreConnectBuildLookupService buildLookupService;
    private readonly IAppStoreConnectProfileLookupService profileLookupService;
    private readonly IAppStoreConnectCertificateLookupService certificateLookupService;
    private readonly IAppStoreConnectDeviceLookupService deviceLookupService;

    public AppStoreConnectRemotePreflightService()
        : this(
            new AppStoreConnectAppLookupService(),
            new AppStoreConnectBundleIdLookupService(),
            new AppStoreConnectBuildLookupService(),
            new AppStoreConnectProfileLookupService(),
            new AppStoreConnectCertificateLookupService(),
            new AppStoreConnectDeviceLookupService())
    {
    }

    public AppStoreConnectRemotePreflightService(
        IAppStoreConnectAppLookupService appLookupService,
        IAppStoreConnectBundleIdLookupService bundleIdLookupService,
        IAppStoreConnectBuildLookupService buildLookupService,
        IAppStoreConnectProfileLookupService profileLookupService,
        IAppStoreConnectCertificateLookupService certificateLookupService,
        IAppStoreConnectDeviceLookupService deviceLookupService)
    {
        this.appLookupService = appLookupService;
        this.bundleIdLookupService = bundleIdLookupService;
        this.buildLookupService = buildLookupService;
        this.profileLookupService = profileLookupService;
        this.certificateLookupService = certificateLookupService;
        this.deviceLookupService = deviceLookupService;
    }

    public async Task<AppStoreConnectRemotePreflightResult> CheckAsync(
        AppStoreConnectRemotePreflightRequest request,
        CancellationToken cancellationToken = default)
    {
        var bundleIdentifier = request.BundleIdentifier.Trim();
        if (string.IsNullOrWhiteSpace(bundleIdentifier))
        {
            return AppStoreConnectRemotePreflightResult.Failure(
                bundleIdentifier,
                new ValidationIssue(
                    AppStoreConnectRemotePreflightErrorCodes.BundleIdMissing,
                    ValidationSeverity.Error,
                    "Bundle ID is required for remote preflight.",
                    "Inspect an IPA before running remote preflight."));
        }

        var appResult = await appLookupService.LookupByBundleIdAsync(
            new AppStoreConnectAppLookupRequest(request.Credential, bundleIdentifier),
            cancellationToken);
        var bundleResult = await bundleIdLookupService.LookupByIdentifierAsync(
            new AppStoreConnectBundleIdLookupRequest(request.Credential, bundleIdentifier),
            cancellationToken);
        var buildResult = await buildLookupService.LookupByBundleIdAsync(
            new AppStoreConnectBuildLookupRequest(request.Credential, bundleIdentifier),
            cancellationToken);
        var profileResult = await profileLookupService.LookupByBundleIdAsync(
            new AppStoreConnectProfileLookupRequest(request.Credential, bundleIdentifier),
            cancellationToken);
        var certificateResult = await certificateLookupService.LookupAsync(
            new AppStoreConnectCertificateLookupRequest(request.Credential),
            cancellationToken);
        var deviceResult = await deviceLookupService.LookupAsync(
            new AppStoreConnectDeviceLookupRequest(request.Credential),
            cancellationToken);

        var issues = new List<ValidationIssue>();
        AddIssues(issues, appResult.Issues);
        AddIssues(issues, bundleResult.Issues);
        AddIssues(issues, buildResult.Issues);
        AddIssues(issues, profileResult.Issues);
        AddIssues(issues, certificateResult.Issues);
        AddIssues(issues, deviceResult.Issues);

        if (appResult.IsSuccess && !appResult.IsFound)
        {
            issues.Add(new ValidationIssue(
                AppStoreConnectRemotePreflightErrorCodes.AppMissing,
                ValidationSeverity.Warning,
                "App was not found in App Store Connect.",
                "Create the App record before upload."));
        }

        if (bundleResult.IsSuccess && !bundleResult.IsFound)
        {
            issues.Add(new ValidationIssue(
                AppStoreConnectRemotePreflightErrorCodes.BundleIdNotRegistered,
                ValidationSeverity.Warning,
                "Bundle ID was not found in App Store Connect.",
                "Register the Bundle ID before upload."));
        }

        issues = DeduplicateIssues(issues);
        var summary = new AppStoreConnectRemotePreflightSummary(
            AppFound: appResult.IsSuccess && appResult.IsFound,
            BundleIdFound: bundleResult.IsSuccess && bundleResult.IsFound,
            BuildCount: buildResult.IsSuccess ? buildResult.Builds.Count : 0,
            ProfileCount: profileResult.IsSuccess ? profileResult.Profiles.Count : 0,
            CertificateCount: certificateResult.IsSuccess ? certificateResult.Certificates.Count : 0,
            DeviceCount: deviceResult.IsSuccess ? deviceResult.Devices.Count : 0);

        return AppStoreConnectRemotePreflightResult.Success(bundleIdentifier, summary, issues);
    }

    private static void AddIssues(List<ValidationIssue> target, IReadOnlyList<ValidationIssue> issues)
    {
        foreach (var issue in issues)
        {
            target.Add(issue);
        }
    }

    private static List<ValidationIssue> DeduplicateIssues(IEnumerable<ValidationIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ValidationIssue>();
        foreach (var issue in issues)
        {
            var key = $"{issue.Code}|{issue.Severity}|{issue.Message}|{issue.SuggestedAction}";
            if (seen.Add(key))
            {
                result.Add(issue);
            }
        }

        return result;
    }
}
