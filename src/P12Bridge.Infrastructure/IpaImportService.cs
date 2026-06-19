using System.Security;
using System.Text;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class IpaImportService : IIpaImportService
{
    private readonly IIpaInspector inspector;

    public IpaImportService(IIpaInspector inspector)
    {
        this.inspector = inspector;
    }

    public IpaImportResult Import(IpaImportRequest request)
    {
        var inputIssues = ValidateRequest(request);
        if (inputIssues.Count > 0)
        {
            return IpaImportResult.Failure(inputIssues.ToArray());
        }

        try
        {
            var ipaBytes = File.ReadAllBytes(request.IpaPath);
            var inspectResult = inspector.Inspect(ipaBytes);
            if (inspectResult.Metadata is null)
            {
                return IpaImportResult.Failure(inspectResult.Issues.ToArray());
            }

            Directory.CreateDirectory(request.BaseDirectory);
            var importedPath = Path.Combine(request.BaseDirectory, CreateIpaFileName(inspectResult.Metadata, request.IpaPath));
            File.WriteAllBytes(importedPath, ipaBytes);

            return IpaImportResult.FromInspectedIpa(
                inspectResult.Metadata,
                importedPath,
                inspectResult.Issues);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException)
        {
            return IpaImportResult.Failure(new ValidationIssue(
                IpaInspectionErrorCodes.ImportFailed,
                ValidationSeverity.Error,
                "导入失败",
                "检查文件权限"));
        }
    }

    private static List<ValidationIssue> ValidateRequest(IpaImportRequest request)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(request.IpaPath))
        {
            issues.Add(new ValidationIssue(
                IpaInspectionErrorCodes.ImportFileMissing,
                ValidationSeverity.Error,
                "文件必填",
                "选择 IPA"));
        }
        else if (!File.Exists(request.IpaPath))
        {
            issues.Add(new ValidationIssue(
                IpaInspectionErrorCodes.ImportFileNotFound,
                ValidationSeverity.Error,
                "文件不存在",
                "重新选择"));
        }

        if (string.IsNullOrWhiteSpace(request.BaseDirectory))
        {
            issues.Add(new ValidationIssue(
                IpaInspectionErrorCodes.ImportDirectoryMissing,
                ValidationSeverity.Error,
                "目录必填",
                "选择目录"));
        }

        return issues;
    }

    private static string CreateIpaFileName(IpaMetadata metadata, string sourcePath)
    {
        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        var bundle = string.IsNullOrWhiteSpace(metadata.BundleIdentifier)
            ? sourceName
            : metadata.BundleIdentifier;
        var version = string.IsNullOrWhiteSpace(metadata.BuildVersion)
            ? metadata.ShortVersion
            : $"{metadata.ShortVersion}-{metadata.BuildVersion}";

        return $"{SanitizeFileName(bundle)}-{SanitizeFileName(version)}.ipa";
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
        return string.IsNullOrWhiteSpace(sanitized) ? "app" : sanitized;
    }
}
