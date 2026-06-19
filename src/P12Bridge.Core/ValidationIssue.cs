namespace P12Bridge.Core;

public sealed record ValidationIssue(
    string Code,
    ValidationSeverity Severity,
    string Message,
    string? SuggestedAction = null);
