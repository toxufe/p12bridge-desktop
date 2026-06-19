using System.Security;
using System.Text;
using System.Text.Json;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class CertificateProjectService : ICertificateProjectService
{
    private const string PrivateKeyFileName = "private.key";
    private const string CertificateSigningRequestFileName = "request.csr";
    private const string CertificateFileName = "certificate.cer";
    private const string P12FileName = "export.p12";
    private const string MetadataFileName = "p12bridge.project.json";

    private readonly ILocalCertificateService certificateService;
    private readonly IClock clock;

    public CertificateProjectService(ILocalCertificateService certificateService, IClock clock)
    {
        this.certificateService = certificateService;
        this.clock = clock;
    }

    public CertificateProjectCreateResult Create(CertificateProjectCreateRequest request)
    {
        var inputIssues = ValidateRequest(request);
        if (inputIssues.Count > 0)
        {
            return CertificateProjectCreateResult.Failure(inputIssues.ToArray());
        }

        var privateKeyResult = certificateService.GeneratePrivateKey();
        if (!privateKeyResult.IsSuccess)
        {
            return CertificateProjectCreateResult.Failure(privateKeyResult.Issues.ToArray());
        }

        var csrResult = certificateService.GenerateCertificateSigningRequest(
            new CertificateGenerationRequest(request.Subject, privateKeyResult.PrivateKeyPkcs8));

        if (!csrResult.IsSuccess)
        {
            return CertificateProjectCreateResult.Failure(csrResult.Issues.ToArray());
        }

        try
        {
            var createdAt = clock.UtcNow;
            var projectDirectory = CreateProjectDirectory(request.BaseDirectory, request.ProjectName, createdAt);
            var privateKeyPath = Path.Combine(projectDirectory, PrivateKeyFileName);
            var csrPath = Path.Combine(projectDirectory, CertificateSigningRequestFileName);
            var metadataPath = Path.Combine(projectDirectory, MetadataFileName);

            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(privateKeyPath, ToPem("PRIVATE KEY", privateKeyResult.PrivateKeyPkcs8), Encoding.ASCII);
            File.WriteAllText(csrPath, ToPem("CERTIFICATE REQUEST", csrResult.CertificateSigningRequestDer), Encoding.ASCII);

            var project = new SigningAssetProject(
                request.ProjectName.Trim(),
                request.Purpose,
                projectDirectory,
                createdAt);

            WriteMetadata(metadataPath, project, request.Subject, PrivateKeyFileName, CertificateSigningRequestFileName);

            var artifacts = new CertificateProjectArtifactPaths(
                projectDirectory,
                privateKeyPath,
                csrPath,
                metadataPath);

            return CertificateProjectCreateResult.Success(project, artifacts);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException)
        {
            return CertificateProjectCreateResult.Failure(new ValidationIssue(
                CertificateProofErrorCodes.ProjectCreateFailed,
                ValidationSeverity.Error,
                "证书项目创建失败",
                "检查目录权限"));
        }
    }

    public CertificateProjectP12ExportResult ExportP12(CertificateProjectP12ExportRequest request)
    {
        var inputIssues = ValidateExportRequest(request);
        if (inputIssues.Count > 0)
        {
            return CertificateProjectP12ExportResult.Failure(inputIssues.ToArray());
        }

        try
        {
            var privateKeyPath = Path.Combine(request.ProjectDirectory, PrivateKeyFileName);
            if (!File.Exists(privateKeyPath))
            {
                return CertificateProjectP12ExportResult.Failure(new ValidationIssue(
                    CertificateProofErrorCodes.MissingPrivateKey,
                    ValidationSeverity.Error,
                    "私钥缺失",
                    "重新生成 CSR"));
            }

            var privateKeyDer = ReadPemOrRaw(privateKeyPath, "PRIVATE KEY");
            var certificateDer = ReadPemOrRaw(request.CertificatePath, "CERTIFICATE");
            var p12Result = certificateService.ExportPkcs12(
                new P12ExportRequest(certificateDer, privateKeyDer, request.Password));

            if (!p12Result.IsSuccess)
            {
                return CertificateProjectP12ExportResult.Failure(p12Result.Issues.ToArray());
            }

            var projectCertificatePath = Path.Combine(request.ProjectDirectory, CertificateFileName);
            var p12Path = Path.Combine(request.ProjectDirectory, P12FileName);
            var metadataPath = Path.Combine(request.ProjectDirectory, MetadataFileName);

            File.WriteAllBytes(projectCertificatePath, certificateDer);
            File.WriteAllBytes(p12Path, p12Result.Pkcs12Bytes);
            UpdateExportMetadata(metadataPath, CertificateFileName, P12FileName, clock.UtcNow);

            return CertificateProjectP12ExportResult.Success(projectCertificatePath, p12Path, metadataPath);
        }
        catch (FormatException)
        {
            return CertificateProjectP12ExportResult.Failure(new ValidationIssue(
                CertificateProofErrorCodes.InvalidCertificate,
                ValidationSeverity.Error,
                "证书无效",
                "选择 CER 文件"));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException)
        {
            return CertificateProjectP12ExportResult.Failure(new ValidationIssue(
                CertificateProofErrorCodes.ProjectExportFailed,
                ValidationSeverity.Error,
                "P12 导出失败",
                "检查目录权限"));
        }
    }

    private static List<ValidationIssue> ValidateRequest(CertificateProjectCreateRequest request)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(request.ProjectName))
        {
            issues.Add(new ValidationIssue(
                CertificateProofErrorCodes.EmptyProjectName,
                ValidationSeverity.Error,
                "项目名必填",
                "输入项目名"));
        }

        if (string.IsNullOrWhiteSpace(request.BaseDirectory))
        {
            issues.Add(new ValidationIssue(
                CertificateProofErrorCodes.MissingProjectDirectory,
                ValidationSeverity.Error,
                "目录必填",
                "选择保存目录"));
        }

        issues.AddRange(request.Subject.Validate());

        return issues;
    }

    private static List<ValidationIssue> ValidateExportRequest(CertificateProjectP12ExportRequest request)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(request.ProjectDirectory))
        {
            issues.Add(new ValidationIssue(
                CertificateProofErrorCodes.MissingProjectDirectory,
                ValidationSeverity.Error,
                "项目目录必填",
                "先生成项目"));
        }
        else if (!Directory.Exists(request.ProjectDirectory))
        {
            issues.Add(new ValidationIssue(
                CertificateProofErrorCodes.ProjectNotFound,
                ValidationSeverity.Error,
                "项目不存在",
                "先生成项目"));
        }

        if (string.IsNullOrWhiteSpace(request.CertificatePath) || !File.Exists(request.CertificatePath))
        {
            issues.Add(new ValidationIssue(
                CertificateProofErrorCodes.MissingCertificate,
                ValidationSeverity.Error,
                "CER 必填",
                "选择 CER"));
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            issues.Add(new ValidationIssue(
                CertificateProofErrorCodes.EmptyP12Password,
                ValidationSeverity.Error,
                "密码必填",
                "输入密码"));
        }

        return issues;
    }

    private static string CreateProjectDirectory(string baseDirectory, string projectName, DateTimeOffset createdAt)
    {
        var folderName = $"{SanitizeFileName(projectName)}-{createdAt:yyyyMMddHHmmss}";
        return Path.Combine(baseDirectory, folderName);
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

    private static string ToPem(string label, byte[] derBytes)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"-----BEGIN {label}-----");
        builder.AppendLine(Convert.ToBase64String(derBytes, Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine($"-----END {label}-----");
        return builder.ToString();
    }

    private static byte[] ReadPemOrRaw(string path, string label)
    {
        var bytes = File.ReadAllBytes(path);
        var text = Encoding.ASCII.GetString(bytes);
        var beginMarker = $"-----BEGIN {label}-----";
        var endMarker = $"-----END {label}-----";

        if (!text.Contains(beginMarker, StringComparison.Ordinal))
        {
            return bytes;
        }

        var begin = text.IndexOf(beginMarker, StringComparison.Ordinal) + beginMarker.Length;
        var end = text.IndexOf(endMarker, begin, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new FormatException("PEM end marker missing.");
        }

        var base64 = text[begin..end]
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();

        return Convert.FromBase64String(base64);
    }

    private static void WriteMetadata(
        string metadataPath,
        SigningAssetProject project,
        CertificateSubject subject,
        string privateKeyFileName,
        string csrFileName)
    {
        var metadata = new
        {
            project.Name,
            Purpose = project.Purpose.ToString(),
            CreatedAt = project.CreatedAt,
            Subject = new
            {
                subject.CommonName,
                subject.EmailAddress,
                subject.Organization,
                subject.CountryCode
            },
            Artifacts = new
            {
                PrivateKey = privateKeyFileName,
                CertificateSigningRequest = csrFileName
            }
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(metadataPath, json, Encoding.UTF8);
    }

    private static void UpdateExportMetadata(
        string metadataPath,
        string certificateFileName,
        string p12FileName,
        DateTimeOffset exportedAt)
    {
        Dictionary<string, object?> metadata;
        if (File.Exists(metadataPath))
        {
            metadata = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                File.ReadAllText(metadataPath, Encoding.UTF8)) ?? new Dictionary<string, object?>();
        }
        else
        {
            metadata = new Dictionary<string, object?>();
        }

        metadata["Certificate"] = certificateFileName;
        metadata["P12"] = p12FileName;
        metadata["P12ExportedAt"] = exportedAt;

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(metadataPath, json, Encoding.UTF8);
    }
}
