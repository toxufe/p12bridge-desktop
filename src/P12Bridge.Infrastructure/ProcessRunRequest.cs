namespace P12Bridge.Infrastructure;

public sealed record ProcessRunRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan? Timeout = null);
