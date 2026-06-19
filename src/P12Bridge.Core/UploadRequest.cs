namespace P12Bridge.Core;

public sealed record UploadRequest(
    string TransporterExecutablePath,
    string PackagePath,
    UploadCredentialMode CredentialMode,
    string? ApiKeyId = null,
    string? IssuerId = null,
    string? Jwt = null,
    TimeSpan? Timeout = null);
