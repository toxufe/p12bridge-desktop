using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class TransporterUploadServiceTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"p12bridge-upload-tests-{Guid.NewGuid():N}");
    private readonly string transporterPath;
    private readonly string packagePath;
    private readonly string assetDescriptionPath;

    public TransporterUploadServiceTests()
    {
        Directory.CreateDirectory(tempDirectory);
        transporterPath = Path.Combine(tempDirectory, "iTMSTransporter.cmd");
        packagePath = Path.Combine(tempDirectory, "demo.ipa");
        assetDescriptionPath = Path.Combine(tempDirectory, "AppStoreInfo.plist");
        File.WriteAllText(transporterPath, "transporter");
        File.WriteAllText(packagePath, "ipa");
        File.WriteAllText(assetDescriptionPath, "plist");
    }

    [Fact]
    public void ValidateEnvironmentRejectsMissingTransporterPath()
    {
        var service = new TransporterUploadService(new FakeProcessRunner());

        var result = service.ValidateEnvironment(ValidRequest() with { TransporterExecutablePath = " " });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.TransporterPathMissing);
    }

    [Fact]
    public void ValidateEnvironmentRejectsMissingTransporterFile()
    {
        var service = new TransporterUploadService(new FakeProcessRunner());

        var result = service.ValidateEnvironment(ValidRequest() with
        {
            TransporterExecutablePath = Path.Combine(tempDirectory, "missing.cmd")
        });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.TransporterNotFound);
    }

    [Fact]
    public void ValidateEnvironmentRejectsMissingPackagePath()
    {
        var service = new TransporterUploadService(new FakeProcessRunner());

        var result = service.ValidateEnvironment(ValidRequest() with { PackagePath = string.Empty });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.PackagePathMissing);
    }

    [Fact]
    public void ValidateEnvironmentRejectsMissingPackageFile()
    {
        var service = new TransporterUploadService(new FakeProcessRunner());

        var result = service.ValidateEnvironment(ValidRequest() with
        {
            PackagePath = Path.Combine(tempDirectory, "missing.ipa")
        });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.PackageNotFound);
    }

    [Fact]
    public void ValidateEnvironmentRejectsMissingApiKeyCredential()
    {
        var service = new TransporterUploadService(new FakeProcessRunner());

        var result = service.ValidateEnvironment(ValidRequest() with { ApiKeyId = null });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.ApiKeyCredentialMissing);
    }

    [Fact]
    public void ValidateEnvironmentRejectsMissingJwt()
    {
        var service = new TransporterUploadService(new FakeProcessRunner());

        var result = service.ValidateEnvironment(ValidRequest() with
        {
            CredentialMode = UploadCredentialMode.Jwt,
            ApiKeyId = null,
            IssuerId = null,
            Jwt = " "
        });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.JwtMissing);
    }

    [Fact]
    public void ValidateEnvironmentAllowsVerifyWithoutAssetDescription()
    {
        var service = new TransporterUploadService(new FakeProcessRunner());

        var result = service.ValidateEnvironment(ValidRequest() with
        {
            AssetDescriptionPath = null,
            ExecutionMode = UploadExecutionMode.Verify
        });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateEnvironmentRejectsUploadMissingAssetDescriptionPath()
    {
        var runner = new FakeProcessRunner();
        var service = new TransporterUploadService(runner);

        var result = service.ValidateEnvironment(ValidUploadRequest() with { AssetDescriptionPath = " " });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.AssetDescriptionPathMissing);
        Assert.Null(runner.LastRequest);
    }

    [Fact]
    public void ValidateEnvironmentRejectsUploadMissingAssetDescriptionFile()
    {
        var service = new TransporterUploadService(new FakeProcessRunner());

        var result = service.ValidateEnvironment(ValidUploadRequest() with
        {
            AssetDescriptionPath = Path.Combine(tempDirectory, "missing.plist")
        });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.AssetDescriptionNotFound);
    }

    [Fact]
    public void BuildVerifyCommandUsesApiKeyArguments()
    {
        var request = ValidRequest();

        var command = TransporterUploadService.BuildVerifyCommand(request);

        Assert.Equal(transporterPath, command.FileName);
        Assert.Equal(
            ["-m", "verify", "-assetFile", packagePath, "-apiKey", "ABC123DEFG", "-apiIssuer", "issuer-id"],
            command.Arguments);
    }

    [Fact]
    public void BuildVerifyCommandUsesJwtArguments()
    {
        var request = ValidRequest() with
        {
            CredentialMode = UploadCredentialMode.Jwt,
            Jwt = "header.payload.signature",
            ApiKeyId = null,
            IssuerId = null
        };

        var command = TransporterUploadService.BuildVerifyCommand(request);

        Assert.Equal(
            ["-m", "verify", "-assetFile", packagePath, "-jwt", "header.payload.signature"],
            command.Arguments);
    }

    [Fact]
    public void BuildUploadCommandUsesAssetDescriptionAndApiKeyArguments()
    {
        var request = ValidUploadRequest();

        var command = TransporterUploadService.BuildUploadCommand(request);

        Assert.Equal(transporterPath, command.FileName);
        Assert.Equal(
            [
                "-m",
                "upload",
                "-assetFile",
                packagePath,
                "-assetDescription",
                assetDescriptionPath,
                "-apiKey",
                "ABC123DEFG",
                "-apiIssuer",
                "issuer-id"
            ],
            command.Arguments);
    }

    [Fact]
    public void BuildUploadCommandUsesJwtArguments()
    {
        var request = ValidUploadRequest() with
        {
            CredentialMode = UploadCredentialMode.Jwt,
            Jwt = "header.payload.signature",
            ApiKeyId = null,
            IssuerId = null
        };

        var command = TransporterUploadService.BuildUploadCommand(request);

        Assert.Equal(
            [
                "-m",
                "upload",
                "-assetFile",
                packagePath,
                "-assetDescription",
                assetDescriptionPath,
                "-jwt",
                "header.payload.signature"
            ],
            command.Arguments);
    }

    [Fact]
    public async Task UploadAsyncMapsFakeProcessSuccess()
    {
        var progressEvents = new List<UploadProgress>();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessRunResult(0, "verification ok", string.Empty)
        };
        var service = new TransporterUploadService(runner);

        var result = await service.UploadAsync(ValidRequest(), new Progress<UploadProgress>(progressEvents.Add));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("verification ok", result.StandardOutput);
        Assert.Equal(UploadPhase.Completed, progressEvents.Last().Phase);
        Assert.NotNull(runner.LastRequest);
    }

    [Fact]
    public async Task UploadAsyncUsesUploadCommandWhenRequested()
    {
        var runner = new FakeProcessRunner
        {
            Result = new ProcessRunResult(0, "upload ok", string.Empty)
        };
        var service = new TransporterUploadService(runner);

        var result = await service.UploadAsync(ValidUploadRequest());

        Assert.True(result.IsSuccess);
        Assert.NotNull(runner.LastRequest);
        Assert.Equal("upload", runner.LastRequest.Arguments[1]);
        Assert.Contains("-assetDescription", runner.LastRequest.Arguments);
        Assert.Contains(assetDescriptionPath, runner.LastRequest.Arguments);
    }

    [Fact]
    public async Task UploadAsyncDoesNotStartProcessWhenUploadAssetDescriptionIsMissing()
    {
        var runner = new FakeProcessRunner();
        var service = new TransporterUploadService(runner);

        var result = await service.UploadAsync(ValidUploadRequest() with { AssetDescriptionPath = null });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.AssetDescriptionPathMissing);
        Assert.Null(runner.LastRequest);
    }

    [Fact]
    public async Task UploadAsyncMapsNonZeroExitCodeAndRedactsJwtLikeLogs()
    {
        var token = "eyJheader.payload.signature";
        var runner = new FakeProcessRunner
        {
            Result = new ProcessRunResult(1, $"stdout {token}", $"Authorization: Bearer {token}")
        };
        var service = new TransporterUploadService(runner);
        var request = ValidRequest() with
        {
            CredentialMode = UploadCredentialMode.Jwt,
            Jwt = token,
            ApiKeyId = null,
            IssuerId = null
        };

        var result = await service.UploadAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.ProcessExitFailed);
        Assert.DoesNotContain(token, result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(token, result.StandardError, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-JWT]", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer [REDACTED]", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsyncMapsTimeout()
    {
        var runner = new FakeProcessRunner
        {
            Result = new ProcessRunResult(null, string.Empty, "timeout", TimedOut: true)
        };
        var service = new TransporterUploadService(runner);

        var result = await service.UploadAsync(ValidRequest());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.ProcessTimedOut);
    }

    [Fact]
    public async Task UploadAsyncMapsCancellation()
    {
        var runner = new FakeProcessRunner
        {
            Result = new ProcessRunResult(null, string.Empty, "cancelled", Cancelled: true)
        };
        var service = new TransporterUploadService(runner);

        var result = await service.UploadAsync(ValidRequest());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.ProcessCancelled);
    }

    [Fact]
    public async Task UploadAsyncMapsProcessStartFailure()
    {
        var runner = new FakeProcessRunner
        {
            Exception = new InvalidOperationException("cannot start")
        };
        var service = new TransporterUploadService(runner);

        var result = await service.UploadAsync(ValidRequest());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.ProcessStartFailed);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private UploadRequest ValidRequest() =>
        new(
            transporterPath,
            packagePath,
            UploadCredentialMode.ApiKey,
            ApiKeyId: "ABC123DEFG",
            IssuerId: "issuer-id",
            Timeout: TimeSpan.FromSeconds(30));

    private UploadRequest ValidUploadRequest() =>
        ValidRequest() with
        {
            ExecutionMode = UploadExecutionMode.Upload,
            AssetDescriptionPath = assetDescriptionPath
        };

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public ProcessRunRequest? LastRequest { get; private set; }

        public ProcessRunResult Result { get; set; } = new(0, string.Empty, string.Empty);

        public Exception? Exception { get; set; }

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Result);
        }
    }
}
