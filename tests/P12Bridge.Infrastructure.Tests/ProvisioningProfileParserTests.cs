using System.Security.Cryptography;
using System.Text;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class ProvisioningProfileParserTests
{
    private readonly ProvisioningProfileParser parser = new();

    [Fact]
    public void ParseReadsDevelopmentProfileMetadata()
    {
        var profileBytes = WrapMobileProvision(ProfilePlist(
            provisionedDevices: ["device-a", "device-b"],
            getTaskAllow: true,
            developerCertificates: [Convert.ToBase64String([1, 2, 3])]));

        var result = parser.Parse(profileBytes, DateTimeOffset.Parse("2026-06-19T00:00:00Z"));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Profile);
        Assert.Equal("PROFILE-UUID", result.Profile.Uuid);
        Assert.Equal("Demo Development", result.Profile.Name);
        Assert.Equal("TEAM123456", result.Profile.TeamId);
        Assert.Equal("TEAM123456.com.example.app", result.Profile.ApplicationIdentifier);
        Assert.Equal("com.example.app", result.Profile.BundleIdentifier);
        Assert.Equal(ProvisioningProfileType.Development, result.Profile.Type);
        Assert.Equal(ProvisioningProfileStatus.Active, result.Profile.Status);
        Assert.Equal(2, result.Profile.ProvisionedDeviceCount);
        Assert.Equal(Convert.ToHexString(SHA256.HashData([1, 2, 3])), Assert.Single(result.Profile.DeveloperCertificateFingerprints));
    }

    [Fact]
    public void ParseReadsAppStoreProfileMetadata()
    {
        var profileBytes = WrapMobileProvision(ProfilePlist(
            name: "Demo App Store",
            provisionedDevices: null,
            getTaskAllow: false));

        var result = parser.Parse(profileBytes, DateTimeOffset.Parse("2026-06-19T00:00:00Z"));

        Assert.True(result.IsSuccess);
        Assert.Equal(ProvisioningProfileType.AppStore, result.Profile?.Type);
        Assert.Equal(0, result.Profile?.ProvisionedDeviceCount);
    }

    [Fact]
    public void ParseMarksExpiredProfile()
    {
        var profileBytes = WrapMobileProvision(ProfilePlist(expirationDate: "2026-01-01T00:00:00Z"));

        var result = parser.Parse(profileBytes, DateTimeOffset.Parse("2026-06-19T00:00:00Z"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ProvisioningProfileStatus.Expired, result.Profile?.Status);
        Assert.Contains(result.Issues, issue => issue.Code == ProvisioningProfileErrorCodes.ExpiredProfile);
    }

    [Fact]
    public void ParseRejectsMalformedPayload()
    {
        var result = parser.Parse(Encoding.UTF8.GetBytes("not a profile"));

        Assert.False(result.IsSuccess);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ProvisioningProfileErrorCodes.PlistNotFound, issue.Code);
    }

    [Fact]
    public void ParseRejectsMissingRequiredKey()
    {
        var plist = ProfilePlist().Replace("<key>UUID</key><string>PROFILE-UUID</string>", string.Empty, StringComparison.Ordinal);

        var result = parser.Parse(WrapMobileProvision(plist));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == ProvisioningProfileErrorCodes.MissingRequiredKey);
    }

    private static byte[] WrapMobileProvision(string plist) =>
        Encoding.UTF8.GetBytes($"cms-header\0{plist}\0cms-footer");

    private static string ProfilePlist(
        string name = "Demo Development",
        string expirationDate = "2026-12-31T00:00:00Z",
        string[]? provisionedDevices = null,
        bool getTaskAllow = true,
        string[]? developerCertificates = null)
    {
        var devicesXml = provisionedDevices is null
            ? string.Empty
            : $"""
               <key>ProvisionedDevices</key>
               <array>
               {string.Join(Environment.NewLine, provisionedDevices.Select(device => $"<string>{device}</string>"))}
               </array>
               """;

        var certificatesXml = developerCertificates is null
            ? string.Empty
            : $"""
               <key>DeveloperCertificates</key>
               <array>
               {string.Join(Environment.NewLine, developerCertificates.Select(certificate => $"<data>{certificate}</data>"))}
               </array>
               """;

        var taskAllowXml = getTaskAllow ? "<true/>" : "<false/>";

        return $$"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>UUID</key><string>PROFILE-UUID</string>
                    <key>Name</key><string>{{name}}</string>
                    <key>TeamIdentifier</key>
                    <array><string>TEAM123456</string></array>
                    <key>CreationDate</key><date>2026-01-01T00:00:00Z</date>
                    <key>ExpirationDate</key><date>{{expirationDate}}</date>
                    {{devicesXml}}
                    {{certificatesXml}}
                    <key>Entitlements</key>
                    <dict>
                        <key>application-identifier</key><string>TEAM123456.com.example.app</string>
                        <key>get-task-allow</key>{{taskAllowXml}}
                    </dict>
                </dict>
                </plist>
                """;
    }
}
