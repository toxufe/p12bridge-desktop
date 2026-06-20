using P12Bridge.Core;
using Xunit;

namespace P12Bridge.Core.Tests;

public sealed class OperationHistoryPathExtractorTests
{
    [Fact]
    public void ExtractLocalPathsReturnsPathsFromDetailBeforeSummary()
    {
        var item = new OperationHistoryItem(
            DateTimeOffset.UtcNow,
            "导出 P12",
            OperationHistoryStatus.Success,
            @"已导出 C:\P12Bridge\Project",
            $"export.p12 / 2 文件{Environment.NewLine}C:\\P12Bridge\\Project\\export.p12");

        var paths = OperationHistoryPathExtractor.ExtractLocalPaths(item);

        Assert.Equal(
            [
                @"C:\P12Bridge\Project\export.p12",
                @"C:\P12Bridge\Project"
            ],
            paths);
    }

    [Fact]
    public void ExtractLocalPathsTrimsLabelsAndPunctuation()
    {
        var item = new OperationHistoryItem(
            DateTimeOffset.UtcNow,
            "选择 IPA",
            OperationHistoryStatus.Success,
            "路径: D:\\Apps\\Demo.ipa。",
            string.Empty);

        var path = Assert.Single(OperationHistoryPathExtractor.ExtractLocalPaths(item));

        Assert.Equal(@"D:\Apps\Demo.ipa", path);
    }

    [Fact]
    public void ExtractLocalPathsDeduplicatesCaseInsensitive()
    {
        var item = new OperationHistoryItem(
            DateTimeOffset.UtcNow,
            "复制路径",
            OperationHistoryStatus.Success,
            @"C:\Assets\Demo.ipa",
            @"c:\Assets\Demo.ipa");

        var path = Assert.Single(OperationHistoryPathExtractor.ExtractLocalPaths(item));

        Assert.Equal(@"c:\Assets\Demo.ipa", path);
    }

    [Fact]
    public void ExtractLocalPathsIgnoresNonLocalText()
    {
        var item = new OperationHistoryItem(
            DateTimeOffset.UtcNow,
            "账号连接",
            OperationHistoryStatus.Failed,
            "https://api.appstoreconnect.apple.com/v1/apps",
            "Bearer [REDACTED]");

        Assert.Empty(OperationHistoryPathExtractor.ExtractLocalPaths(item));
    }
}
