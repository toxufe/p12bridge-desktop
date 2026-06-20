namespace P12Bridge.Core;

public sealed record UploadEvidence(
    DateTimeOffset CapturedAt,
    string BundleIdentifier = "",
    string Version = "",
    string Build = "",
    string TeamId = "",
    string IpaSummary = "",
    string IpaPath = "",
    string ProfileSummary = "",
    string ProfilePath = "",
    string AssetDescriptionSummary = "",
    string AssetDescriptionPath = "",
    string ReadinessStatus = "",
    string EnvironmentStatus = "",
    string ProofStatus = "",
    string VerifyStatus = "",
    string ReadinessDetail = "",
    string RemotePreflightDetail = "",
    string BuildLookupDetail = "",
    string TransporterDetail = "");
