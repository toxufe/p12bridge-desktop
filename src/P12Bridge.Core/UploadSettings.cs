namespace P12Bridge.Core;

public sealed record UploadSettings(
    string TransporterExecutablePath = "",
    string PackagePath = "",
    string AssetDescriptionPath = "",
    UploadCredentialMode CredentialMode = UploadCredentialMode.ApiKey,
    string ApiKeyId = "",
    string IssuerId = "",
    string PrivateKeyPath = "",
    string AppleAccount = "",
    bool SaveSensitiveValues = false,
    string Jwt = "",
    string AppSpecificPassword = "",
    string CertificateDirectory = "",
    string ProfileDirectory = "",
    string IpaDirectory = "")
{
    public override string ToString() =>
        "UploadSettings { Jwt = [REDACTED], AppSpecificPassword = [REDACTED] }";
}
