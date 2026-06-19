namespace P12Bridge.Core;

public sealed record AssetExpirationReminderRequest(
    string CertificateDirectory,
    string ProfileDirectory,
    TimeSpan? WarningWindow = null);
