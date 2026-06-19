namespace P12Bridge.Core;

public sealed record LocalAssetItem(
    LocalAssetType Type,
    string Name,
    string Path,
    DateTimeOffset ModifiedAt);
