using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class AssetExpirationReminderServiceTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"p12bridge-expiration-{Guid.NewGuid():N}");
    private readonly string certificateDirectory;
    private readonly string profileDirectory;
    private readonly DateTimeOffset now = DateTimeOffset.Parse("2026-06-20T00:00:00Z");

    public AssetExpirationReminderServiceTests()
    {
        certificateDirectory = Path.Combine(tempDirectory, "Certificates");
        profileDirectory = Path.Combine(tempDirectory, "Profiles");
        Directory.CreateDirectory(certificateDirectory);
        Directory.CreateDirectory(profileDirectory);
    }

    [Fact]
    public void ScanReturnsExpiredCertificateReminder()
    {
        var projectDirectory = CreateCertificateProject("Expired Cert", now.AddDays(-5));
        var service = new AssetExpirationReminderService();

        var result = service.Scan(ValidRequest(), now);

        var reminder = Assert.Single(result.Reminders);
        Assert.True(result.IsSuccess);
        Assert.Equal(AssetExpirationReminderType.Certificate, reminder.Type);
        Assert.Equal(AssetExpirationReminderStatus.Expired, reminder.Status);
        Assert.Equal("Expired Cert", reminder.Name);
        Assert.Equal(projectDirectory, reminder.Path);
        Assert.True(reminder.DaysRemaining < 0);
    }

    [Fact]
    public void ScanReturnsSoonExpiringCertificateReminder()
    {
        CreateCertificateProject("Soon Cert", now.AddDays(12));
        var service = new AssetExpirationReminderService();

        var result = service.Scan(ValidRequest(), now);

        var reminder = Assert.Single(result.Reminders);
        Assert.Equal(AssetExpirationReminderStatus.ExpiringSoon, reminder.Status);
        Assert.Equal(12, reminder.DaysRemaining);
    }

    [Fact]
    public void ScanIgnoresCertificateOutsideWarningWindow()
    {
        CreateCertificateProject("Valid Cert", now.AddDays(90));
        var service = new AssetExpirationReminderService();

        var result = service.Scan(ValidRequest(), now);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Reminders);
    }

    [Fact]
    public void ScanIgnoresCertificateProjectWithoutCertificateFile()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "NoCert");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "private.key"), "PRIVATE KEY CONTENT");
        var service = new AssetExpirationReminderService();

        var result = service.Scan(ValidRequest(), now);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Reminders);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ScanReturnsExpiredProfileReminder()
    {
        WriteProfile("expired.mobileprovision", "Expired Profile", now.AddDays(-1));
        var service = new AssetExpirationReminderService();

        var result = service.Scan(ValidRequest(), now);

        var reminder = Assert.Single(result.Reminders);
        Assert.True(result.IsSuccess);
        Assert.Equal(AssetExpirationReminderType.ProvisioningProfile, reminder.Type);
        Assert.Equal(AssetExpirationReminderStatus.Expired, reminder.Status);
        Assert.Equal("Expired Profile", reminder.Name);
    }

    [Fact]
    public void ScanReturnsSoonExpiringProfileReminder()
    {
        WriteProfile("soon.mobileprovision", "Soon Profile", now.AddDays(8));
        var service = new AssetExpirationReminderService();

        var result = service.Scan(ValidRequest(), now);

        var reminder = Assert.Single(result.Reminders);
        Assert.Equal(AssetExpirationReminderStatus.ExpiringSoon, reminder.Status);
        Assert.Equal(8, reminder.DaysRemaining);
    }

    [Fact]
    public void ScanContinuesAfterInvalidAssets()
    {
        File.WriteAllText(Path.Combine(profileDirectory, "bad.mobileprovision"), "not a profile");
        CreateCertificateProject("Good Cert", now.AddDays(4));
        var badProject = Path.Combine(certificateDirectory, "BadCert");
        Directory.CreateDirectory(badProject);
        File.WriteAllText(Path.Combine(badProject, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(badProject, "certificate.cer"), "not a certificate");
        var service = new AssetExpirationReminderService();

        var result = service.Scan(ValidRequest(), now);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Reminders);
        Assert.Contains(result.Issues, issue => issue.Code == AssetExpirationReminderErrorCodes.ProfileInvalid);
        Assert.Contains(result.Issues, issue => issue.Code == AssetExpirationReminderErrorCodes.CertificateInvalid);
    }

    [Fact]
    public void ScanTreatsMissingDirectoriesAsEmpty()
    {
        var service = new AssetExpirationReminderService();

        var result = service.Scan(new AssetExpirationReminderRequest(
            Path.Combine(tempDirectory, "MissingCerts"),
            Path.Combine(tempDirectory, "MissingProfiles")), now);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Reminders);
    }

    [Fact]
    public void ScanDoesNotUsePrivateKeyContents()
    {
        CreateCertificateProject("Secret Cert", now.AddDays(3), privateKeyContent: "PRIVATE SECRET VALUE");
        var service = new AssetExpirationReminderService();

        var result = service.Scan(ValidRequest(), now);

        Assert.DoesNotContain(result.Reminders, reminder => reminder.Name.Contains("PRIVATE", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Reminders, reminder => reminder.Path.Contains("PRIVATE", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains("PRIVATE", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private AssetExpirationReminderRequest ValidRequest() =>
        new(certificateDirectory, profileDirectory);

    private string CreateCertificateProject(
        string name,
        DateTimeOffset expiresAt,
        string privateKeyContent = "sensitive")
    {
        var projectDirectory = Path.Combine(certificateDirectory, name);
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "private.key"), privateKeyContent);
        File.WriteAllBytes(Path.Combine(projectDirectory, "certificate.cer"), CreateCertificate(expiresAt));
        return projectDirectory;
    }

    private static byte[] CreateCertificate(DateTimeOffset expiresAt)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Demo",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var notBefore = expiresAt.AddDays(-365);
        using var certificate = request.CreateSelfSigned(notBefore, expiresAt);
        return certificate.Export(X509ContentType.Cert);
    }

    private void WriteProfile(string fileName, string name, DateTimeOffset expiresAt)
    {
        var path = Path.Combine(profileDirectory, fileName);
        File.WriteAllBytes(path, WrapMobileProvision(ProfilePlist(name, expiresAt)));
    }

    private static byte[] WrapMobileProvision(string plist) =>
        Encoding.UTF8.GetBytes($"cms-header\0{plist}\0cms-footer");

    private static string ProfilePlist(string name, DateTimeOffset expiresAt) =>
        $$"""
          <?xml version="1.0" encoding="UTF-8"?>
          <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
          <plist version="1.0">
          <dict>
              <key>UUID</key><string>PROFILE-UUID-{{name}}</string>
              <key>Name</key><string>{{name}}</string>
              <key>TeamIdentifier</key>
              <array><string>TEAM123456</string></array>
              <key>CreationDate</key><date>2026-01-01T00:00:00Z</date>
              <key>ExpirationDate</key><date>{{expiresAt:yyyy-MM-ddTHH:mm:ssZ}}</date>
              <key>Entitlements</key>
              <dict>
                  <key>application-identifier</key><string>TEAM123456.com.example.app</string>
                  <key>get-task-allow</key><false/>
              </dict>
          </dict>
          </plist>
          """;
}
