namespace P12Bridge.Core;

public sealed record TextExportRequest(
    string OutputPath,
    string Content);
