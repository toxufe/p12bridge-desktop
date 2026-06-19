using System.Diagnostics;

namespace P12Bridge.Infrastructure;

public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in request.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Process failed to start.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        using var timeoutTokenSource = request.Timeout.HasValue
            ? new CancellationTokenSource(request.Timeout.Value)
            : new CancellationTokenSource();
        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutTokenSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedTokenSource.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcess(process);
            return new ProcessRunResult(
                null,
                await standardOutputTask,
                await standardErrorTask,
                Cancelled: true);
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            return new ProcessRunResult(
                null,
                await standardOutputTask,
                await standardErrorTask,
                TimedOut: true);
        }

        return new ProcessRunResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static void KillProcess(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        process.Kill(entireProcessTree: true);
    }
}
