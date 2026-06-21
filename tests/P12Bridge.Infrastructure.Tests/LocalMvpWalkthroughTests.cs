using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class LocalMvpWalkthroughTests : IDisposable
{
    private static readonly byte[] ProfileCertificatePayload = [1, 2, 3];
    private readonly string temporaryDirectory;
    private readonly DateTimeOffset now = DateTimeOffset.Parse("2026-06-20T08:30:00Z");

    public LocalMvpWalkthroughTests()
    {
        temporaryDirectory = Path.Combine(Path.GetTempPath(), "P12BridgeTests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void LocalWorkflowCreatesAssetsImportsIpaAndReportsUploadReady()
    {
        var certificateService = new CertificateProjectService(
            new LocalCertificateService(),
            new FixedClock(now));
        var profileService = new ProvisioningProfileImportService(
            new ProvisioningProfileParser(),
            new FixedClock(now));
        var ipaService = new IpaImportService(new IpaInspector());
        var readinessEvaluator = new UploadReadinessEvaluator();

        var certificateDirectory = Path.Combine(temporaryDirectory, "Certificates");
        var profileDirectory = Path.Combine(temporaryDirectory, "Profiles");
        var ipaDirectory = Path.Combine(temporaryDirectory, "IPAs");

        var certificateResult = certificateService.Create(new CertificateProjectCreateRequest(
            "Demo App",
            SigningPurpose.Distribution,
            new CertificateSubject(
                "Developer Name",
                EmailAddress: "developer@example.com",
                Organization: "P12Bridge",
                CountryCode: "CN"),
            certificateDirectory,
            "Local walkthrough"));

        Assert.True(certificateResult.IsSuccess);
        Assert.NotNull(certificateResult.Project);
        Assert.NotNull(certificateResult.Artifacts);
        Assert.True(File.Exists(certificateResult.Artifacts.PrivateKeyPath));
        Assert.True(File.Exists(certificateResult.Artifacts.CertificateSigningRequestPath));
        Assert.True(File.Exists(certificateResult.Artifacts.MetadataPath));

        var profilePath = WriteProfile("Demo.mobileprovision");
        var profileResult = profileService.Import(new ProvisioningProfileImportRequest(
            profilePath,
            profileDirectory));

        Assert.True(profileResult.IsSuccess);
        Assert.NotNull(profileResult.Profile);
        Assert.Equal(ProvisioningProfileType.AppStore, profileResult.Profile.Type);
        Assert.Equal(ProvisioningProfileStatus.Active, profileResult.Profile.Status);
        Assert.Equal("com.example.demo", profileResult.Profile.BundleIdentifier);
        Assert.True(File.Exists(profileResult.ImportedPath));

        var ipaPath = WriteIpa("Demo.ipa", CreateIpa(File.ReadAllBytes(profilePath)));
        var ipaResult = ipaService.Import(new IpaImportRequest(ipaPath, ipaDirectory));

        Assert.True(ipaResult.IsSuccess);
        Assert.NotNull(ipaResult.Metadata);
        Assert.True(File.Exists(ipaResult.ImportedPath));
        Assert.Equal("com.example.demo", ipaResult.Metadata.BundleIdentifier);
        Assert.True(ipaResult.Metadata.HasEmbeddedProvisioningProfile);
        Assert.NotNull(ipaResult.Metadata.EmbeddedProvisioningProfile);
        Assert.True(ipaResult.Metadata.SignaturePresence.HasCodeResources);

        var assetDescriptionPath = Path.Combine(temporaryDirectory, "AppStoreInfo.plist");
        File.WriteAllText(assetDescriptionPath, "synthetic app store info path marker");

        var readiness = readinessEvaluator.Evaluate(new UploadReadinessRequest(
            UploadTarget.AppStore,
            ipaResult.Metadata,
            profileResult.Profile,
            ipaResult.ImportedPath,
            assetDescriptionPath,
            [Convert.ToHexString(SHA256.HashData(ProfileCertificatePayload))]),
            now);

        Assert.True(readiness.IsReady);
        Assert.Equal(UploadReadinessStatus.Ready, readiness.Status);
        Assert.Empty(readiness.Issues);
        Assert.All(readiness.Checks, check => Assert.Equal(UploadReadinessCheckStatus.Passed, check.Status));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private string WriteProfile(string fileName)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, fileName);
        File.WriteAllText(path, $"cms-header\0{ProfilePlist()}\0cms-footer", Encoding.UTF8);
        return path;
    }

    private string WriteIpa(string fileName, byte[] bytes)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] CreateIpa(byte[] embeddedProfile)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "Payload/Demo.app/Info.plist", Encoding.UTF8.GetBytes(InfoPlist()));
            WriteEntry(archive, "Payload/Demo.app/embedded.mobileprovision", embeddedProfile);
            WriteEntry(archive, "Payload/Demo.app/_CodeSignature/CodeResources", Encoding.UTF8.GetBytes("signature marker"));
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name);
        using var entryStream = entry.Open();
        entryStream.Write(bytes);
    }

    private static string ProfilePlist() =>
        $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>UUID</key><string>PROFILE-UUID</string>
            <key>Name</key><string>Demo App Store</string>
            <key>TeamIdentifier</key>
            <array><string>TEAM123456</string></array>
            <key>CreationDate</key><date>2026-01-01T00:00:00Z</date>
            <key>ExpirationDate</key><date>2026-12-31T00:00:00Z</date>
            <key>DeveloperCertificates</key>
            <array><data>{{Convert.ToBase64String(ProfileCertificatePayload)}}</data></array>
            <key>Entitlements</key>
            <dict>
                <key>application-identifier</key><string>TEAM123456.com.example.demo</string>
                <key>get-task-allow</key><false/>
            </dict>
        </dict>
        </plist>
        """;

    private static string InfoPlist() =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>CFBundleIdentifier</key><string>com.example.demo</string>
            <key>CFBundleShortVersionString</key><string>1.2.3</string>
            <key>CFBundleVersion</key><string>45</string>
            <key>CFBundleDisplayName</key><string>Demo App</string>
        </dict>
        </plist>
        """;

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
