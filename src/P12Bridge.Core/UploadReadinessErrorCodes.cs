namespace P12Bridge.Core;

public static class UploadReadinessErrorCodes
{
    public const string IpaMetadataMissing = "UPLOAD_IPA_METADATA_MISSING";
    public const string IpaBundleIdMissing = "UPLOAD_IPA_BUNDLE_ID_MISSING";
    public const string IpaVersionMissing = "UPLOAD_IPA_VERSION_MISSING";
    public const string IpaBuildMissing = "UPLOAD_IPA_BUILD_MISSING";
    public const string IpaSignatureMarkerMissing = "UPLOAD_IPA_SIGNATURE_MARKER_MISSING";
    public const string EmbeddedProfileMissing = "UPLOAD_EMBEDDED_PROFILE_MISSING";
    public const string EmbeddedProfileExpired = "UPLOAD_EMBEDDED_PROFILE_EXPIRED";
    public const string EmbeddedProfileTypeInvalid = "UPLOAD_EMBEDDED_PROFILE_TYPE_INVALID";
    public const string EmbeddedProfileBundleIdMismatch = "UPLOAD_EMBEDDED_PROFILE_BUNDLE_ID_MISMATCH";
    public const string ImportedProfileMissing = "UPLOAD_IMPORTED_PROFILE_MISSING";
    public const string ImportedProfileExpired = "UPLOAD_IMPORTED_PROFILE_EXPIRED";
    public const string ImportedProfileTypeInvalid = "UPLOAD_IMPORTED_PROFILE_TYPE_INVALID";
    public const string ImportedProfileBundleIdMismatch = "UPLOAD_IMPORTED_PROFILE_BUNDLE_ID_MISMATCH";
    public const string ImportedProfileTeamIdMismatch = "UPLOAD_IMPORTED_PROFILE_TEAM_ID_MISMATCH";
    public const string ImportedProfileUuidMismatch = "UPLOAD_IMPORTED_PROFILE_UUID_MISMATCH";
    public const string AppStoreTargetSupported = "UPLOAD_TARGET_APP_STORE_SUPPORTED";
}
