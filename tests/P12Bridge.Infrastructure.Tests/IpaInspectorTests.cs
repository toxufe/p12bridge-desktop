using System.IO.Compression;
using System.Text;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class IpaInspectorTests
{
    [Fact]
    public void InspectReadsMetadataSignatureFlagsAndEmbeddedProfile()
    {
        var profile = new ProvisioningProfile(
            "profile-uuid",
            "Demo App Store",
            "TEAM123456",
            "TEAM123456.com.example.demo",
            "com.example.demo",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
            ProvisioningProfileType.AppStore,
            ProvisioningProfileStatus.Active,
            0,
            Array.Empty<string>());
        var parser = new StubProvisioningProfileParser(ProvisioningProfileParseResult.Success(profile));
        var inspector = new IpaInspector(parser);
        var ipaBytes = CreateIpa(
            infoPlist: InfoPlist(),
            embeddedProfile: Encoding.UTF8.GetBytes("profile bytes"),
            includeCodeResources: true);

        var result = inspector.Inspect(ipaBytes, DateTimeOffset.Parse("2026-06-19T00:00:00Z"));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Metadata);
        Assert.Equal(ipaBytes.Length, result.Metadata.FileSizeBytes);
        Assert.Equal("Payload/Demo.app", result.Metadata.AppBundlePath);
        Assert.Equal("com.example.demo", result.Metadata.BundleIdentifier);
        Assert.Equal("1.2.3", result.Metadata.ShortVersion);
        Assert.Equal("45", result.Metadata.BuildVersion);
        Assert.True(result.Metadata.HasEmbeddedProvisioningProfile);
        Assert.True(result.Metadata.SignaturePresence.HasEmbeddedProvisioningProfile);
        Assert.True(result.Metadata.SignaturePresence.HasCodeResources);
        Assert.Equal(profile, result.Metadata.EmbeddedProvisioningProfile);
        Assert.Equal(Encoding.UTF8.GetBytes("profile bytes"), parser.LastBytes);
    }

    [Fact]
    public void InspectAllowsMissingEmbeddedProfile()
    {
        var inspector = new IpaInspector(new StubProvisioningProfileParser(ProvisioningProfileParseResult.Failure()));
        var ipaBytes = CreateIpa(infoPlist: InfoPlist());

        var result = inspector.Inspect(ipaBytes);

        Assert.True(result.IsSuccess);
        Assert.False(result.Metadata?.HasEmbeddedProvisioningProfile);
        Assert.False(result.Metadata?.SignaturePresence.HasEmbeddedProvisioningProfile);
        Assert.Null(result.Metadata?.EmbeddedProvisioningProfile);
    }

    [Fact]
    public void InspectRejectsEmptyInput()
    {
        var inspector = new IpaInspector();

        var result = inspector.Inspect(Array.Empty<byte>());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.EmptyPayload);
    }

    [Fact]
    public void InspectRejectsInvalidZip()
    {
        var inspector = new IpaInspector();

        var result = inspector.Inspect(Encoding.UTF8.GetBytes("not a zip"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.InvalidArchive);
    }

    [Fact]
    public void InspectRejectsMissingAppBundle()
    {
        var inspector = new IpaInspector();
        var ipaBytes = CreateZip(("README.txt", "not an app"));

        var result = inspector.Inspect(ipaBytes);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.AppBundleMissing);
    }

    [Fact]
    public void InspectRejectsMultipleAppBundles()
    {
        var inspector = new IpaInspector();
        var ipaBytes = CreateZip(
            ("Payload/One.app/Info.plist", InfoPlist(bundleIdentifier: "com.example.one")),
            ("Payload/Two.app/Info.plist", InfoPlist(bundleIdentifier: "com.example.two")));

        var result = inspector.Inspect(ipaBytes);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.MultipleAppBundles);
    }

    [Fact]
    public void InspectRejectsMissingInfoPlist()
    {
        var inspector = new IpaInspector();
        var ipaBytes = CreateZip(("Payload/Demo.app/embedded.mobileprovision", "profile"));

        var result = inspector.Inspect(ipaBytes);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.InfoPlistMissing);
    }

    [Fact]
    public void InspectRejectsMalformedXmlInfoPlist()
    {
        var inspector = new IpaInspector();
        var ipaBytes = CreateIpa(infoPlist: "<plist><dict>");

        var result = inspector.Inspect(ipaBytes);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.InfoPlistMalformed);
    }

    [Fact]
    public void InspectRejectsBinaryInfoPlistWithStableUnsupportedError()
    {
        var inspector = new IpaInspector();
        var ipaBytes = CreateIpa(infoPlistBytes: Encoding.ASCII.GetBytes("bplist00binary-data"));

        var result = inspector.Inspect(ipaBytes);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.InfoPlistUnsupported);
    }

    [Fact]
    public void InspectRejectsMissingRequiredInfoPlistKey()
    {
        var inspector = new IpaInspector();
        var ipaBytes = CreateIpa(infoPlist: InfoPlist(bundleIdentifier: null));

        var result = inspector.Inspect(ipaBytes);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.MissingRequiredKey);
    }

    [Fact]
    public void InspectPropagatesEmbeddedProfileParseFailure()
    {
        var profileIssue = new ValidationIssue(
            ProvisioningProfileErrorCodes.PlistNotFound,
            ValidationSeverity.Error,
            "Profile plist missing.");
        var inspector = new IpaInspector(new StubProvisioningProfileParser(ProvisioningProfileParseResult.Failure(profileIssue)));
        var ipaBytes = CreateIpa(
            infoPlist: InfoPlist(),
            embeddedProfile: Encoding.UTF8.GetBytes("bad profile"));

        var result = inspector.Inspect(ipaBytes);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Metadata);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.EmbeddedProfileInvalid);
        Assert.Contains(result.Issues, issue => issue.Code == ProvisioningProfileErrorCodes.PlistNotFound);
    }

    private static byte[] CreateIpa(
        string? infoPlist = null,
        byte[]? infoPlistBytes = null,
        byte[]? embeddedProfile = null,
        bool includeCodeResources = false)
    {
        var entries = new List<(string Name, byte[] Bytes)>
        {
            ("Payload/Demo.app/Info.plist", infoPlistBytes ?? Encoding.UTF8.GetBytes(infoPlist ?? InfoPlist()))
        };

        if (embeddedProfile is not null)
        {
            entries.Add(("Payload/Demo.app/embedded.mobileprovision", embeddedProfile));
        }

        if (includeCodeResources)
        {
            entries.Add(("Payload/Demo.app/_CodeSignature/CodeResources", Encoding.UTF8.GetBytes("signature marker")));
        }

        return CreateZip(entries.ToArray());
    }

    private static byte[] CreateZip(params (string Name, string Text)[] entries) =>
        CreateZip(entries.Select(entry => (entry.Name, Encoding.UTF8.GetBytes(entry.Text))).ToArray());

    private static byte[] CreateZip(params (string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Name);
                using var entryStream = zipEntry.Open();
                entryStream.Write(entry.Bytes);
            }
        }

        return stream.ToArray();
    }

    private static string InfoPlist(
        string? bundleIdentifier = "com.example.demo",
        string? shortVersion = "1.2.3",
        string? buildVersion = "45")
    {
        var bundleIdentifierXml = bundleIdentifier is null
            ? string.Empty
            : $"<key>CFBundleIdentifier</key><string>{bundleIdentifier}</string>";
        var shortVersionXml = shortVersion is null
            ? string.Empty
            : $"<key>CFBundleShortVersionString</key><string>{shortVersion}</string>";
        var buildVersionXml = buildVersion is null
            ? string.Empty
            : $"<key>CFBundleVersion</key><string>{buildVersion}</string>";

        return $$"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    {{bundleIdentifierXml}}
                    {{shortVersionXml}}
                    {{buildVersionXml}}
                </dict>
                </plist>
                """;
    }

    private sealed class StubProvisioningProfileParser : IProvisioningProfileParser
    {
        private readonly ProvisioningProfileParseResult result;

        public StubProvisioningProfileParser(ProvisioningProfileParseResult result)
        {
            this.result = result;
        }

        public byte[]? LastBytes { get; private set; }

        public ProvisioningProfileParseResult Parse(byte[] mobileProvisionBytes, DateTimeOffset? now = null)
        {
            LastBytes = mobileProvisionBytes;
            return result;
        }
    }
}
