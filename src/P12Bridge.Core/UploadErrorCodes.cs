namespace P12Bridge.Core;

public static class UploadErrorCodes
{
    public const string TransporterPathMissing = "UPLOAD_TRANSPORTER_PATH_MISSING";
    public const string TransporterNotFound = "UPLOAD_TRANSPORTER_NOT_FOUND";
    public const string PackagePathMissing = "UPLOAD_PACKAGE_PATH_MISSING";
    public const string PackageNotFound = "UPLOAD_PACKAGE_NOT_FOUND";
    public const string AssetDescriptionPathMissing = "UPLOAD_ASSET_DESCRIPTION_PATH_MISSING";
    public const string AssetDescriptionNotFound = "UPLOAD_ASSET_DESCRIPTION_NOT_FOUND";
    public const string ApiKeyCredentialMissing = "UPLOAD_API_KEY_CREDENTIAL_MISSING";
    public const string JwtMissing = "UPLOAD_JWT_MISSING";
    public const string AppleAccountMissing = "UPLOAD_APPLE_ACCOUNT_MISSING";
    public const string AppSpecificPasswordMissing = "UPLOAD_APP_SPECIFIC_PASSWORD_MISSING";
    public const string ProcessStartFailed = "UPLOAD_PROCESS_START_FAILED";
    public const string ProcessTimedOut = "UPLOAD_PROCESS_TIMED_OUT";
    public const string ProcessCancelled = "UPLOAD_PROCESS_CANCELLED";
    public const string ProcessExitFailed = "UPLOAD_PROCESS_EXIT_FAILED";
    public const string UnexpectedProcessResult = "UPLOAD_PROCESS_RESULT_UNEXPECTED";
}
