using P12Bridge.Core;
using Xunit;

namespace P12Bridge.Core.Tests;

public sealed class UploadEvidenceFormatterTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 6, 21, 10, 15, 30, TimeSpan.Zero);

    [Fact]
    public void Format_WhenEvidenceIsEmpty_ReturnsEmpty()
    {
        var text = UploadEvidenceFormatter.Format(new UploadEvidence(CapturedAt));

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void Format_IncludesTimestampAndOverviewFields()
    {
        var text = UploadEvidenceFormatter.Format(new UploadEvidence(
            CapturedAt,
            BuildIdentity: "1.0.0+abc123",
            WindowsVersion: "Microsoft Windows 10.0.26100",
            DotNetVersion: ".NET 8.0.22",
            TransporterPath: @"C:\Transporter\iTMSTransporter.cmd",
            CredentialMode: "API Key",
            BundleIdentifier: "com.example.app",
            Version: "1.2.3",
            Build: "45",
            TeamId: "TEAM123456",
            IpaSummary: "com.example.app / 1.2.3 (45) / demo.ipa",
            IpaPath: @"C:\Safe\demo.ipa",
            ProfileSummary: "App Store / 有效 / com.example.app / demo.mobileprovision",
            ProfilePath: @"C:\Safe\demo.mobileprovision",
            AssetDescriptionSummary: "AppStoreInfo.plist",
            AssetDescriptionPath: @"C:\Safe\AppStoreInfo.plist",
            ReadinessStatus: "可上传",
            EnvironmentStatus: "已验证",
            ProofStatus: "待核验",
            VerifyStatus: "校验完成",
            BuildLookupStatus: "已找到"));

        Assert.Contains("概览", text, StringComparison.Ordinal);
        Assert.Contains("时间: 2026-06-21 10:15:30", text, StringComparison.Ordinal);
        Assert.Contains("构建: 1.0.0+abc123", text, StringComparison.Ordinal);
        Assert.Contains("Windows: Microsoft Windows 10.0.26100", text, StringComparison.Ordinal);
        Assert.Contains(".NET: .NET 8.0.22", text, StringComparison.Ordinal);
        Assert.Contains(@"Transporter: C:\Transporter\iTMSTransporter.cmd", text, StringComparison.Ordinal);
        Assert.Contains("凭据: API Key", text, StringComparison.Ordinal);
        Assert.Contains("Bundle ID: com.example.app", text, StringComparison.Ordinal);
        Assert.Contains("版本: 1.2.3", text, StringComparison.Ordinal);
        Assert.Contains("Build: 45", text, StringComparison.Ordinal);
        Assert.Contains("Team ID: TEAM123456", text, StringComparison.Ordinal);
        Assert.Contains(@"IPA 路径: C:\Safe\demo.ipa", text, StringComparison.Ordinal);
        Assert.Contains("校验: 校验完成", text, StringComparison.Ordinal);
        Assert.Contains("构建: 已找到", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_IncludesOptionalEvidenceSections()
    {
        var text = UploadEvidenceFormatter.Format(new UploadEvidence(
            CapturedAt,
            ReadinessDetail: "状态: 可上传",
            RemotePreflightDetail: "状态: 通过",
            BuildLookupDetail: "构建: 45",
            CommandPreview: "命令: iTMSTransporter -m verify -jwt [REDACTED]",
            TransporterDetail: "状态: 校验完成"));

        Assert.Contains($"检查项{Environment.NewLine}状态: 可上传", text, StringComparison.Ordinal);
        Assert.Contains($"远端检查{Environment.NewLine}状态: 通过", text, StringComparison.Ordinal);
        Assert.Contains($"构建查询{Environment.NewLine}构建: 45", text, StringComparison.Ordinal);
        Assert.Contains($"命令{Environment.NewLine}命令: iTMSTransporter -m verify -jwt [REDACTED]", text, StringComparison.Ordinal);
        Assert.Contains($"Transporter{Environment.NewLine}状态: 校验完成", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_FiltersDefaultStatuses()
    {
        var text = UploadEvidenceFormatter.Format(new UploadEvidence(
            CapturedAt,
            BuildIdentity: string.Empty,
            IpaSummary: "未检查",
            ProfileSummary: "未导入",
            AssetDescriptionSummary: "未选择",
            ReadinessStatus: "未检查",
            EnvironmentStatus: "未验证",
            ProofStatus: "待实测",
            VerifyStatus: "未校验",
            BuildLookupStatus: "未查询",
            BuildLookupDetail: "构建: 45"));

        Assert.DoesNotContain("IPA:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("描述:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("元数据:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("检查:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("环境:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("链路:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("校验:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("构建: 未查询", text, StringComparison.Ordinal);
        Assert.Contains("构建查询", text, StringComparison.Ordinal);
        Assert.Contains("时间: 2026-06-21 10:15:30", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_RedactsKnownSecretPatternsFromOverviewFields()
    {
        var token = "eyJheader.payload.signature";
        var password = "abcd-efgh-ijkl-mnop";

        var text = UploadEvidenceFormatter.Format(new UploadEvidence(
            CapturedAt,
            TransporterPath: @"C:\Transporter\iTMSTransporter.cmd",
            CredentialMode: $"JWT {token}",
            BundleIdentifier: "com.example.app",
            TeamId: "TEAM123456",
            IpaPath: @"C:\Safe\demo.ipa",
            ProfilePath: $"password=secret {password}"));

        Assert.Contains(@"Transporter: C:\Transporter\iTMSTransporter.cmd", text, StringComparison.Ordinal);
        Assert.Contains("Bundle ID: com.example.app", text, StringComparison.Ordinal);
        Assert.Contains("Team ID: TEAM123456", text, StringComparison.Ordinal);
        Assert.Contains(@"IPA 路径: C:\Safe\demo.ipa", text, StringComparison.Ordinal);
        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        Assert.DoesNotContain(password, text, StringComparison.Ordinal);
        Assert.DoesNotContain("password=secret", text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-JWT]", text, StringComparison.Ordinal);
        Assert.Contains("password=[REDACTED]", text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-PASSWORD]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_RedactsKnownSecretPatternsFromDetailSections()
    {
        var token = "eyJheader.payload.signature";
        var privateKey = "-----BEGIN PRIVATE KEY-----\nsecret\n-----END PRIVATE KEY-----";

        var text = UploadEvidenceFormatter.Format(new UploadEvidence(
            CapturedAt,
            ReadinessDetail: "状态: 可上传",
            CommandPreview: $"命令: iTMSTransporter -m verify -jwt {token}",
            TransporterDetail: $"Authorization: Bearer {token}{Environment.NewLine}{privateKey}"));

        Assert.Contains($"检查项{Environment.NewLine}状态: 可上传", text, StringComparison.Ordinal);
        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        Assert.DoesNotContain(privateKey, text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-JWT]", text, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer [REDACTED]", text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-PRIVATE-KEY]", text, StringComparison.Ordinal);
    }
}
