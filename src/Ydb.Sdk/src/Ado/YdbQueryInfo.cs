using Ydb.TableStats;

namespace Ydb.Sdk.Ado;

/// <summary>
/// Metadata returned by a successfully completed query.
/// </summary>
public sealed class YdbQueryInfo
{
    internal YdbQueryInfo(QueryStats? stats, VirtualTimestamp? commitTimestamp, VirtualTimestamp? snapshotTimestamp)
    {
        Stats = stats;
        CommitTimestamp = commitTimestamp;
        SnapshotTimestamp = snapshotTimestamp;
    }

    /// <summary>
    /// Gets the complete server statistics, or null when statistics were not requested or returned.
    /// </summary>
    public QueryStats? Stats { get; }

    /// <summary>
    /// Gets the virtual commit timestamp of a strict serializable write transaction, if returned.
    /// </summary>
    /// <remarks>
    /// This is database time, not wall-clock time. Values from different databases must not be compared.
    /// </remarks>
    public VirtualTimestamp? CommitTimestamp { get; }

    /// <summary>
    /// Gets the virtual timestamp of the query's snapshot, if returned.
    /// </summary>
    public VirtualTimestamp? SnapshotTimestamp { get; }
}
