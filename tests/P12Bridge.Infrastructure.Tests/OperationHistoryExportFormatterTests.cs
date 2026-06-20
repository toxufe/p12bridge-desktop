using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class OperationHistoryExportFormatterTests
{
    [Fact]
    public void FormatReturnsEmptyForEmptyHistory()
    {
        var text = OperationHistoryExportFormatter.Format(Array.Empty<OperationHistoryItem>());

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void FormatIncludesDetailWhenPresent()
    {
        var occurredAt = CreateLocalTime(2026, 6, 20, 1, 2, 3);
        var item = new OperationHistoryItem(
            occurredAt,
            "上传 IPA",
            OperationHistoryStatus.Failed,
            "上传失败",
            "APP_STORE_INFO_PLIST_MISSING: 元数据不存在 / 选择元数据");

        var text = OperationHistoryExportFormatter.Format(item);

        Assert.Equal(
            $"{occurredAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} 失败 上传 IPA{Environment.NewLine}" +
            $"上传失败{Environment.NewLine}" +
            "APP_STORE_INFO_PLIST_MISSING: 元数据不存在 / 选择元数据",
            text);
    }

    [Fact]
    public void FormatOmitsBlankDetailLine()
    {
        var occurredAt = CreateLocalTime(2026, 6, 20, 4, 5, 6);
        var item = new OperationHistoryItem(
            occurredAt,
            "制作证书",
            OperationHistoryStatus.Success,
            "已生成",
            string.Empty);

        var text = OperationHistoryExportFormatter.Format(item);

        Assert.Equal(
            $"{occurredAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} 成功 制作证书{Environment.NewLine}已生成",
            text);
    }

    [Fact]
    public void FormatMapsWarningStatus()
    {
        var occurredAt = CreateLocalTime(2026, 6, 20, 7, 8, 9);
        var item = new OperationHistoryItem(
            occurredAt,
            "检查 IPA",
            OperationHistoryStatus.Warning,
            "有警告",
            string.Empty);

        var text = OperationHistoryExportFormatter.Format(item);

        Assert.StartsWith($"{occurredAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} 警告 检查 IPA", text);
    }

    [Fact]
    public void FormatMultipleItemsSeparatesEntries()
    {
        var first = new OperationHistoryItem(
            CreateLocalTime(2026, 6, 20, 9, 0, 0),
            "导入描述",
            OperationHistoryStatus.Success,
            "已导入",
            string.Empty);
        var second = new OperationHistoryItem(
            CreateLocalTime(2026, 6, 20, 9, 1, 0),
            "导出 P12",
            OperationHistoryStatus.Success,
            "已导出",
            "C:\\safe\\export.p12");

        var text = OperationHistoryExportFormatter.Format(new[] { first, second });

        Assert.Contains($"{Environment.NewLine}{Environment.NewLine}", text, StringComparison.Ordinal);
        Assert.Contains("导入描述", text, StringComparison.Ordinal);
        Assert.Contains("导出 P12", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatUsesRedactedHistoryItems()
    {
        var historyPath = Path.Combine(Path.GetTempPath(), $"p12bridge-export-{Guid.NewGuid():N}.json");
        try
        {
            var service = new JsonOperationHistoryService(historyPath);
            var jwt = "eyJheader.payload.signature";
            var privateKey = "-----BEGIN PRIVATE KEY-----\nsecret\n-----END PRIVATE KEY-----";

            service.Record(new OperationHistoryRecordRequest(
                "上传",
                OperationHistoryStatus.Failed,
                $"password=secret {jwt}",
                $"Authorization: Bearer {jwt}{Environment.NewLine}{privateKey}"));

            var text = OperationHistoryExportFormatter.Format(service.List().Items);

            Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(jwt, text, StringComparison.Ordinal);
            Assert.DoesNotContain(privateKey, text, StringComparison.Ordinal);
            Assert.Contains("[REDACTED-JWT]", text, StringComparison.Ordinal);
            Assert.Contains("Authorization: Bearer [REDACTED]", text, StringComparison.Ordinal);
            Assert.Contains("[REDACTED-PRIVATE-KEY]", text, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(historyPath))
            {
                File.Delete(historyPath);
            }
        }
    }

    private static DateTimeOffset CreateLocalTime(int year, int month, int day, int hour, int minute, int second)
    {
        var localDate = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        return new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));
    }
}
