namespace P12Bridge.Core;

public static class UploadEvidenceFormatter
{
    private static readonly HashSet<string> DefaultValues = new(StringComparer.Ordinal)
    {
        "未检查",
        "未导入",
        "未选择",
        "未验证",
        "未校验",
        "未查询",
        "待实测",
        "校验链路"
    };

    public static string Format(UploadEvidence evidence)
    {
        var sections = new List<string>();
        var detailSections = new List<string>();
        var overview = new List<string>();

        AddLine(overview, "Windows", evidence.WindowsVersion);
        AddLine(overview, ".NET", evidence.DotNetVersion);
        AddLine(overview, "Transporter", evidence.TransporterPath);
        AddLine(overview, "凭据", evidence.CredentialMode);
        AddLine(overview, "Bundle ID", evidence.BundleIdentifier);
        AddLine(overview, "版本", evidence.Version);
        AddLine(overview, "Build", evidence.Build);
        AddLine(overview, "Team ID", evidence.TeamId);
        AddLine(overview, "IPA", evidence.IpaSummary);
        AddLine(overview, "IPA 路径", evidence.IpaPath);
        AddLine(overview, "描述", evidence.ProfileSummary);
        AddLine(overview, "描述路径", evidence.ProfilePath);
        AddLine(overview, "元数据", evidence.AssetDescriptionSummary);
        AddLine(overview, "元数据路径", evidence.AssetDescriptionPath);
        AddLine(overview, "检查", evidence.ReadinessStatus);
        AddLine(overview, "环境", evidence.EnvironmentStatus);
        AddLine(overview, "链路", evidence.ProofStatus);
        AddLine(overview, "校验", evidence.VerifyStatus);
        AddLine(overview, "构建", evidence.BuildLookupStatus);

        AddSection(detailSections, "检查项", evidence.ReadinessDetail);
        AddSection(detailSections, "远端检查", evidence.RemotePreflightDetail);
        AddSection(detailSections, "构建查询", evidence.BuildLookupDetail);
        AddSection(detailSections, "命令", evidence.CommandPreview);
        AddSection(detailSections, "Transporter", evidence.TransporterDetail);

        if (overview.Count == 0 && detailSections.Count == 0)
        {
            return string.Empty;
        }

        overview.Insert(0, $"时间: {evidence.CapturedAt:yyyy-MM-dd HH:mm:ss}");
        AddSection(sections, "概览", overview);
        sections.AddRange(detailSections);

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", sections);
    }

    private static void AddLine(ICollection<string> lines, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || DefaultValues.Contains(value))
        {
            return;
        }

        lines.Add($"{label}: {value}");
    }

    private static void AddSection(ICollection<string> sections, string title, IEnumerable<string> lines)
    {
        var sectionLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (sectionLines.Length == 0)
        {
            return;
        }

        sections.Add($"{title}{Environment.NewLine}{string.Join(Environment.NewLine, sectionLines)}");
    }

    private static void AddSection(ICollection<string> sections, string title, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        sections.Add($"{title}{Environment.NewLine}{text.Trim()}");
    }
}
