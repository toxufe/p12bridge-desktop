using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class InMemoryOperationHistoryServiceTests
{
    [Fact]
    public void RecordAddsNewestItemFirst()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 6, 20, 1, 2, 3, TimeSpan.Zero));
        var service = new InMemoryOperationHistoryService(clock);

        service.Record(new OperationHistoryRecordRequest("证书", OperationHistoryStatus.Success, "已生成"));
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var result = service.Record(new OperationHistoryRecordRequest("IPA", OperationHistoryStatus.Failed, "未通过"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("IPA", result.Items[0].Operation);
        Assert.Equal(OperationHistoryStatus.Failed, result.Items[0].Status);
    }

    [Fact]
    public void RecordRedactsKnownSecretPatterns()
    {
        var service = new InMemoryOperationHistoryService(new FakeClock());
        var jwt = "eyJheader.payload.signature";
        var privateKey = "-----BEGIN PRIVATE KEY-----\nsecret\n-----END PRIVATE KEY-----";
        var appSpecificPassword = "abcd-efgh-ijkl-mnop";

        var result = service.Record(new OperationHistoryRecordRequest(
            "上传",
            OperationHistoryStatus.Failed,
            $"password=secret {jwt}",
            $"Authorization: Bearer {jwt}\n{privateKey}\n{appSpecificPassword}"));

        var item = Assert.Single(result.Items);
        Assert.DoesNotContain("secret", item.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(jwt, item.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(privateKey, item.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(appSpecificPassword, item.Detail, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-JWT]", item.Summary, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer [REDACTED]", item.Detail, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-PRIVATE-KEY]", item.Detail, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-PASSWORD]", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearRemovesItems()
    {
        var service = new InMemoryOperationHistoryService(new FakeClock());
        service.Record(new OperationHistoryRecordRequest("证书", OperationHistoryStatus.Success, "已生成"));

        var result = service.Clear();

        Assert.Empty(result.Items);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock()
            : this(new DateTimeOffset(2026, 6, 20, 1, 2, 3, TimeSpan.Zero))
        {
        }

        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }
}
