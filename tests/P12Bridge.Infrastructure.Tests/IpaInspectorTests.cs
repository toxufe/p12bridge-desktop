using System.IO.Compression;
using System.Buffers.Binary;
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
    public void InspectReadsBinaryInfoPlistMetadata()
    {
        var inspector = new IpaInspector();
        var ipaBytes = CreateIpa(infoPlistBytes: BinaryInfoPlist(new Dictionary<string, string>
        {
            ["CFBundleIdentifier"] = "com.example.binary",
            ["CFBundleShortVersionString"] = "2.0.1",
            ["CFBundleVersion"] = "99"
        }));

        var result = inspector.Inspect(ipaBytes);

        Assert.True(result.IsSuccess);
        Assert.Equal("com.example.binary", result.Metadata?.BundleIdentifier);
        Assert.Equal("2.0.1", result.Metadata?.ShortVersion);
        Assert.Equal("99", result.Metadata?.BuildVersion);
    }

    [Fact]
    public void InspectRejectsBinaryInfoPlistMissingRequiredKey()
    {
        var inspector = new IpaInspector();
        var ipaBytes = CreateIpa(infoPlistBytes: BinaryInfoPlist(new Dictionary<string, string>
        {
            ["CFBundleIdentifier"] = "com.example.binary",
            ["CFBundleVersion"] = "99"
        }));

        var result = inspector.Inspect(ipaBytes);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.MissingRequiredKey);
    }

    [Fact]
    public void InspectRejectsMalformedBinaryInfoPlist()
    {
        var inspector = new IpaInspector();
        var ipaBytes = CreateIpa(infoPlistBytes: Encoding.ASCII.GetBytes("bplist00binary-data"));

        var result = inspector.Inspect(ipaBytes);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.InfoPlistMalformed);
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

    private static byte[] BinaryInfoPlist(IReadOnlyDictionary<string, string> values)
    {
        var objects = new List<byte[]>();
        foreach (var key in values.Keys)
        {
            objects.Add(BinaryAsciiString(key));
        }

        foreach (var value in values.Values)
        {
            objects.Add(BinaryAsciiString(value));
        }

        var objectRefSize = SizeFor((ulong)(values.Count * 2));
        objects.Add(BinaryDictionary(values.Count, objectRefSize));

        var offsets = new List<int>();
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes("bplist00"));
        foreach (var item in objects)
        {
            offsets.Add((int)stream.Position);
            stream.Write(item);
        }

        var offsetTableOffset = (int)stream.Position;
        var offsetIntSize = SizeFor((ulong)offsetTableOffset);
        foreach (var offset in offsets)
        {
            WriteSizedUnsigned(stream, (ulong)offset, offsetIntSize);
        }

        Span<byte> trailer = stackalloc byte[32];
        trailer[6] = (byte)offsetIntSize;
        trailer[7] = (byte)objectRefSize;
        BinaryPrimitives.WriteUInt64BigEndian(trailer[8..16], (ulong)objects.Count);
        BinaryPrimitives.WriteUInt64BigEndian(trailer[16..24], (ulong)(objects.Count - 1));
        BinaryPrimitives.WriteUInt64BigEndian(trailer[24..32], (ulong)offsetTableOffset);
        stream.Write(trailer);
        return stream.ToArray();

        byte[] BinaryDictionary(int count, int refSize)
        {
            using var dictStream = new MemoryStream();
            WriteCollectionMarker(dictStream, 0xD0, count);

            for (var index = 0; index < count; index++)
            {
                WriteSizedUnsigned(dictStream, (ulong)index, refSize);
            }

            for (var index = 0; index < count; index++)
            {
                WriteSizedUnsigned(dictStream, (ulong)(count + index), refSize);
            }

            return dictStream.ToArray();
        }
    }

    private static byte[] BinaryAsciiString(string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        using var stream = new MemoryStream();
        WriteCollectionMarker(stream, 0x50, bytes.Length);
        stream.Write(bytes);
        return stream.ToArray();
    }

    private static void WriteCollectionMarker(Stream stream, int type, int count)
    {
        if (count < 0x0F)
        {
            stream.WriteByte((byte)(type | count));
            return;
        }

        stream.WriteByte((byte)(type | 0x0F));
        WriteIntegerObject(stream, (ulong)count);
    }

    private static void WriteIntegerObject(Stream stream, ulong value)
    {
        var size = SizeFor(value);
        stream.WriteByte(size switch
        {
            1 => 0x10,
            2 => 0x11,
            4 => 0x12,
            _ => 0x13
        });
        WriteSizedUnsigned(stream, value, size);
    }

    private static int SizeFor(ulong value)
    {
        if (value <= byte.MaxValue)
        {
            return 1;
        }

        if (value <= ushort.MaxValue)
        {
            return 2;
        }

        return value <= uint.MaxValue ? 4 : 8;
    }

    private static void WriteSizedUnsigned(Stream stream, ulong value, int size)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        stream.Write(buffer[(8 - size)..]);
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
