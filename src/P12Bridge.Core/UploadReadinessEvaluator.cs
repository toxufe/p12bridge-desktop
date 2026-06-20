namespace P12Bridge.Core;

public sealed class UploadReadinessEvaluator : IUploadReadinessEvaluator
{
    public UploadReadinessResult Evaluate(UploadReadinessRequest request, DateTimeOffset? now = null)
    {
        var checks = new List<UploadReadinessCheck>
        {
            Passed(
                UploadReadinessErrorCodes.AppStoreTargetSupported,
                "App Store / TestFlight upload target is supported by this readiness proof.")
        };

        AddPackagePathCheck(checks, request.PackagePath);
        AddAssetDescriptionPathCheck(checks, request.AssetDescriptionPath);

        if (request.IpaMetadata is null)
        {
            checks.Add(Blocked(
                UploadReadinessErrorCodes.IpaMetadataMissing,
                "IPA metadata is required before checking upload readiness.",
                "Inspect the IPA before running upload readiness checks."));

            return UploadReadinessResult.FromChecks(checks);
        }

        var ipa = request.IpaMetadata;
        AddIpaMetadataChecks(checks, ipa);
        AddEmbeddedProfileChecks(checks, ipa);
        AddImportedProfileChecks(checks, ipa, request.ImportedProvisioningProfile);

        return UploadReadinessResult.FromChecks(checks);
    }

    private static void AddPackagePathCheck(List<UploadReadinessCheck> checks, string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            checks.Add(Blocked(
                UploadReadinessErrorCodes.PackagePathMissing,
                "IPA package path is required.",
                "Choose an IPA before running upload readiness checks."));
            return;
        }

