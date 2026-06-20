using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class JsonOperationHistoryServiceTests : IDisposable
{
    private readonly string tempDirectory;
    private readonly string historyPath;

    public JsonOperationHistoryServiceTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"p12bridge-history-{Guid.NewGuid():N}");
        historyPath = Path.Combine(tempDirectory, "operation-history.json");
    }

    [Fact]
    public void ListReturnsEmptyWhenFileDoesNotExist()
    {
        var service = CreateService();

        var result = service.List();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Items);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void RecordPersistsItemsForNewServiceInstance()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 6, 20, 1, 2, 3, TimeSpan.Zero));
        var service = CreateService(clock);
        service.Record(new OperationHistoryRecordRequest("证书", OperationHistoryStatus.Success, "已生成"));
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        service.Record(new OperationHistoryRecordRequest("IPA", OperationHistoryStatus.Failed, "未通过"));

        var loaded = CreateService().List();

        Assert.True(loaded.IsSuccess);
        Assert.Equal(2, loaded.Items.Count);
        Assert.Equal("IPA", loaded.Items[0].Operation);
        Assert.Equal(OperationHistoryStatus.Failed, loaded.Items[0].Status);
        Assert.Equal(new DateTimeOffset(2026, 6, 20, 1, 3, 3, TimeSpan.Zero), loaded.Items[0].OccurredAt);
    }

    [Fact]
    public void RecordRedactsSecretsBeforeWritingJson()
    {
        var service = CreateService();
        var jwt = "eyJheader.payload.signature";
        var privateKey = "-----BEGIN PRIVATE KEY-----\nsecret\n-----END PRIVATE KEY-----";
        var appSpecificPassword = "abcd-efgh-ijkl-mnop";

        var result = service.Record(new OperationHistoryRecordRequest(
            "上传",
            OperationHistoryStatus.Failed,
            $"password=secret {jwt}",
            $"Authorization: Bearer {jwt}\n{privateKey}\n{appSpecificPassword}"));

        var item = Assert.Single(result.Items);
        var json = File.ReadAllText(historyPath);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(jwt, json, StringComparison.Ordinal);
        Assert.DoesNotContain(privateKey, json, StringComparison.Ordinal);
        Assert.DoesNotContain(appSpecificPassword, json, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-JWT]", item.Summary, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer [REDACTED]", item.Detail, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-PRIVATE-KEY]", item.Detail, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-PASSWORD]", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ListReturnsWarningWhenJsonIsCorrupt()
    {
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(historyPath, "{bad json");
        var service = CreateService();

        var result = service.List();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Items);
        Assert.Contains(result.Issues, issue => issue.Code == OperationHistoryErrorCodes.LoadFailed);
    }

    [Fact]
    public void ClearDeletesStoredHistory()
    {
        var service = CreateService();
        service.Record(new OperationHistoryRecordRequest("证书", OperationHistoryStatus.Success, "已生成"));

        var result = service.Clear();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Items);
        Assert.False(File.Exists(historyPath));
        Assert.Empty(service.List().Items);
    }

    [Fact]
    public void RecordTrimsToMaximumRetainedItems()
    {
        var service = CreateService();

        for (var index = 0; index < 205; index++)
        {
            service.Record(new OperationHistoryRecordRequest(
                $"操作 {index}",
                OperationHistoryStatus.Success,
                "完成"));
        }

        var loaded = CreateService().List();

        Assert.Equal(200, loaded.Items.Count);
        Assert.Equal("操作 204", loaded.Items[0].Operation);
        Assert.Equal("操作 5", loaded.Items[^1].Operation);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private JsonOperationHistoryService CreateService(IClock? clock = null) =>
        new(historyPath, clock);

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }
}
