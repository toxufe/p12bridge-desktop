using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.IO.Compression;
using System.Text;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class LocalAssetLibraryServiceTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"p12bridge-assets-{Guid.NewGuid():N}");
    private readonly string certificateDirectory;
    private readonly string profileDirectory;
    private readonly string ipaDirectory;

    public LocalAssetLibraryServiceTests()
    {
        certificateDirectory = Path.Combine(tempDirectory, "Certificates");
        profileDirectory = Path.Combine(tempDirectory, "Profiles");
        ipaDirectory = Path.Combine(tempDirectory, "IPAs");

        Directory.CreateDirectory(certificateDirectory);
        Directory.CreateDirectory(profileDirectory);
        Directory.CreateDirectory(ipaDirectory);
    }

    [Fact]
    public void ScanDiscoversCertificateProjectsProfilesAndIpas()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Demo");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "private.key"), "sensitive");
        var profilePath = Path.Combine(profileDirectory, "demo.mobileprovision");
        var ipaPath = Path.Combine(ipaDirectory, "demo.ipa");
        File.WriteAllText(profilePath, "profile");
        File.WriteAllText(ipaPath, "ipa");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Items, item =>
            item.Type == LocalAssetType.CertificateProject
            && item.Name == "Demo"
            && item.Path == projectDirectory);
        Assert.Contains(result.Items, item =>
            item.Type == LocalAssetType.ProvisioningProfile
            && item.Name == "demo.mobileprovision"
            && item.Path == profilePath);
        Assert.Contains(result.Items, item =>
            item.Type == LocalAssetType.Ipa
            && item.Name == "demo.ipa"
            && item.Path == ipaPath);
    }

    [Fact]
    public void ScanTreatsMissingDirectoriesAsEmpty()
    {
        var service = new LocalAssetLibraryService();

        var result = service.Scan(new LocalAssetLibraryRequest(
            Path.Combine(tempDirectory, "MissingCerts"),
            Path.Combine(tempDirectory, "MissingProfiles"),
            Path.Combine(tempDirectory, "MissingIpas")));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void ScanReadsCertificateProjectNoteFromMetadata()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Demo");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), """
            {
              "Note": "  发布证书  "
            }
            """);
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.Equal("发布证书", item.Note);
    }

    [Fact]
    public void ScanReportsEmptyCertificateProjectArtifactStatus()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Empty");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.NotNull(item.CertificateArtifacts);
        Assert.False(item.CertificateArtifacts.HasAny);
    }

    [Fact]
    public void ScanReportsPrivateKeyAndCsrArtifactStatus()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "CsrReady");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "private.key"), "PRIVATE KEY CONTENT");
        File.WriteAllText(Path.Combine(projectDirectory, "request.csr"), "CSR CONTENT");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.NotNull(item.CertificateArtifacts);
        Assert.True(item.CertificateArtifacts.HasPrivateKey);
        Assert.True(item.CertificateArtifacts.HasCertificateSigningRequest);
        Assert.False(item.CertificateArtifacts.HasCertificate);
        Assert.False(item.CertificateArtifacts.HasP12);
    }

    [Fact]
    public void ScanReportsCertificateAndP12ArtifactStatus()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Exported");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "certificate.cer"), "CER CONTENT");
        File.WriteAllText(Path.Combine(projectDirectory, "export.p12"), "P12 CONTENT");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.NotNull(item.CertificateArtifacts);
        Assert.False(item.CertificateArtifacts.HasPrivateKey);
        Assert.False(item.CertificateArtifacts.HasCertificateSigningRequest);
        Assert.True(item.CertificateArtifacts.HasCertificate);
        Assert.True(item.CertificateArtifacts.HasP12);
    }

    [Fact]
    public void ScanReportsNewestCertificateBackupSummary()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "BackedUp");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        var backupDirectory = Path.Combine(certificateDirectory, "Backups");
        Directory.CreateDirectory(backupDirectory);
        var oldBackupPath = Path.Combine(backupDirectory, "BackedUp-20260601010203.zip");
        var newBackupPath = Path.Combine(backupDirectory, "BackedUp-20260620010203.zip");
        File.WriteAllText(oldBackupPath, "old backup");
        File.WriteAllText(newBackupPath, "new backup");
        File.SetLastWriteTimeUtc(oldBackupPath, new DateTime(2026, 6, 1, 1, 2, 3, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newBackupPath, new DateTime(2026, 6, 20, 1, 2, 3, DateTimeKind.Utc));
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.Equal("备份 2026-06-20", item.BackupSummary);
        Assert.Equal(newBackupPath, item.BackupPath);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ScanMatchesCertificateBackupWithSanitizedProjectName()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Demo Project");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        var backupDirectory = Path.Combine(certificateDirectory, "Backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, "Demo-Project-20260620010203.zip");
        File.WriteAllText(backupPath, "backup");
        File.SetLastWriteTimeUtc(backupPath, new DateTime(2026, 6, 20, 1, 2, 3, DateTimeKind.Utc));
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.Equal("备份 2026-06-20", item.BackupSummary);
        Assert.Equal(backupPath, item.BackupPath);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ScanKeepsCertificateProjectWithoutBackupSummaryWhenBackupIsMissing()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "NoBackup");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.Equal(string.Empty, item.BackupSummary);
        Assert.Equal(string.Empty, item.BackupPath);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ScanReportsCertificateExpiration()
    {
        var expiresAt = new DateTimeOffset(2027, 6, 20, 0, 0, 0, TimeSpan.Zero);
        var projectDirectory = Path.Combine(certificateDirectory, "Expiring");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllBytes(Path.Combine(projectDirectory, "certificate.cer"), CreateCertificate(expiresAt));
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.NotNull(item.ExpiresAt);
        Assert.Equal(expiresAt.UtcDateTime.Date, item.ExpiresAt.Value.UtcDateTime.Date);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ScanKeepsCertificateProjectWithoutExpirationWhenCertificateIsMissing()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "NoCertificate");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.Null(item.ExpiresAt);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ScanKeepsCertificateProjectAndWarnsWhenCertificateIsInvalid()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "InvalidCertificate");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "certificate.cer"), "not a certificate");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.Null(item.ExpiresAt);
        Assert.Contains(result.Issues, issue => issue.Code == LocalAssetLibraryErrorCodes.ScanFailed);
    }

    [Fact]
    public void ScanReportsProvisioningProfileExpiration()
    {
        var expiresAt = new DateTimeOffset(2027, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var profilePath = Path.Combine(profileDirectory, "demo.mobileprovision");
        File.WriteAllBytes(profilePath, WrapMobileProvision(ProfilePlist(expiresAt)));
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.ProvisioningProfile);
        Assert.Equal(expiresAt, item.ExpiresAt);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ScanReportsProvisioningProfileSafeSummary()
    {
        var expiresAt = new DateTimeOffset(2027, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var profilePath = Path.Combine(profileDirectory, "demo.mobileprovision");
        File.WriteAllBytes(profilePath, WrapMobileProvision(ProfilePlist(expiresAt)));
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.ProvisioningProfile);
        Assert.Equal("App Store / 有效 / com.example.app / TEAM123456 / 证书 0", item.SafeMetadataSummary);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ScanReportsProvisioningProfileCertificateCount()
    {
        var expiresAt = new DateTimeOffset(2027, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var profilePath = Path.Combine(profileDirectory, "demo.mobileprovision");
        File.WriteAllBytes(profilePath, WrapMobileProvision(ProfilePlist(expiresAt, [
            Convert.ToBase64String([1, 2, 3]),
            Convert.ToBase64String([4, 5, 6])
        ])));
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.ProvisioningProfile);
        Assert.Equal("App Store / 有效 / com.example.app / TEAM123456 / 证书 2", item.SafeMetadataSummary);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ScanKeepsProvisioningProfileAndWarnsWhenProfileIsInvalid()
    {
        var profilePath = Path.Combine(profileDirectory, "bad.mobileprovision");
        File.WriteAllText(profilePath, "raw profile payload");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.ProvisioningProfile);
        Assert.Equal("bad.mobileprovision", item.Name);
        Assert.Equal(profilePath, item.Path);
        Assert.Null(item.ExpiresAt);
        Assert.Equal(string.Empty, item.SafeMetadataSummary);
        Assert.Contains(result.Issues, issue =>
            issue.Code == LocalAssetLibraryErrorCodes.ScanFailed
            && issue.SuggestedAction == ProvisioningProfileErrorCodes.PlistNotFound);
    }

    [Fact]
    public void ScanReportsIpaSafeSummary()
    {
        var ipaPath = Path.Combine(ipaDirectory, "demo.ipa");
        File.WriteAllBytes(ipaPath, CreateIpa(InfoPlist(
            bundleIdentifier: "com.example.demo",
            shortVersion: "1.2.3",
            buildVersion: "45",
            displayName: "Demo App")));
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.Ipa);
        Assert.Equal("com.example.demo / 1.2.3 (45) / Demo App", item.SafeMetadataSummary);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ScanKeepsIpaAndWarnsWhenIpaIsInvalid()
    {
        var ipaPath = Path.Combine(ipaDirectory, "bad.ipa");
        File.WriteAllText(ipaPath, "raw ipa payload");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.Ipa);
        Assert.Equal("bad.ipa", item.Name);
        Assert.Equal(ipaPath, item.Path);
        Assert.Equal(string.Empty, item.SafeMetadataSummary);
        Assert.Contains(result.Issues, issue =>
            issue.Code == LocalAssetLibraryErrorCodes.ScanFailed
            && issue.SuggestedAction == IpaInspectionErrorCodes.InvalidArchive);
    }

    [Fact]
    public void ScanKeepsCertificateProjectWhenMetadataNoteIsMalformed()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Broken");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.Equal("Broken", item.Name);
        Assert.Equal(string.Empty, item.Note);
    }

    [Fact]
    public void ScanDoesNotReadPrivateKeyContents()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Secret");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "private.key"), "PRIVATE KEY CONTENT");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        Assert.DoesNotContain(result.Items, item => item.Name.Contains("PRIVATE", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Items, item => item.Note.Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Fact]
    public void ScanDoesNotExposeBackupArchiveOrProjectSigningContents()
    {
        var privateKeyContent = "PRIVATE KEY CONTENT";
        var backupContent = "SECRET BACKUP ARCHIVE CONTENT";
        var projectDirectory = Path.Combine(certificateDirectory, "SecretBackup");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "private.key"), privateKeyContent);
        var backupDirectory = Path.Combine(certificateDirectory, "Backups");
        Directory.CreateDirectory(backupDirectory);
        File.WriteAllText(Path.Combine(backupDirectory, "SecretBackup-20260620010203.zip"), backupContent);
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.CertificateProject);
        Assert.DoesNotContain(privateKeyContent, item.BackupSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(privateKeyContent, item.BackupPath, StringComparison.Ordinal);
        Assert.DoesNotContain(backupContent, item.BackupSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(backupContent, item.BackupPath, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains(privateKeyContent, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains(backupContent, StringComparison.Ordinal));
    }

    [Fact]
    public void ScanDoesNotExposeRawProvisioningProfileContents()
    {
        var secretProfilePayload = "SECRET-PROFILE-PAYLOAD";
        var profilePath = Path.Combine(profileDirectory, "secret.mobileprovision");
        File.WriteAllText(profilePath, secretProfilePayload);
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        Assert.DoesNotContain(result.Items, item => item.Name.Contains(secretProfilePayload, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Items, item => item.Note.Contains(secretProfilePayload, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Items, item => item.SafeMetadataSummary.Contains(secretProfilePayload, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains(secretProfilePayload, StringComparison.Ordinal));
    }

    [Fact]
    public void ScanDoesNotExposeProvisioningProfileCertificatePayloadOrFingerprints()
    {
        var certificatePayload = Convert.ToBase64String([1, 2, 3]);
        var fingerprint = Convert.ToHexString(SHA256.HashData([1, 2, 3]));
        var profilePath = Path.Combine(profileDirectory, "secret.mobileprovision");
        File.WriteAllBytes(profilePath, WrapMobileProvision(ProfilePlist(
            new DateTimeOffset(2027, 1, 2, 0, 0, 0, TimeSpan.Zero),
            [certificatePayload])));
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.ProvisioningProfile);
        Assert.Contains("/ 证书 1", item.SafeMetadataSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(certificatePayload, item.SafeMetadataSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(fingerprint, item.SafeMetadataSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains(certificatePayload, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains(fingerprint, StringComparison.Ordinal));
    }

    [Fact]
    public void ScanDoesNotExposeRawIpaOrEmbeddedProfileContents()
    {
        var secretInfoValue = "SECRET-INFO-PLIST-VALUE";
        var secretProfilePayload = "SECRET-EMBEDDED-PROFILE";
        var ipaPath = Path.Combine(ipaDirectory, "secret.ipa");
        File.WriteAllBytes(ipaPath, CreateIpa(
            InfoPlist(displayName: secretInfoValue),
            Encoding.UTF8.GetBytes(secretProfilePayload)));
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        var item = Assert.Single(result.Items, item => item.Type == LocalAssetType.Ipa);
        Assert.DoesNotContain(secretInfoValue, item.Name, StringComparison.Ordinal);
        Assert.DoesNotContain(secretInfoValue, item.Path, StringComparison.Ordinal);
        Assert.DoesNotContain(secretProfilePayload, item.SafeMetadataSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains(secretInfoValue, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains(secretProfilePayload, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private LocalAssetLibraryRequest ValidRequest() =>
        new(certificateDirectory, profileDirectory, ipaDirectory);

    private static byte[] CreateCertificate(DateTimeOffset expiresAt)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Demo",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(expiresAt.AddDays(-365), expiresAt);
        return certificate.Export(X509ContentType.Cert);
    }

    private static byte[] WrapMobileProvision(string plist) =>
        Encoding.UTF8.GetBytes($"cms-header\0{plist}\0cms-footer");

    private static byte[] CreateIpa(string infoPlist, byte[]? embeddedProfile = null)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "Payload/Demo.app/Info.plist", Encoding.UTF8.GetBytes(infoPlist));
            WriteZipEntry(archive, "Payload/Demo.app/_CodeSignature/CodeResources", Encoding.UTF8.GetBytes("signature marker"));

            if (embeddedProfile is not null)
            {
                WriteZipEntry(archive, "Payload/Demo.app/embedded.mobileprovision", embeddedProfile);
            }
        }

        return stream.ToArray();
    }

    private static void WriteZipEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static string InfoPlist(
        string bundleIdentifier = "com.example.demo",
        string shortVersion = "1.2.3",
        string buildVersion = "45",
        string displayName = "Demo App") =>
        $$"""
          <?xml version="1.0" encoding="UTF-8"?>
          <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
          <plist version="1.0">
          <dict>
              <key>CFBundleIdentifier</key><string>{{bundleIdentifier}}</string>
              <key>CFBundleShortVersionString</key><string>{{shortVersion}}</string>
              <key>CFBundleVersion</key><string>{{buildVersion}}</string>
              <key>CFBundleDisplayName</key><string>{{displayName}}</string>
          </dict>
          </plist>
          """;

    private static string ProfilePlist(
        DateTimeOffset expiresAt,
        string[]? developerCertificates = null)
    {
        var certificatesXml = developerCertificates is null
            ? string.Empty
            : $"""
              <key>DeveloperCertificates</key>
              <array>
              {string.Join(Environment.NewLine, developerCertificates.Select(certificate => $"<data>{certificate}</data>"))}
              </array>
              """;

        return $$"""
          <?xml version="1.0" encoding="UTF-8"?>
          <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
          <plist version="1.0">
          <dict>
              <key>UUID</key><string>PROFILE-UUID</string>
              <key>Name</key><string>Demo App Store</string>
              <key>TeamIdentifier</key>
              <array><string>TEAM123456</string></array>
              <key>CreationDate</key><date>2026-01-01T00:00:00Z</date>
              <key>ExpirationDate</key><date>{{expiresAt:yyyy-MM-ddTHH:mm:ssZ}}</date>
              <key>Entitlements</key>
              <dict>
                  <key>application-identifier</key><string>TEAM123456.com.example.app</string>
                  <key>get-task-allow</key><false/>
              </dict>
              {{certificatesXml}}
          </dict>
          </plist>
          """;
    }
}
