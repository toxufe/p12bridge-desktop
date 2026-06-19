namespace P12Bridge.Core;

public sealed record AppStoreConnectBuild(
    string Id,
    string Version,
    string ProcessingState,
    DateTimeOffset? UploadedDate,
    bool? Expired);
