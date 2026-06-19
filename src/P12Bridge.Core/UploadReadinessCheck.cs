namespace P12Bridge.Core;

public sealed record UploadReadinessCheck(
    string Code,
    UploadReadinessCheckStatus Status,
    string Message,
    string? SuggestedAction = null);
