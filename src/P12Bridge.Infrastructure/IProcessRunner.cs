namespace P12Bridge.Infrastructure;

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken = default);
}
