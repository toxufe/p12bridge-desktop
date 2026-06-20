namespace P12Bridge.Core;

public sealed record SigningAssetProject(
    string Name,
    SigningPurpose Purpose,
    string ProjectDirectory,
    DateTimeOffset CreatedAt,
    string Note = "");
