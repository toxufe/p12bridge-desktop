namespace P12Bridge.Core;

public static class LocalAssetSummaryFormatter
{
    public static string Format(LocalAssetItem item, string typeText)
    {
        var lines = new List<string>
        {
            $"类型: {typeText}",
            $"名称: {item.Name}",
            $"路径: {item.Path}",
            $"修改: {item.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm}"
        };

        AddIfPresent(lines, "备注", item.Note);
        AddIfPresent(lines, "状态", FormatCertificateArtifacts(item.CertificateArtifacts));
        AddIfPresent(lines, "信息", item.SafeMetadataSummary);
        AddIfPresent(lines, "备份", item.BackupSummary);
        AddIfPresent(lines, "备份路径", item.BackupPath);

        if (item.ExpiresAt is not null)
        {
            lines.Add($"到期: {item.ExpiresAt.Value.ToLocalTime():yyyy-MM-dd}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddIfPresent(ICollection<string> lines, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {value.Trim()}");
        }
    }

    private static string FormatCertificateArtifacts(CertificateProjectArtifactStatus? artifacts)
    {
        if (artifacts?.HasAny != true)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        AddArtifact(parts, artifacts.HasPrivateKey, "私钥");
        AddArtifact(parts, artifacts.HasCertificateSigningRequest, "CSR");
        AddArtifact(parts, artifacts.HasCertificate, "CER");
        AddArtifact(parts, artifacts.HasP12, "P12");

        return string.Join(" / ", parts);
    }

    private static void AddArtifact(ICollection<string> parts, bool exists, string name)
    {
        if (exists)
        {
            parts.Add(name);
        }
    }
}
