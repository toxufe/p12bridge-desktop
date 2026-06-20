using P12Bridge.Core;
using Xunit;

namespace P12Bridge.Core.Tests;

public sealed class UploadExecutionGuardTests
{
    [Fact]
    public void ShouldBlockExecutionBlocksUploadWhenReadinessIsBlocked()
    {
        var readiness = Result(UploadReadinessStatus.Blocked);

        var shouldBlock = UploadExecutionGuard.ShouldBlockExecution(
            UploadExecutionMode.Upload,
            readiness);

        Assert.True(shouldBlock);
    }

    [Fact]
    public void ShouldBlockExecutionAllowsUploadWhenReadinessHasWarnings()
    {
        var readiness = Result(UploadReadinessStatus.ReadyWithWarnings);

        var shouldBlock = UploadExecutionGuard.ShouldBlockExecution(
            UploadExecutionMode.Upload,
            readiness);

        Assert.False(shouldBlock);
    }

    [Fact]
    public void ShouldBlockExecutionAllowsVerifyWhenReadinessIsBlocked()
    {
        var readiness = Result(UploadReadinessStatus.Blocked);

        var shouldBlock = UploadExecutionGuard.ShouldBlockExecution(
            UploadExecutionMode.Verify,
            readiness);

        Assert.False(shouldBlock);
    }

    private static UploadReadinessResult Result(UploadReadinessStatus status) =>
        new(status, Array.Empty<UploadReadinessCheck>(), Array.Empty<ValidationIssue>());
}
