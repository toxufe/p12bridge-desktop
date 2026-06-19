using System.IO.Compression;
using System.Security;
using System.Text;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class CertificateProjectBackupService : ICertificateProjectBackupService
{
    private const string MetadataFileName = "p12bridge.project.json";

    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "bin",
        "obj"
    };

    private readonly IClock clock;

    public CertificateProjectBackupService(IClock? clock = null)
    {
        this.clock = clock ?? new SystemClock();
    }

    public CertificateProjectBackupResult Export(CertificateProjectBackupRequest request)
    {
        var issues = ValidateRequest(request);
        if (issues.Count > 0)
        {
            return CertificateProjectBackupResult.Failure(issues.ToArray());
        }

        try
        {
            var projectName = SanitizeFileName(Path.GetFileName(
                Path.GetFullPath(request.ProjectDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            var backupPath = Path.Combine(request.OutputDirectory, $"{projectName}-{clock.UtcNow:yyyyMMddHHmmss}.zip");
            var filesIncluded = 0;

            using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
            foreach (var filePath in EnumerateBackupFiles(request.ProjectDirectory))
            {
                var relativePath = Path.GetRelativePath(request.ProjectDirectory, filePath)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
                filesIncluded++;
            }

            return CertificateProjectBackupResult.Success(backupPath, filesIncluded);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException)
        {
            return CertificateProjectBackupResult.Failure(new ValidationIssue(
                CertificateProjectBackupErrorCodes.ExportFailed,
                ValidationSeverity.Error,
                "备份失败",
                "检查目录权限"));
        }
    }

    private static List<ValidationIssue> ValidateRequest(CertificateProjectBackupRequest request)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(request.ProjectDirectory))
        {
            issues.Add(new ValidationIssue(
                CertificateProjectBackupErrorCodes.ProjectDirectoryMissing,
                ValidationSeverity.Error,
                "项目目录必填",
                "选择证书项目"));
        }
        else if (!Directory.Exists(request.ProjectDirectory))
        {
            issues.Add(new ValidationIssue(
                CertificateProjectBackupErrorCodes.ProjectNotFound,
                ValidationSeverity.Error,
                "项目不存在",
                "刷新资产库"));
        }
        else if (!File.Exists(Path.Combine(request.ProjectDirectory, MetadataFileName)))
        {
            issues.Add(new ValidationIssue(
                CertificateProjectBackupErrorCodes.MetadataMissing,
                ValidationSeverity.Error,
                "项目无效",
                "选择证书项目"));
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            issues.Add(new ValidationIssue(
                CertificateProjectBackupErrorCodes.OutputDirectoryMissing,
                ValidationSeverity.Error,
                "备份目录必填",
                "选择目录"));
        }
        else if (!Directory.Exists(request.OutputDirectory))
        {
            issues.Add(new ValidationIssue(
                CertificateProjectBackupErrorCodes.OutputDirectoryNotFound,
                ValidationSeverity.Error,
                "备份目录不存在",
                "重新选择"));
        }

        return issues;
    }

    private static IEnumerable<string> EnumerateBackupFiles(string projectDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(projectDirectory);

        while (pending.Count > 0)
        {
            var currentDirectory = pending.Pop();

            foreach (var directory in Directory.EnumerateDirectories(currentDirectory))
            {
                if (!ExcludedDirectoryNames.Contains(Path.GetFileName(directory)))
                {
                    pending.Push(directory);
                }
            }

            foreach (var filePath in Directory.EnumerateFiles(currentDirectory))
            {
                yield return filePath;
            }
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Trim())
        {
            builder.Append(char.IsWhiteSpace(character) || invalidChars.Contains(character)
                ? '-'
                : character);
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "certificate-project" : sanitized;
    }
}
