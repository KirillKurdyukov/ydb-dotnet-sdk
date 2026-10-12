using Xunit;
using Ydb.Query;
using Ydb.Sdk.Ado.RetryPolicy;
using Ydb.Sdk.Ado.Tests.Utils;
using Ydb.TableStats;

namespace Ydb.Sdk.Ado.Tests;

public class YdbQueryInfoTests : TestBase
{
    [Theory]
    [InlineData(0, StatsMode.None)]
    [InlineData(0, StatsMode.Basic)]
    [InlineData(0, StatsMode.Full)]
    [InlineData(1, StatsMode.Basic)]
    [InlineData(2, StatsMode.Basic)]
    public async Task QueryInfo_WhenQueryCompletes_ContainsRequestedStatistics(int sessionMode, StatsMode statsMode)
    {
        await using var connection = new YdbConnection(new YdbConnectionStringBuilder(ConnectionString)
        {
            EnableImplicitSession = sessionMode == 1
        });
        if (sessionMode == 2)
        {
            await connection.OpenAsync(new YdbRetryPolicyExecutor(new YdbRetryPolicy(new YdbRetryPolicyConfig())));
        }
        else
        {
            await connection.OpenAsync();
        }

        await using var command = new YdbCommand("SELECT 1; SELECT 2;", connection) { StatsMode = statsMode };
        await using var reader = await command.ExecuteReaderAsync();
        Assert.Null(reader.QueryInfo);
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.False(await reader.ReadAsync());
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.False(await reader.ReadAsync());
        Assert.False(await reader.NextResultAsync());

        var info = Assert.IsType<YdbQueryInfo>(reader.QueryInfo);
        Assert.Null(info.CommitTimestamp);
        if (statsMode == StatsMode.None)
        {
            Assert.Null(info.Stats);
        }
        else
        {
            Assert.NotNull(info.Stats);
            Assert.True(info.Stats.TotalDurationUs > 0);
            if (statsMode == StatsMode.Full)
            {
                Assert.False(string.IsNullOrEmpty(info.Stats.QueryPlan));
            }
        }
    }

    [Fact]
    public async Task QueryInfo_WhenMetadataPrecedesRows_PublishesAllMetadataOnlyAtSuccessfulEnd()
    {
        await using var connection = await CreateOpenConnectionAsync();
        var rows = ResultSet.Parser.ParseJson(
            """{"columns":[{"name":"value","type":{"typeId":"BOOL"}}],"rows":[{"items":[{"boolValue":true}]}]}""");
        await using var reader = await YdbDataReader.CreateYdbDataReader(
            new MockAsyncEnumerator<ExecuteQueryResponsePart>(
            [
                new ExecuteQueryResponsePart
                {
                    Status = StatusIds.Types.StatusCode.Success,
                    SnapshotTimestamp = new VirtualTimestamp { PlanStep = 7, TxId = 11 }
                },
                new ExecuteQueryResponsePart
                {
                    Status = StatusIds.Types.StatusCode.Success, ResultSet = rows,
                    ExecStats = new QueryStats { TotalDurationUs = 23 },
                    CommitTimestamp = new VirtualTimestamp { PlanStep = ulong.MaxValue, TxId = 13 }
                }
            ]), connection, null, 0);

        Assert.Equal(1, reader.FieldCount);
        Assert.Null(reader.QueryInfo);
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.Null(reader.QueryInfo);
        Assert.False(await reader.ReadAsync());
        var info = Assert.IsType<YdbQueryInfo>(reader.QueryInfo);
        Assert.Equal((ulong)23, info.Stats!.TotalDurationUs);
        Assert.Equal(ulong.MaxValue, info.CommitTimestamp!.PlanStep);
        Assert.Equal((ulong)13, info.CommitTimestamp.TxId);
        Assert.Equal((ulong)7, info.SnapshotTimestamp!.PlanStep);
        await reader.CloseAsync();
        Assert.Same(info, reader.QueryInfo);
    }

    [Fact]
    public async Task QueryInfo_WhenStreamFailsAfterMetadata_DoesNotPublishSuccessfulInformation()
    {
        await using var connection = await CreateOpenConnectionAsync();
        await using var reader = await YdbDataReader.CreateYdbDataReader(
            new MockAsyncEnumerator<ExecuteQueryResponsePart>(
            [
                new ExecuteQueryResponsePart
                    { Status = StatusIds.Types.StatusCode.Success, ResultSet = new ResultSet() },
                new ExecuteQueryResponsePart
                {
                    Status = StatusIds.Types.StatusCode.Success,
                    ExecStats = new QueryStats { TotalDurationUs = 23 },
                    CommitTimestamp = new VirtualTimestamp { PlanStep = 7, TxId = 11 }
                },
                new ExecuteQueryResponsePart { Status = StatusIds.Types.StatusCode.Aborted }
            ]), connection, null, 0);

        await Assert.ThrowsAsync<YdbException>(() => reader.ReadAsync());
        Assert.Null(reader.QueryInfo);
    }
}
