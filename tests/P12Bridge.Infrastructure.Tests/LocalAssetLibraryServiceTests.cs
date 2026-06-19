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
    public void ScanDoesNotReadPrivateKeyContents()
    {
        var projectDirectory = Path.Combine(certificateDirectory, "Secret");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "private.key"), "PRIVATE KEY CONTENT");
        var service = new LocalAssetLibraryService();

        var result = service.Scan(ValidRequest());

        Assert.DoesNotContain(result.Items, item => item.Name.Contains("PRIVATE", StringComparison.Ordinal));
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
}
