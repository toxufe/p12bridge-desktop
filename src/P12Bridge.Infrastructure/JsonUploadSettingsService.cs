using System.Text.Json;
using System.Security.Cryptography;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class JsonUploadSettingsService : IUploadSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string settingsPath;
    private readonly ISecretProtector secretProtector;

    public JsonUploadSettingsService(string? settingsPath = null, ISecretProtector? secretProtector = null)
    {
        this.settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? GetDefaultSettingsPath()
            : settingsPath;
        this.secretProtector = secretProtector ?? CreateDefaultSecretProtector();
    }

    public UploadSettingsResult Load()
    {
        if (!File.Exists(settingsPath))
        {
            return UploadSettingsResult.Success(new UploadSettings());
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            var stored = JsonSerializer.Deserialize<StoredUploadSettings>(json, SerializerOptions);

            if (stored is null)
            {
                return LoadWarning();
            }

            return ToSettings(stored);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return LoadWarning();
        }
    }

    public UploadSettingsResult Save(UploadSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath) ?? ".");
            var stored = FromSettings(settings);
            var json = JsonSerializer.Serialize(stored, SerializerOptions);
            File.WriteAllText(settingsPath, json);
            return UploadSettingsResult.Success(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException or CryptographicException)
        {
            return UploadSettingsResult.Failure(new ValidationIssue(
                UploadSettingsErrorCodes.SaveFailed,
                ValidationSeverity.Error,
                "Upload settings could not be saved.",
                "Check the settings storage location and try again."));
        }
    }

    public UploadSettingsResult Clear()
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }

            return UploadSettingsResult.Success(new UploadSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return UploadSettingsResult.Failure(new ValidationIssue(
                UploadSettingsErrorCodes.ClearFailed,
                ValidationSeverity.Error,
                "Upload settings could not be cleared.",
                "Check the settings storage location and try again."));
        }
    }

    private UploadSettingsResult ToSettings(StoredUploadSettings stored)
    {
        var issues = new List<ValidationIssue>();
        var jwt = string.Empty;
        var appSpecificPassword = string.Empty;

        if (stored.SaveSensitiveValues)
        {
            jwt = TryUnprotect(stored.ProtectedJwt, issues);
            appSpecificPassword = TryUnprotect(stored.ProtectedAppSpecificPassword, issues);
        }

        var settings = new UploadSettings(
            stored.TransporterExecutablePath ?? string.Empty,
            stored.PackagePath ?? string.Empty,
            stored.AssetDescriptionPath ?? string.Empty,
            stored.CredentialMode,
            stored.ApiKeyId ?? string.Empty,
            stored.IssuerId ?? string.Empty,
            stored.PrivateKeyPath ?? string.Empty,
            stored.AppleAccount ?? string.Empty,
            stored.SaveSensitiveValues,
            jwt,
            appSpecificPassword,
            stored.CertificateDirectory ?? string.Empty,
            stored.ProfileDirectory ?? string.Empty,
            stored.IpaDirectory ?? string.Empty);

        return issues.Count == 0
            ? UploadSettingsResult.Success(settings)
            : UploadSettingsResult.Warning(settings, issues.ToArray());
    }

    private StoredUploadSettings FromSettings(UploadSettings settings) =>
        new()
        {
            TransporterExecutablePath = settings.TransporterExecutablePath,
            PackagePath = settings.PackagePath,
            AssetDescriptionPath = settings.AssetDescriptionPath,
            CredentialMode = settings.CredentialMode,
            ApiKeyId = settings.ApiKeyId,
            IssuerId = settings.IssuerId,
            PrivateKeyPath = settings.PrivateKeyPath,
            AppleAccount = settings.AppleAccount,
            SaveSensitiveValues = settings.SaveSensitiveValues,
            ProtectedJwt = settings.SaveSensitiveValues ? secretProtector.Protect(settings.Jwt) : string.Empty,
            ProtectedAppSpecificPassword = settings.SaveSensitiveValues ? secretProtector.Protect(settings.AppSpecificPassword) : string.Empty,
            CertificateDirectory = settings.CertificateDirectory,
            ProfileDirectory = settings.ProfileDirectory,
            IpaDirectory = settings.IpaDirectory
        };

    private string TryUnprotect(string? protectedValue, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return string.Empty;
        }

        try
        {
            return secretProtector.Unprotect(protectedValue);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            issues.Add(new ValidationIssue(
                UploadSettingsErrorCodes.SecretUnprotectFailed,
                ValidationSeverity.Warning,
                "Saved secret could not be restored.",
                "Re-enter and save the secret again."));
            return string.Empty;
        }
    }

    private static UploadSettingsResult LoadWarning() =>
        UploadSettingsResult.Warning(
            new UploadSettings(),
            new ValidationIssue(
                UploadSettingsErrorCodes.LoadFailed,
                ValidationSeverity.Warning,
                "Upload settings could not be loaded.",
                "Save the settings again."));

    private static string GetDefaultSettingsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "P12Bridge",
            "upload-settings.json");

    private static ISecretProtector CreateDefaultSecretProtector()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Upload secret storage requires Windows.");
        }

        return new DpapiSecretProtector();
    }

    private sealed class StoredUploadSettings
    {
        public string? TransporterExecutablePath { get; set; }

        public string? PackagePath { get; set; }

        public string? AssetDescriptionPath { get; set; }

        public UploadCredentialMode CredentialMode { get; set; } = UploadCredentialMode.ApiKey;

        public string? ApiKeyId { get; set; }

        public string? IssuerId { get; set; }

        public string? PrivateKeyPath { get; set; }

        public string? AppleAccount { get; set; }

        public bool SaveSensitiveValues { get; set; }

        public string? ProtectedJwt { get; set; }

        public string? ProtectedAppSpecificPassword { get; set; }

        public string? CertificateDirectory { get; set; }

        public string? ProfileDirectory { get; set; }

        public string? IpaDirectory { get; set; }
    }
}
