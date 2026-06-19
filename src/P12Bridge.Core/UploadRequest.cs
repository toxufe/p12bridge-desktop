namespace P12Bridge.Core;

public sealed record UploadRequest(
    string TransporterExecutablePath,
    string PackagePath,
    UploadCredentialMode CredentialMode,
    UploadExecutionMode ExecutionMode = UploadExecutionMode.Verify,
    string? AssetDescriptionPath = null,
    string? ApiKeyId = null,
    string? IssuerId = null,
    string? Jwt = null,
    string? AppleAccount = null,
    string? AppSpecificPassword = null,
    TimeSpan? Timeout = null);
