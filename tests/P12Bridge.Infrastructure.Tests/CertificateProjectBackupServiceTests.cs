using System.IO.Compression;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class CertificateProjectBackupServiceTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"p12bridge-backup-{Guid.NewGuid():N}");
    private readonly string projectDirectory;
    private readonly string outputDirectory;
    private readonly CertificateProjectBackupService service;

    public CertificateProjectBackupServiceTests()
    {
        projectDirectory = Path.Combine(tempDirectory, "Demo Project");
        outputDirectory = Path.Combine(tempDirectory, "Backups");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(outputDirectory);
        service = new CertificateProjectBackupService(new FakeClock());
    }

    [Fact]
    public void ExportCreatesZipWithProjectFilesAndRelativePaths()
    {
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        File.WriteAllText(Path.Combine(projectDirectory, "private.key"), "sensitive");
        Directory.CreateDirectory(Path.Combine(projectDirectory, "logs"));
        File.WriteAllText(Path.Combine(projectDirectory, "logs", "run.txt"), "ok");

        var result = service.Export(new CertificateProjectBackupRequest(projectDirectory, outputDirectory));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.FilesIncluded);
        Assert.Equal(Path.Combine(outputDirectory, "Demo-Project-20260620010203.zip"), result.BackupPath);

        using var archive = ZipFile.OpenRead(result.BackupPath);
        var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("p12bridge.project.json", entries);
        Assert.Contains("private.key", entries);
        Assert.Contains("logs/run.txt", entries);
    }

    [Fact]
    public void ExportSkipsTransientDirectories()
    {
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");
        Directory.CreateDirectory(Path.Combine(projectDirectory, "bin"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "obj"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, ".git"));
        File.WriteAllText(Path.Combine(projectDirectory, "bin", "temp.txt"), "bin");
        File.WriteAllText(Path.Combine(projectDirectory, "obj", "temp.txt"), "obj");
        File.WriteAllText(Path.Combine(projectDirectory, ".git", "config"), "git");

        var result = service.Export(new CertificateProjectBackupRequest(projectDirectory, outputDirectory));

        using var archive = ZipFile.OpenRead(result.BackupPath);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("bin/", StringComparison.Ordinal));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("obj/", StringComparison.Ordinal));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith(".git/", StringComparison.Ordinal));
    }

    [Fact]
    public void ExportRejectsMissingProjectDirectory()
    {
        var result = service.Export(new CertificateProjectBackupRequest(
            Path.Combine(tempDirectory, "missing"),
            outputDirectory));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == CertificateProjectBackupErrorCodes.ProjectNotFound);
    }

    [Fact]
    public void ExportRejectsMissingMetadata()
    {
        var result = service.Export(new CertificateProjectBackupRequest(projectDirectory, outputDirectory));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == CertificateProjectBackupErrorCodes.MetadataMissing);
    }

    [Fact]
    public void ExportRejectsMissingOutputDirectory()
    {
        File.WriteAllText(Path.Combine(projectDirectory, "p12bridge.project.json"), "{}");

        var result = service.Export(new CertificateProjectBackupRequest(projectDirectory, " "));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == CertificateProjectBackupErrorCodes.OutputDirectoryMissing);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 6, 20, 1, 2, 3, TimeSpan.Zero);
    }
}
