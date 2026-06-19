using System.IO.Compression;
using System.Text;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class IpaImportServiceTests : IDisposable
{
    private readonly string temporaryDirectory;
    private readonly string importDirectory;
    private readonly IpaImportService service;

    public IpaImportServiceTests()
    {
        temporaryDirectory = Path.Combine(Path.GetTempPath(), "P12BridgeTests", Guid.NewGuid().ToString("N"));
        importDirectory = Path.Combine(temporaryDirectory, "IPAs");
        service = new IpaImportService(new IpaInspector());
    }

    [Fact]
    public void ImportCopiesIpaAndReturnsMetadata()
    {
        var ipaPath = WriteIpa("Demo.ipa", CreateIpa(includeCodeResources: true));

        var result = service.Import(new IpaImportRequest(ipaPath, importDirectory));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Metadata);
        Assert.Equal("com.example.demo", result.Metadata.BundleIdentifier);
        Assert.Equal("1.2.3", result.Metadata.ShortVersion);
        Assert.Equal("45", result.Metadata.BuildVersion);
        Assert.True(result.Metadata.SignaturePresence.HasCodeResources);
        Assert.Equal(Path.Combine(importDirectory, "com.example.demo-1.2.3-45.ipa"), result.ImportedPath);
        Assert.True(File.Exists(result.ImportedPath));
    }

    [Fact]
    public void ImportRejectsMissingFilePath()
    {
        var result = service.Import(new IpaImportRequest(" ", importDirectory));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.ImportFileMissing);
    }

    [Fact]
    public void ImportRejectsMissingFile()
    {
        var result = service.Import(new IpaImportRequest(
            Path.Combine(temporaryDirectory, "missing.ipa"),
            importDirectory));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.ImportFileNotFound);
    }

    [Fact]
    public void ImportRejectsInvalidIpa()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var ipaPath = Path.Combine(temporaryDirectory, "bad.ipa");
        File.WriteAllText(ipaPath, "not a zip");

        var result = service.Import(new IpaImportRequest(ipaPath, importDirectory));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Metadata);
        Assert.Contains(result.Issues, issue => issue.Code == IpaInspectionErrorCodes.InvalidArchive);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private string WriteIpa(string fileName, byte[] bytes)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] CreateIpa(bool includeCodeResources = false)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "Payload/Demo.app/Info.plist", InfoPlist());

            if (includeCodeResources)
            {
                WriteEntry(archive, "Payload/Demo.app/_CodeSignature/CodeResources", "signature marker");
            }
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name);
        using var entryStream = entry.Open();
        entryStream.Write(Encoding.UTF8.GetBytes(value));
    }

    private static string InfoPlist() =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>CFBundleIdentifier</key><string>com.example.demo</string>
            <key>CFBundleShortVersionString</key><string>1.2.3</string>
            <key>CFBundleVersion</key><string>45</string>
        </dict>
        </plist>
        """;
}
