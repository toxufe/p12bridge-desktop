namespace P12Bridge.Core;

public static class ProvisioningProfileErrorCodes
{
    public const string EmptyPayload = "PROFILE_PAYLOAD_EMPTY";
    public const string PlistNotFound = "PROFILE_PLIST_NOT_FOUND";
    public const string MalformedPlist = "PROFILE_PLIST_MALFORMED";
    public const string MissingRequiredKey = "PROFILE_REQUIRED_KEY_MISSING";
    public const string ExpiredProfile = "PROFILE_EXPIRED";
    public const string UnknownProfileType = "PROFILE_TYPE_UNKNOWN";
    public const string ImportFileMissing = "PROFILE_IMPORT_FILE_MISSING";
    public const string ImportFileNotFound = "PROFILE_IMPORT_FILE_NOT_FOUND";
    public const string ImportDirectoryMissing = "PROFILE_IMPORT_DIRECTORY_MISSING";
    public const string ImportFailed = "PROFILE_IMPORT_FAILED";
}
