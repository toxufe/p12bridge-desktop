using System.Security;
using System.Text;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class TextExportService : ITextExportService
{
    public TextExportResult Export(TextExportRequest request)
    {
        var issues = ValidateRequest(request);
        if (issues.Count > 0)
        {
            return TextExportResult.Failure(issues.ToArray());
        }

        try
        {
            File.WriteAllText(request.OutputPath, request.Content, Encoding.UTF8);
            return TextExportResult.Success(request.OutputPath);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or SecurityException)
        {
            return TextExportResult.Failure(new ValidationIssue(
                TextExportErrorCodes.WriteFailed,
                ValidationSeverity.Error,
                "文本保存失败",
                "检查目录权限"));
        }
    }

    private static List<ValidationIssue> ValidateRequest(TextExportRequest request)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            issues.Add(new ValidationIssue(
                TextExportErrorCodes.OutputPathMissing,
                ValidationSeverity.Error,
                "保存路径必填",
                "选择文件"));
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            issues.Add(new ValidationIssue(
                TextExportErrorCodes.ContentEmpty,
                ValidationSeverity.Error,
                "文本为空",
                "重新生成"));
        }

        return issues;
    }
}
