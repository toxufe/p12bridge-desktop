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

        if (request.ExecutionMode == UploadExecutionMode.Upload)
        {
            AddAssetDescriptionIssues(issues, request);
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

        progress?.Report(new UploadProgress(UploadPhase.BuildingCommand, "Building Transporter command."));
        var command = BuildCommand(request);

        try
        {
            progress?.Report(new UploadProgress(UploadPhase.RunningTransporter, "Running Transporter."));
            var processResult = await processRunner.RunAsync(command, cancellationToken);
            var standardOutput = Redact(processResult.StandardOutput, request);
            var standardError = Redact(processResult.StandardError, request);
            var actionName = FormatActionName(request.ExecutionMode);

            if (processResult.Cancelled)
            {
                progress?.Report(new UploadProgress(UploadPhase.Failed, "Transporter was cancelled."));
                return UploadResult.Failure(
                    processResult.ExitCode,
                    standardOutput,
                    standardError,
                    new ValidationIssue(
                        UploadErrorCodes.ProcessCancelled,
                        ValidationSeverity.Error,
                        $"Transporter {actionName} was cancelled.",
                        $"Run the {actionName} again when ready."));
            }

            if (processResult.TimedOut)
            {
                progress?.Report(new UploadProgress(UploadPhase.Failed, "Transporter timed out."));
                return UploadResult.Failure(
                    processResult.ExitCode,
                    standardOutput,
                    standardError,
                    new ValidationIssue(
                        UploadErrorCodes.ProcessTimedOut,
                        ValidationSeverity.Error,
                        $"Transporter {actionName} timed out.",
                        "Check network connectivity and retry."));
            }

            if (processResult.ExitCode == 0)
            {
                progress?.Report(new UploadProgress(UploadPhase.Completed, "Transporter completed."));
                return UploadResult.Success(0, standardOutput, standardError);
            }

            progress?.Report(new UploadProgress(UploadPhase.Failed, "Transporter failed."));
            return UploadResult.Failure(
                processResult.ExitCode,
                standardOutput,
                standardError,
                ClassifyProcessExitFailure(actionName, standardOutput, standardError));
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

    public static ProcessRunRequest BuildCommand(UploadRequest request) =>
        request.ExecutionMode == UploadExecutionMode.Upload
            ? BuildUploadCommand(request)
            : BuildVerifyCommand(request);

    public static ProcessRunRequest BuildVerifyCommand(UploadRequest request)
    {
        var arguments = new List<string>
        {
            "-m",
            "verify",
            "-assetFile",
            request.PackagePath
        };

        AddCredentialArguments(arguments, request);

        return new ProcessRunRequest(request.TransporterExecutablePath, arguments, request.Timeout);
    }

    public static ProcessRunRequest BuildUploadCommand(UploadRequest request)
    {
        var arguments = new List<string>
        {
            "-m",
            "upload",
            "-assetFile",
            request.PackagePath,
            "-assetDescription",
            request.AssetDescriptionPath ?? string.Empty
        };

        AddCredentialArguments(arguments, request);

        return new ProcessRunRequest(request.TransporterExecutablePath, arguments, request.Timeout);
    }

    private static void AddCredentialArguments(List<string> arguments, UploadRequest request)
    {
        if (request.CredentialMode == UploadCredentialMode.ApiKey)
        {
            arguments.Add("-apiKey");
            arguments.Add(request.ApiKeyId ?? string.Empty);
            arguments.Add("-apiIssuer");
            arguments.Add(request.IssuerId ?? string.Empty);
            return;
        }

        if (request.CredentialMode == UploadCredentialMode.Jwt)
        {
            arguments.Add("-jwt");
            arguments.Add(request.Jwt ?? string.Empty);
            return;
        }

        arguments.Add("-u");
        arguments.Add(request.AppleAccount ?? string.Empty);
        arguments.Add("-p");
        arguments.Add(request.AppSpecificPassword ?? string.Empty);
    }

    private static void AddAssetDescriptionIssues(List<ValidationIssue> issues, UploadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AssetDescriptionPath))
        {
            issues.Add(new ValidationIssue(
                UploadErrorCodes.AssetDescriptionPathMissing,
                ValidationSeverity.Error,
                "AppStoreInfo.plist path is required for Transporter upload on Windows.",
                "Choose the AppStoreInfo.plist exported with the signed IPA."));
        }
        else if (!File.Exists(request.AssetDescriptionPath))
        {
            issues.Add(new ValidationIssue(
                UploadErrorCodes.AssetDescriptionNotFound,
                ValidationSeverity.Error,
                "AppStoreInfo.plist was not found.",
                "Choose an existing AppStoreInfo.plist file."));
        }
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

        if (request.CredentialMode == UploadCredentialMode.Jwt)
        {
            if (!string.IsNullOrWhiteSpace(request.Jwt))
            {
                return;
            }

            issues.Add(new ValidationIssue(
                UploadErrorCodes.JwtMissing,
                ValidationSeverity.Error,
                "App Store Connect JWT is required.",
                "Generate or configure a JWT before running Transporter verification."));
            return;
        }

        if (string.IsNullOrWhiteSpace(request.AppleAccount))
        {
            issues.Add(new ValidationIssue(
                UploadErrorCodes.AppleAccountMissing,
                ValidationSeverity.Error,
                "Apple account is required.",
                "Enter the Apple account used for upload."));
        }

        if (string.IsNullOrWhiteSpace(request.AppSpecificPassword))
        {
            issues.Add(new ValidationIssue(
                UploadErrorCodes.AppSpecificPasswordMissing,
                ValidationSeverity.Error,
                "App-specific password is required.",
                "Enter an app-specific password for upload."));
        }
    }

    private static string Redact(string value, UploadRequest request)
    {
        var redacted = value;

        if (!string.IsNullOrWhiteSpace(request.Jwt))
        {
            redacted = redacted.Replace(request.Jwt, "[REDACTED-JWT]", StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(request.AppSpecificPassword))
        {
            redacted = redacted.Replace(request.AppSpecificPassword, "[REDACTED-PASSWORD]", StringComparison.Ordinal);
        }

        redacted = JwtLikePattern.Replace(redacted, "[REDACTED-JWT]");
        redacted = Regex.Replace(
            redacted,
            @"Authorization:\s*Bearer\s+\S+",
            "Authorization: Bearer [REDACTED]",
            RegexOptions.IgnoreCase);

        return redacted;
    }

    private static ValidationIssue ClassifyProcessExitFailure(string actionName, string standardOutput, string standardError)
    {
        var output = $"{standardOutput}{Environment.NewLine}{standardError}".ToLowerInvariant();

        if (ContainsAny(output, "unauthorized", "authentication", "authenticate", "invalid credentials", "invalid token", "api key", "issuer", "jwt", "password"))
        {
            return new ValidationIssue(
                UploadErrorCodes.TransporterAuthenticationFailed,
                ValidationSeverity.Error,
                $"Transporter {actionName} authentication failed.",
                "Check the upload credential and App Store Connect access.");
        }

        if (ContainsAny(output, "appstoreinfo", "asset description", "assetdescription", "metadata.xml", "metadata"))
        {
            return new ValidationIssue(
                UploadErrorCodes.TransporterAssetMetadataFailed,
                ValidationSeverity.Error,
                $"Transporter {actionName} metadata failed.",
                "Choose the AppStoreInfo.plist exported with the signed IPA.");
        }

        if (ContainsAny(output, "network", "connection", "timed out", "timeout", "could not connect", "unable to connect", "ssl", "proxy"))
        {
            return new ValidationIssue(
                UploadErrorCodes.TransporterNetworkFailed,
                ValidationSeverity.Error,
                $"Transporter {actionName} network failed.",
                "Check network connectivity and retry.");
        }

        if (ContainsAny(output, "invalid binary", "validation failed", "asset validation", "bundle", "version", "build number", "signature", "provisioning profile"))
        {
            return new ValidationIssue(
                UploadErrorCodes.TransporterValidationFailed,
                ValidationSeverity.Error,
                $"Transporter {actionName} validation failed.",
                "Fix the IPA validation issue reported by Transporter.");
        }

        return new ValidationIssue(
            UploadErrorCodes.ProcessExitFailed,
            ValidationSeverity.Error,
            $"Transporter {actionName} failed.",
            "Review the Transporter output and fix the reported issue.");
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.Ordinal));

    private static string FormatActionName(UploadExecutionMode executionMode) =>
        executionMode == UploadExecutionMode.Upload ? "upload" : "verification";
}
