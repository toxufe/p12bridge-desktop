namespace P12Bridge.Core;

public sealed record UploadProgress(
    UploadPhase Phase,
    string Message,
    int? Percent = null);
