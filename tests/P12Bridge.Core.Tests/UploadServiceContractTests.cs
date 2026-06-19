using P12Bridge.Core;
using Xunit;

namespace P12Bridge.Core.Tests;

public sealed class UploadServiceContractTests
{
    [Fact]
    public void EnvironmentValidationSuccessHasNoErrors()
    {
        var result = UploadEnvironmentValidationResult.Success();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void EnvironmentValidationFailureReportsError()
    {
        var result = UploadEnvironmentValidationResult.Failure(new ValidationIssue(
            UploadErrorCodes.TransporterNotFound,
            ValidationSeverity.Error,
            "Transporter was not found."));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadErrorCodes.TransporterNotFound);
    }

    [Fact]
    public void UploadResultSuccessRequiresZeroExitCodeAndNoErrors()
    {
        var success = UploadResult.Success(0, "ok", string.Empty);
        var failedExit = UploadResult.Failure(
            1,
            string.Empty,
            "failed",
            new ValidationIssue(UploadErrorCodes.ProcessExitFailed, ValidationSeverity.Error, "Upload failed."));

        Assert.True(success.IsSuccess);
        Assert.False(failedExit.IsSuccess);
    }

    [Fact]
    public void UploadProgressCarriesPhaseAndMessage()
    {
        var progress = new UploadProgress(UploadPhase.RunningTransporter, "Running Transporter.", 50);

        Assert.Equal(UploadPhase.RunningTransporter, progress.Phase);
        Assert.Equal("Running Transporter.", progress.Message);
        Assert.Equal(50, progress.Percent);
    }
}
