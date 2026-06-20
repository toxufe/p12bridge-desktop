using System.Text;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class TextExportServiceTests : IDisposable
{
    private readonly string tempDirectory;
    private readonly TextExportService service = new();

    public TextExportServiceTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"p12bridge-text-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void ExportWritesProvidedText()
    {
        var outputPath = Path.Combine(tempDirectory, "evidence.txt");
        var content = $"上传证据{Environment.NewLine}Bundle: com.example.app";

        var result = service.Export(new TextExportRequest(outputPath, content));

        Assert.True(result.IsSuccess);
        Assert.Equal(outputPath, result.OutputPath);
        Assert.Empty(result.Issues);
        Assert.Equal(content, File.ReadAllText(outputPath, Encoding.UTF8));
    }

    [Fact]
    public void ExportRejectsEmptyOutputPath()
    {
        var result = service.Export(new TextExportRequest(" ", "history"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == TextExportErrorCodes.OutputPathMissing);
        Assert.DoesNotContain(result.Issues, issue => issue.Code == TextExportErrorCodes.WriteFailed);
    }

    [Fact]
    public void ExportRejectsEmptyContent()
    {
        var outputPath = Path.Combine(tempDirectory, "empty.txt");

        var result = service.Export(new TextExportRequest(outputPath, " "));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == TextExportErrorCodes.ContentEmpty);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void ExportReturnsWriteFailedForMissingDirectory()
    {
        var outputPath = Path.Combine(tempDirectory, "missing", "history.txt");

        var result = service.Export(new TextExportRequest(outputPath, "history"));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == TextExportErrorCodes.WriteFailed);
        Assert.False(File.Exists(outputPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
