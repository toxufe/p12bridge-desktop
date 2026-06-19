using System.Security;
using System.Text;
using System.Text.Json;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class CertificateProjectService : ICertificateProjectService
{
    private const string PrivateKeyFileName = "private.key";
    private const string CertificateSigningRequestFileName = "request.csr";
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
}
