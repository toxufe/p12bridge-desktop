namespace P12Bridge.Core;

public sealed record IpaImportRequest(
    string IpaPath,
    string BaseDirectory);
