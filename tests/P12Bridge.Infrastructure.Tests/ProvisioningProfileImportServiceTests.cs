using System.Text;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class ProvisioningProfileImportServiceTests : IDisposable
{
    private readonly string temporaryDirectory;
    private readonly string importDirectory;
    private readonly ProvisioningProfileImportService service;

    public ProvisioningProfileImportServiceTests()
    {
        temporaryDirectory = Path.Combine(Path.GetTempPath(), "P12BridgeTests", Guid.NewGuid().ToString("N"));
        importDirectory = Path.Combine(temporaryDirectory, "Profiles");
        service = new ProvisioningProfileImportService(
            new ProvisioningProfileParser(),
            new FixedClock(DateTimeOffset.Parse("2026-06-20T00:00:00Z")));
    }

    [Fact]
    public void ImportCopiesProfileAndReturnsMetadata()
    {
        var profilePath = WriteProfile("source.mobileprovision", ProfilePlist());

        var result = service.Import(new ProvisioningProfileImportRequest(profilePath, importDirectory));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Profile);
        Assert.Equal("Demo App Store", result.Profile.Name);
        Assert.Equal("TEAM123456", result.Profile.TeamId);
        Assert.Equal("com.example.app", result.Profile.BundleIdentifier);
        Assert.Equal(ProvisioningProfileType.AppStore, result.Profile.Type);
        Assert.Equal(ProvisioningProfileStatus.Active, result.Profile.Status);
        Assert.Equal(Path.Combine(importDirectory, "Demo-App-Store-PROFILE-UUID.mobileprovision"), result.ImportedPath);
        Assert.True(File.Exists(result.ImportedPath));
    }

    [Fact]
    public void ImportReturnsParsedExpiredProfileWithIssue()
    {
        var profilePath = WriteProfile("expired.mobileprovision", ProfilePlist(expirationDate: "2026-01-01T00:00:00Z"));

        var result = service.Import(new ProvisioningProfileImportRequest(profilePath, importDirectory));

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Profile);
        Assert.Equal(ProvisioningProfileStatus.Expired, result.Profile.Status);
        Assert.True(File.Exists(result.ImportedPath));
        Assert.Contains(result.Issues, issue => issue.Code == ProvisioningProfileErrorCodes.ExpiredProfile);
    }

    [Fact]
    public void ImportRejectsMissingFilePath()
    {
        var result = service.Import(new ProvisioningProfileImportRequest(" ", importDirectory));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == ProvisioningProfileErrorCodes.ImportFileMissing);
    }

    [Fact]
    public void ImportRejectsMissingFile()
    {
        var result = service.Import(new ProvisioningProfileImportRequest(
            Path.Combine(temporaryDirectory, "missing.mobileprovision"),
            importDirectory));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == ProvisioningProfileErrorCodes.ImportFileNotFound);
    }

    [Fact]
    public void ImportRejectsMalformedProfile()
    {
        var profilePath = WriteProfile("bad.mobileprovision", "not a profile");

        var result = service.Import(new ProvisioningProfileImportRequest(profilePath, importDirectory));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Profile);
        Assert.Contains(result.Issues, issue => issue.Code == ProvisioningProfileErrorCodes.PlistNotFound);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private string WriteProfile(string fileName, string payload)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, fileName);
        File.WriteAllText(path, $"cms-header\0{payload}\0cms-footer", Encoding.UTF8);
        return path;
    }

    private static string ProfilePlist(string expirationDate = "2026-12-31T00:00:00Z") =>
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
              <key>ExpirationDate</key><date>{{expirationDate}}</date>
              <key>Entitlements</key>
              <dict>
                  <key>application-identifier</key><string>TEAM123456.com.example.app</string>
                  <key>get-task-allow</key><false/>
              </dict>
          </dict>
          </plist>
          """;

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
