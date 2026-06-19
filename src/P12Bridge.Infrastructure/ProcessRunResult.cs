namespace P12Bridge.Infrastructure;

public sealed record ProcessRunResult(
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    bool Cancelled = false);
