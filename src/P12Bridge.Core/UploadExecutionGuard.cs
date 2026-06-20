namespace P12Bridge.Core;

public static class UploadExecutionGuard
{
    public static bool ShouldBlockExecution(
        UploadExecutionMode executionMode,
        UploadReadinessResult readiness) =>
        executionMode == UploadExecutionMode.Upload
        && readiness.Status == UploadReadinessStatus.Blocked;
}
