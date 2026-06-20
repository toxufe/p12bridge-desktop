using P12Bridge.Core;
using Xunit;

namespace P12Bridge.Core.Tests;

public sealed class LocalAssetSummaryFormatterTests
{
    [Fact]
    public void FormatReturnsSafeAssetSummaryWithBackupPath()
    {
        var asset = new LocalAssetItem(
            LocalAssetType.CertificateProject,
            "Demo Project",
            Path.Combine("C:", "Assets", "Demo Project"),
            new DateTimeOffset(2026, 6, 20, 1, 2, 3, TimeSpan.Zero),
            Note: "Release",
            CertificateArtifacts: new CertificateProjectArtifactStatus(
                HasPrivateKey: true,
                HasCertificateSigningRequest: true,
                HasCertificate: false,
                HasP12: true),
            ExpiresAt: new DateTimeOffset(2027, 6, 20, 0, 0, 0, TimeSpan.Zero),
            SafeMetadataSummary: "发布证书",
            BackupSummary: "备份 2026-06-20",
            BackupPath: Path.Combine("D:", "Backups", "Demo-20260620010203.zip"));

        var summary = LocalAssetSummaryFormatter.Format(asset, "证书");

        Assert.Contains("类型: 证书", summary, StringComparison.Ordinal);
        Assert.Contains("名称: Demo Project", summary, StringComparison.Ordinal);
        Assert.Contains($"路径: {asset.Path}", summary, StringComparison.Ordinal);
        Assert.Contains("状态: 私钥 / CSR / P12", summary, StringComparison.Ordinal);
        Assert.Contains("信息: 发布证书", summary, StringComparison.Ordinal);
        Assert.Contains("备份: 备份 2026-06-20", summary, StringComparison.Ordinal);
        Assert.Contains($"备份路径: {asset.BackupPath}", summary, StringComparison.Ordinal);
        Assert.Contains("到期:", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatOmitsEmptyOptionalFields()
    {
        var asset = new LocalAssetItem(
            LocalAssetType.Ipa,
            "Demo.ipa",
            Path.Combine("C:", "Assets", "Demo.ipa"),
            new DateTimeOffset(2026, 6, 20, 1, 2, 3, TimeSpan.Zero));

        var summary = LocalAssetSummaryFormatter.Format(asset, "IPA");

        Assert.Contains("类型: IPA", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("备注:", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("状态:", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("备份:", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("备份路径:", summary, StringComparison.Ordinal);
    }
}