        checks.Add(File.Exists(packagePath)
            ? Passed(UploadReadinessErrorCodes.PackageNotFound, "IPA package exists.")
            : Blocked(
                UploadReadinessErrorCodes.PackageNotFound,
                "IPA package was not found.",
                "Choose an existing IPA file."));
    }

    private static void AddAssetDescriptionPathCheck(List<UploadReadinessCheck> checks, string assetDescriptionPath)
    {
        if (string.IsNullOrWhiteSpace(assetDescriptionPath))
        {
            checks.Add(Blocked(
                UploadReadinessErrorCodes.AssetDescriptionPathMissing,
                "AppStoreInfo.plist path is required.",
                "Choose the AppStoreInfo.plist exported with the signed IPA."));
            return;
        }

        checks.Add(File.Exists(assetDescriptionPath)
            ? Passed(UploadReadinessErrorCodes.AssetDescriptionNotFound, "AppStoreInfo.plist exists.")
            : Blocked(
                UploadReadinessErrorCodes.AssetDescriptionNotFound,
                "AppStoreInfo.plist was not found.",
                "Choose an existing AppStoreInfo.plist file."));
    }

    private static void AddIpaMetadataChecks(List<UploadReadinessCheck> checks, IpaMetadata ipa)
    {
        AddRequiredStringCheck(
            checks,
            ipa.BundleIdentifier,
            UploadReadinessErrorCodes.IpaBundleIdMissing,
            "IPA Bundle ID is readable.",
            "IPA Bundle ID is missing.",
            "Rebuild the IPA with a valid CFBundleIdentifier.");

        AddRequiredStringCheck(
            checks,
            ipa.ShortVersion,
            UploadReadinessErrorCodes.IpaVersionMissing,
            "IPA version is readable.",
            "IPA version is missing.",
            "Rebuild the IPA with CFBundleShortVersionString in Info.plist.");

        AddRequiredStringCheck(
            checks,
            ipa.BuildVersion,
            UploadReadinessErrorCodes.IpaBuildMissing,
            "IPA build number is readable.",
            "IPA build number is missing.",
            "Rebuild the IPA with CFBundleVersion in Info.plist.");

        checks.Add(ipa.SignaturePresence.HasCodeResources
            ? Passed(UploadReadinessErrorCodes.IpaSignatureMarkerMissing, "IPA contains a basic signature marker.")
            : Blocked(
                UploadReadinessErrorCodes.IpaSignatureMarkerMissing,
                "IPA is missing _CodeSignature/CodeResources.",
                "Upload an already signed distribution IPA."));
    }

    private static void AddEmbeddedProfileChecks(List<UploadReadinessCheck> checks, IpaMetadata ipa)
    {
        if (!ipa.HasEmbeddedProvisioningProfile || ipa.EmbeddedProvisioningProfile is null)
        {
            checks.Add(Blocked(
                UploadReadinessErrorCodes.EmbeddedProfileMissing,
                "IPA is missing an embedded provisioning profile.",
                "Upload an already signed IPA that includes embedded.mobileprovision."));
            return;
        }

        var profile = ipa.EmbeddedProvisioningProfile;
        AddProfileCompatibilityChecks(checks, profile, "embedded", isImportedProfile: false);

        checks.Add(string.Equals(ipa.BundleIdentifier, profile.BundleIdentifier, StringComparison.Ordinal)
            ? Passed(UploadReadinessErrorCodes.EmbeddedProfileBundleIdMismatch, "IPA Bundle ID matches the embedded profile.")
            : Blocked(
                UploadReadinessErrorCodes.EmbeddedProfileBundleIdMismatch,
                "IPA Bundle ID does not match the embedded provisioning profile.",
                "Rebuild the IPA with a profile for the same Bundle ID."));
    }

    private static void AddImportedProfileChecks(
        List<UploadReadinessCheck> checks,
        IpaMetadata ipa,
        ProvisioningProfile? importedProfile)
    {
        if (importedProfile is null)
        {
            checks.Add(Warning(
                UploadReadinessErrorCodes.ImportedProfileMissing,
                "No imported profile was provided for asset-library comparison.",
                "Import the matching App Store provisioning profile to enable stronger local checks."));
            return;
        }

        AddProfileCompatibilityChecks(checks, importedProfile, "imported", isImportedProfile: true);

        checks.Add(string.Equals(ipa.BundleIdentifier, importedProfile.BundleIdentifier, StringComparison.Ordinal)
            ? Passed(UploadReadinessErrorCodes.ImportedProfileBundleIdMismatch, "IPA Bundle ID matches the imported profile.")
            : Blocked(
                UploadReadinessErrorCodes.ImportedProfileBundleIdMismatch,
                "IPA Bundle ID does not match the imported provisioning profile.",
                "Import an App Store profile for the same Bundle ID as the IPA."));

        if (ipa.EmbeddedProvisioningProfile is null)
        {
            return;
        }

        checks.Add(string.Equals(ipa.EmbeddedProvisioningProfile.TeamId, importedProfile.TeamId, StringComparison.Ordinal)
            ? Passed(UploadReadinessErrorCodes.ImportedProfileTeamIdMismatch, "Imported profile Team ID matches the embedded profile.")
            : Blocked(
                UploadReadinessErrorCodes.ImportedProfileTeamIdMismatch,
                "Imported profile Team ID does not match the embedded profile Team ID.",
                "Use profiles from the same Apple Developer team."));

        if (string.Equals(ipa.EmbeddedProvisioningProfile.Uuid, importedProfile.Uuid, StringComparison.Ordinal))
        {
            checks.Add(Passed(UploadReadinessErrorCodes.ImportedProfileUuidMismatch, "Imported profile matches the embedded profile UUID."));
        }
        else
        {
            checks.Add(Warning(
                UploadReadinessErrorCodes.ImportedProfileUuidMismatch,
                "Imported profile UUID differs from the embedded profile UUID.",
                "This can still be acceptable if Bundle ID, Team ID, profile type, and expiration are compatible."));
        }
    }

    private static void AddProfileCompatibilityChecks(
        List<UploadReadinessCheck> checks,
        ProvisioningProfile profile,
        string label,
        bool isImportedProfile)
    {
        var expiredCode = isImportedProfile
            ? UploadReadinessErrorCodes.ImportedProfileExpired
            : UploadReadinessErrorCodes.EmbeddedProfileExpired;
        var typeCode = isImportedProfile
            ? UploadReadinessErrorCodes.ImportedProfileTypeInvalid
            : UploadReadinessErrorCodes.EmbeddedProfileTypeInvalid;

        checks.Add(profile.Status == ProvisioningProfileStatus.Active
            ? Passed(expiredCode, $"The {label} profile is active.")
            : Blocked(
                expiredCode,
                $"The {label} profile is expired.",
                "Create or import a current App Store provisioning profile."));

        checks.Add(profile.Type == ProvisioningProfileType.AppStore
            ? Passed(typeCode, $"The {label} profile is an App Store profile.")
            : Blocked(
                typeCode,
                $"The {label} profile is not an App Store profile.",
                "Use an App Store distribution provisioning profile for TestFlight/App Store upload."));
    }

    private static void AddRequiredStringCheck(
        List<UploadReadinessCheck> checks,
        string value,
        string code,
        string passedMessage,
        string blockedMessage,
        string suggestedAction)
    {
        checks.Add(string.IsNullOrWhiteSpace(value)
            ? Blocked(code, blockedMessage, suggestedAction)
            : Passed(code, passedMessage));
    }

    private static UploadReadinessCheck Passed(string code, string message) =>
        new(code, UploadReadinessCheckStatus.Passed, message);

    private static UploadReadinessCheck Warning(string code, string message, string suggestedAction) =>
        new(code, UploadReadinessCheckStatus.Warning, message, suggestedAction);

    private static UploadReadinessCheck Blocked(string code, string message, string suggestedAction) =>
        new(code, UploadReadinessCheckStatus.Blocked, message, suggestedAction);
}
