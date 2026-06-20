namespace P12Bridge.Core;

public sealed record UploadCommandPreview(
    UploadExecutionMode ExecutionMode,
    UploadCredentialMode CredentialMode,
    string ExecutablePath,
    IReadOnlyList<string> Arguments)
{
    public string CommandLine => string.Join(" ", new[] { ExecutablePath }.Concat(Arguments).Select(QuoteIfNeeded));

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }
}
