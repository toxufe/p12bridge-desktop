namespace P12Bridge.Core;

public static class OperationHistoryPathExtractor
{
    private static readonly char[] TrimCharacters =
    [
        ' ',
        '\t',
        '\r',
        '\n',
        '"',
        '\'',
        '`',
        '，',
        '。',
        '；',
        ';',
        ',',
        ')',
        ']',
        '}'
    ];

    public static IReadOnlyList<string> ExtractLocalPaths(OperationHistoryItem item)
    {
        var paths = new List<string>();
        AddPaths(paths, item.Detail);
        AddPaths(paths, item.Summary);
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddPaths(ICollection<string> paths, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var line in text.Replace('\r', '\n').Split('\n'))
        {
            var candidate = ExtractPathFromLine(line);
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                paths.Add(candidate);
            }
        }
    }

    private static string ExtractPathFromLine(string line)
    {
        var start = FindDrivePathStart(line);
        if (start < 0)
        {
            return string.Empty;
        }

        var candidate = line[start..].Trim(TrimCharacters);
        return candidate.Length >= 3 ? candidate : string.Empty;
    }

    private static int FindDrivePathStart(string value)
    {
        for (var i = 0; i < value.Length - 2; i++)
        {
            if (char.IsAsciiLetter(value[i])
                && value[i + 1] == ':'
                && (value[i + 2] == '\\' || value[i + 2] == '/')
                && (i == 0 || !char.IsLetterOrDigit(value[i - 1])))
            {
                return i;
            }
        }

        return -1;
    }
}
