using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class AssetExpirationReminderService : IAssetExpirationReminderService
{
    private const string CertificateMetadataFileName = "p12bridge.project.json";
    private const string CertificateFileName = "certificate.cer";
    private static readonly TimeSpan DefaultWarningWindow = TimeSpan.FromDays(30);

    private readonly IProvisioningProfileParser profileParser;

    public AssetExpirationReminderService(IProvisioningProfileParser? profileParser = null)
    {
        this.profileParser = profileParser ?? new ProvisioningProfileParser();
    }

    public AssetExpirationReminderResult Scan(AssetExpirationReminderRequest request, DateTimeOffset? now = null)
    {
        var reminders = new List<AssetExpirationReminder>();
        var issues = new List<ValidationIssue>();
        var referenceTime = now ?? DateTimeOffset.UtcNow;
        var warningWindow = request.WarningWindow ?? DefaultWarningWindow;

        AddCertificateReminders(request.CertificateDirectory, referenceTime, warningWindow, reminders, issues);
        AddProfileReminders(request.ProfileDirectory, referenceTime, warningWindow, reminders, issues);

        var sorted = reminders
            .OrderBy(reminder => reminder.ExpiresAt)
            .ThenBy(reminder => reminder.Type)
            .ThenBy(reminder => reminder.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return issues.Count == 0
            ? AssetExpirationReminderResult.Success(sorted)
            : AssetExpirationReminderResult.Partial(sorted, issues.ToArray());
    }

    private static void AddCertificateReminders(
        string rootDirectory,
        DateTimeOffset now,
        TimeSpan warningWindow,
        List<AssetExpirationReminder> reminders,
        List<ValidationIssue> issues)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return;
        }

        try
        {
            foreach (var metadataPath in Directory.EnumerateFiles(
                rootDirectory,
                CertificateMetadataFileName,
                SearchOption.AllDirectories))
            {
                var projectDirectory = Path.GetDirectoryName(metadataPath);
                if (string.IsNullOrWhiteSpace(projectDirectory))
                {
                    continue;
                }

                var certificatePath = Path.Combine(projectDirectory, CertificateFileName);
                if (!File.Exists(certificatePath))
                {
                    continue;
                }

                AddCertificateReminder(certificatePath, projectDirectory, now, warningWindow, reminders, issues);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            issues.Add(new ValidationIssue(
                AssetExpirationReminderErrorCodes.CertificateScanFailed,
                ValidationSeverity.Warning,
                "Certificate reminder scan failed.",
                exception.GetType().Name));
        }
    }

    private static void AddCertificateReminder(
        string certificatePath,
        string projectDirectory,
        DateTimeOffset now,
        TimeSpan warningWindow,
        List<AssetExpirationReminder> reminders,
        List<ValidationIssue> issues)
    {
        try
        {
            using var certificate = new X509Certificate2(certificatePath);
            var expiresAt = new DateTimeOffset(certificate.NotAfter).ToUniversalTime();
            AddReminderIfNeeded(
                AssetExpirationReminderType.Certificate,
                Path.GetFileName(projectDirectory),
                projectDirectory,
                expiresAt,
                now,
                warningWindow,
                reminders);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException or SecurityException)
        {
            issues.Add(new ValidationIssue(
                AssetExpirationReminderErrorCodes.CertificateInvalid,
                ValidationSeverity.Warning,
                "Certificate could not be read.",
                Path.GetFileName(certificatePath)));
        }
    }

    private void AddProfileReminders(
        string rootDirectory,
        DateTimeOffset now,
        TimeSpan warningWindow,
        List<AssetExpirationReminder> reminders,
        List<ValidationIssue> issues)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return;
        }

        try
        {
            foreach (var profilePath in Directory.EnumerateFiles(rootDirectory, "*.mobileprovision", SearchOption.TopDirectoryOnly))
            {
                AddProfileReminder(profilePath, now, warningWindow, reminders, issues);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            issues.Add(new ValidationIssue(
                AssetExpirationReminderErrorCodes.ProfileScanFailed,
                ValidationSeverity.Warning,
                "Profile reminder scan failed.",
                exception.GetType().Name));
        }
    }

    private void AddProfileReminder(
        string profilePath,
        DateTimeOffset now,
        TimeSpan warningWindow,
        List<AssetExpirationReminder> reminders,
        List<ValidationIssue> issues)
    {
        try
        {
            var result = profileParser.Parse(File.ReadAllBytes(profilePath), now);
            if (result.Profile is null)
            {
                issues.Add(new ValidationIssue(
                    AssetExpirationReminderErrorCodes.ProfileInvalid,
                    ValidationSeverity.Warning,
                    "Profile could not be read.",
                    Path.GetFileName(profilePath)));
                return;
            }

            AddReminderIfNeeded(
                AssetExpirationReminderType.ProvisioningProfile,
                result.Profile.Name,
                profilePath,
                result.Profile.ExpirationDate,
                now,
                warningWindow,
                reminders);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            issues.Add(new ValidationIssue(
                AssetExpirationReminderErrorCodes.ProfileInvalid,
                ValidationSeverity.Warning,
                "Profile could not be read.",
                Path.GetFileName(profilePath)));
        }
    }

    private static void AddReminderIfNeeded(
        AssetExpirationReminderType type,
        string name,
        string path,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        TimeSpan warningWindow,
        List<AssetExpirationReminder> reminders)
    {
        var timeRemaining = expiresAt - now;
        if (timeRemaining > warningWindow)
        {
            return;
        }

        var daysRemaining = (int)Math.Floor(timeRemaining.TotalDays);
        var status = expiresAt <= now
            ? AssetExpirationReminderStatus.Expired
            : AssetExpirationReminderStatus.ExpiringSoon;

        reminders.Add(new AssetExpirationReminder(
            type,
            name,
            path,
            expiresAt,
            status,
            daysRemaining));
    }
}
