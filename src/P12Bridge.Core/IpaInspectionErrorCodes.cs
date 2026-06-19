namespace P12Bridge.Core;

public static class IpaInspectionErrorCodes
{
    public const string EmptyPayload = "IPA_PAYLOAD_EMPTY";
    public const string InvalidArchive = "IPA_ARCHIVE_INVALID";
    public const string AppBundleMissing = "IPA_APP_BUNDLE_MISSING";
    public const string MultipleAppBundles = "IPA_APP_BUNDLE_MULTIPLE";
    public const string InfoPlistMissing = "IPA_INFO_PLIST_MISSING";
    public const string InfoPlistUnsupported = "IPA_INFO_PLIST_UNSUPPORTED";
    public const string InfoPlistMalformed = "IPA_INFO_PLIST_MALFORMED";
    public const string MissingRequiredKey = "IPA_INFO_PLIST_REQUIRED_KEY_MISSING";
    public const string EmbeddedProfileInvalid = "IPA_EMBEDDED_PROFILE_INVALID";
}
