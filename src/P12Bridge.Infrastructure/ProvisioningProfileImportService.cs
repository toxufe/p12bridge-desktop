using System.Security;
using System.Text;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class ProvisioningProfileImportService : IProvisioningProfileImportService
{
    private readonly IProvisioningProfileParser parser;
    private readonly IClock clock;

    public ProvisioningProfileImportService(IProvisioningProfileParser parser, IClock clock)
    {
        this.parser = parser;
        this.clock = clock;
    }

    public ProvisioningProfileImportResult Import(ProvisioningProfileImportRequest request)
    {
        var inputIssues = ValidateRequest(request);
        if (inputIssues.Count > 0)
        {
            return ProvisioningProfileImportResult.Failure(inputIssues.ToArray());
        }

        try
        {
            var profileBytes = File.ReadAllBytes(request.ProfilePath);
            var parseResult = parser.Parse(profileBytes, clock.UtcNow);
            if (parseResult.Profile is null)
            {
                return ProvisioningProfileImportResult.Failure(parseResult.Issues.ToArray());
            }

            Directory.CreateDirectory(request.BaseDirectory);
            var importedPath = Path.Combine(request.BaseDirectory, CreateProfileFileName(parseResult.Profile));
            File.WriteAllBytes(importedPath, profileBytes);

            return ProvisioningProfileImportResult.FromParsedProfile(
                parseResult.Profile,
                importedPath,
                parseResult.Issues);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException)
        {
            return ProvisioningProfileImportResult.Failure(new ValidationIssue(
                ProvisioningProfileErrorCodes.ImportFailed,
                ValidationSeverity.Error,
                "导入失败",
                "检查文件权限"));
        }
    }

    private static List<ValidationIssue> ValidateRequest(ProvisioningProfileImportRequest request)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(request.ProfilePath))
        {
            issues.Add(new ValidationIssue(
                ProvisioningProfileErrorCodes.ImportFileMissing,
                ValidationSeverity.Error,
                "文件必填",
                "选择文件"));
        }
        else if (!File.Exists(request.ProfilePath))
        {
            issues.Add(new ValidationIssue(
                ProvisioningProfileErrorCodes.ImportFileNotFound,
                ValidationSeverity.Error,
                "文件不存在",
                "重新选择"));
        }

        if (string.IsNullOrWhiteSpace(request.BaseDirectory))
        {
            issues.Add(new ValidationIssue(
                ProvisioningProfileErrorCodes.ImportDirectoryMissing,
                ValidationSeverity.Error,
                "目录必填",
                "选择目录"));
        }

        return issues;
    }

    private static string CreateProfileFileName(ProvisioningProfile profile)
    {
        var name = SanitizeFileName(profile.Name);
        var uuid = SanitizeFileName(profile.Uuid);
        return $"{name}-{uuid}.mobileprovision";
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
        return string.IsNullOrWhiteSpace(sanitized) ? "profile" : sanitized;
    }
}
