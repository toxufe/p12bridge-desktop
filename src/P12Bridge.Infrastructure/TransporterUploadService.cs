using System.Text.RegularExpressions;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class TransporterUploadService : IUploadService
{
    private static readonly Regex JwtLikePattern = new(
        @"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+",
        RegexOptions.Compiled);

    private readonly IProcessRunner processRunner;

    public TransporterUploadService(IProcessRunner? processRunner = null)
    {
        this.processRunner = processRunner ?? new SystemProcessRunner();
    }

    public UploadEnvironmentValidationResult ValidateEnvironment(UploadRequest request)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(request.TransporterExecutablePath))
        {
            issues.Add(new ValidationIssue(
                UploadErrorCodes.TransporterPathMissing,
                ValidationSeverity.Error,
                "Transporter executable path is required.",
                "Configure the iTMSTransporter executable path before uploading."));
        }
        else if (!File.Exists(request.TransporterExecutablePath))
        {
            issues.Add(new ValidationIssue(
                UploadErrorCodes.TransporterNotFound,
                ValidationSeverity.Error,
                "Transporter executable was not found.",
                "Install Apple Transporter or select the correct iTMSTransporter executable."));
        }

        if (string.IsNullOrWhiteSpace(request.PackagePath))
        {
            issues.Add(new ValidationIssue(
                UploadErrorCodes.PackagePathMissing,
                ValidationSeverity.Error,
                "IPA package path is required.",
                "Choose an already signed IPA before running upload verification."));
        }
        else if (!File.Exists(request.PackagePath))
        {
            issues.Add(new ValidationIssue(
                UploadErrorCodes.PackageNotFound,
                ValidationSeverity.Error,
                "IPA package was not found.",
                "Choose an existing IPA file."));
        }

        AddCredentialIssues(issues, request);

        return issues.Count == 0
            ? UploadEnvironmentValidationResult.Success()
            : UploadEnvironmentValidationResult.Failure(issues.ToArray());
    }

    public async Task<UploadResult> UploadAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new UploadProgress(UploadPhase.ValidatingEnvironment, "Validating Transporter environment."));
        var validation = ValidateEnvironment(request);
        if (!validation.IsSuccess)
        {
            progress?.Report(new UploadProgress(UploadPhase.Failed, "Transporter environment validation failed."));
            return UploadResult.Failure(null, string.Empty, string.Empty, validation.Issues.ToArray());
        }

        progress?.Report(new UploadProgress(UploadPhase.BuildingCommand, "Building Transporter verification command."));
        var command = BuildVerifyCommand(request);

        try
        {
            progress?.Report(new UploadProgress(UploadPhase.RunningTransporter, "Running Transporter verification."));
            var processResult = await processRunner.RunAsync(command, cancellationToken);
            var standardOutput = Redact(processResult.StandardOutput, request);
            var standardError = Redact(processResult.StandardError, request);

            if (processResult.Cancelled)
            {
                progress?.Report(new UploadProgress(UploadPhase.Failed, "Transporter verification was cancelled."));
                return UploadResult.Failure(
                    processResult.ExitCode,
                    standardOutput,
                    standardError,
                    new ValidationIssue(
                        UploadErrorCodes.ProcessCancelled,
                        ValidationSeverity.Error,
                        "Transporter verification was cancelled.",
                        "Run the verification again when ready."));
            }

            if (processResult.TimedOut)
            {
                progress?.Report(new UploadProgress(UploadPhase.Failed, "Transporter verification timed out."));
                return UploadResult.Failure(
                    processResult.ExitCode,
                    standardOutput,
                    standardError,
                    new ValidationIssue(
                        UploadErrorCodes.ProcessTimedOut,
                        ValidationSeverity.Error,
                        "Transporter verification timed out.",
                        "Check network connectivity and retry."));
            }

            if (processResult.ExitCode == 0)
            {
                progress?.Report(new UploadProgress(UploadPhase.Completed, "Transporter verification completed."));
                return UploadResult.Success(0, standardOutput, standardError);
            }

            progress?.Report(new UploadProgress(UploadPhase.Failed, "Transporter verification failed."));
            return UploadResult.Failure(
                processResult.ExitCode,
                standardOutput,
                standardError,
                new ValidationIssue(
                    UploadErrorCodes.ProcessExitFailed,
                    ValidationSeverity.Error,
                    "Transporter verification failed.",
                    "Review the Transporter output and fix the reported issue."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            progress?.Report(new UploadProgress(UploadPhase.Failed, "Transporter process failed to start."));
            return UploadResult.Failure(
                null,
                string.Empty,
                Redact(exception.Message, request),
                new ValidationIssue(
                    UploadErrorCodes.ProcessStartFailed,
                    ValidationSeverity.Error,
                    "Transporter process could not be started.",
                    "Verify the Transporter executable path and permissions."));
        }
    }

    public static ProcessRunRequest BuildVerifyCommand(UploadRequest request)
    {
        var arguments = new List<string>
        {
            "-m",
            "verify",
            "-assetFile",
            request.PackagePath
        };

        if (request.CredentialMode == UploadCredentialMode.ApiKey)
        {
            arguments.Add("-apiKey");
            arguments.Add(request.ApiKeyId ?? string.Empty);
            arguments.Add("-apiIssuer");
            arguments.Add(request.IssuerId ?? string.Empty);
        }
        else
        {
            arguments.Add("-jwt");
            arguments.Add(request.Jwt ?? string.Empty);
        }

        return new ProcessRunRequest(request.TransporterExecutablePath, arguments, request.Timeout);
    }

    private static void AddCredentialIssues(List<ValidationIssue> issues, UploadRequest request)
    {
        if (request.CredentialMode == UploadCredentialMode.ApiKey)
        {
            if (!string.IsNullOrWhiteSpace(request.ApiKeyId) && !string.IsNullOrWhiteSpace(request.IssuerId))
            {
                return;
            }

            issues.Add(new ValidationIssue(
                UploadErrorCodes.ApiKeyCredentialMissing,
                ValidationSeverity.Error,
                "App Store Connect API Key ID and Issuer ID are required.",
                "Configure API Key credentials before running Transporter verification."));
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.Jwt))
        {
            return;
        }

        issues.Add(new ValidationIssue(
            UploadErrorCodes.JwtMissing,
            ValidationSeverity.Error,
            "App Store Connect JWT is required.",
            "Generate or configure a JWT before running Transporter verification."));
    }

    private static string Redact(string value, UploadRequest request)
    {
        var redacted = value;

        if (!string.IsNullOrWhiteSpace(request.Jwt))
        {
            redacted = redacted.Replace(request.Jwt, "[REDACTED-JWT]", StringComparison.Ordinal);
        }

        redacted = JwtLikePattern.Replace(redacted, "[REDACTED-JWT]");
        redacted = Regex.Replace(
            redacted,
            @"Authorization:\s*Bearer\s+\S+",
            "Authorization: Bearer [REDACTED]",
            RegexOptions.IgnoreCase);

        return redacted;
    }
}
