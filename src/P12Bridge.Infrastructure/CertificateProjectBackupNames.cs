using System.Text;

namespace P12Bridge.Infrastructure;

internal static class CertificateProjectBackupNames
{
    public static string CreateProjectPrefix(string projectDirectory)
    {
        var projectName = Path.GetFileName(Path.GetFullPath(projectDirectory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));

        return SanitizeFileName(projectName);
    }

    public static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Trim())
        {
            builder.Append(char.IsWhiteSpace(character) || invalidChars.Contains(character)
                ? '-'
                : character);
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "certificate-project" : sanitized;
    }
}
