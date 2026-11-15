using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Tests;

/// <summary>The stale rule for the oracle price (specs/014-digibyte-io-oracle, FR-004, data-model.md) and the degraded states built on it.</summary>
public class OracleFreshnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static NetworkHealth Health(int nodeHeight = 1000, int? oracleHeight = 995, DateTimeOffset? updatedAt = null, bool reportedStale = false) => new()
    {
        Available = true,
        DeploymentActive = true,
        NodeHeight = nodeHeight,
        OraclePriceMicroUsd = 2725,
        OracleHeight = oracleHeight,
        OracleUpdatedAt = updatedAt,
        OracleReportedStale = reportedStale,
    };

    // ---------- the source says stale ----------

    [Fact]
    public void A_price_the_source_reports_as_stale_is_stale_even_when_it_is_recent()
    {
        Assert.True(Health(oracleHeight: 999, reportedStale: true).IsOracleStaleAt(Now));
    }

    // ---------- the chain height is known ----------

    [Theory]
    [InlineData(1000, 979, true)]   // 21 blocks behind
    [InlineData(1000, 980, false)]  // exactly 20 is not stale
    [InlineData(1000, 995, false)]
    [InlineData(1000, 1005, false)] // an update block ahead of the height is not stale
    public void With_a_known_height_the_price_is_stale_only_when_more_than_20_blocks_behind(int nodeHeight, int oracleHeight, bool expected)
    {
        Assert.Equal(expected, Health(nodeHeight, oracleHeight).IsOracleStaleAt(Now));
    }

    [Fact]
    public void A_known_height_wins_over_the_update_time()
    {
        // Updated an hour ago by the clock, but only 5 blocks behind by the chain: the blocks decide.
        Assert.False(Health(1000, 995, updatedAt: Now.AddHours(-1)).IsOracleStaleAt(Now));
    }

    // ---------- the height is missing from an otherwise available answer ----------

    [Theory]
    [InlineData(-299, false)] // 4 min 59 s old
    [InlineData(-300, false)] // exactly 5 minutes is not stale
    [InlineData(-301, true)]
    [InlineData(-3600, true)]
    public void With_a_missing_height_the_update_time_decides_with_a_5_minute_limit(int secondsFromNow, bool expected)
    {
        Assert.Equal(expected, Health(nodeHeight: 0, oracleHeight: 995, updatedAt: Now.AddSeconds(secondsFromNow)).IsOracleStaleAt(Now));
    }

    [Fact]
    public void With_a_missing_height_and_no_update_time_the_price_is_not_called_stale()
    {
        Assert.False(Health(nodeHeight: 0, oracleHeight: 995, updatedAt: null).IsOracleStaleAt(Now));
    }

    [Fact]
    public void Without_any_price_data_nothing_is_stale()
    {
        Assert.False(new NetworkHealth { Available = true, NodeHeight = 1000 }.IsOracleStaleAt(Now));
    }

    // ---------- degraded states ----------

    [Fact]
    public void A_healthy_fresh_price_is_not_degraded()
    {
        Assert.False(Health().IsDegraded);
    }

    [Fact]
    public void Each_condition_makes_the_state_degraded()
    {
        Assert.True((Health() with { ErrActive = true }).IsDegraded);
        Assert.True((Health() with { VolatilityFreeze = true }).IsDegraded);
        Assert.True((Health() with { MintingRestricted = true }).IsDegraded);
        Assert.True((Health() with { AllOperationsRestricted = true }).IsDegraded);
        Assert.True(Health(oracleHeight: 900).IsDegraded);               // stale by blocks
        Assert.True(Health(reportedStale: true).IsDegraded);             // stale by the source's flag
        Assert.True((Health() with { OraclePriceMicroUsd = null }).IsDegraded); // no price at all
    }

    [Fact]
    public void The_oracle_loading_flag_does_not_by_itself_provide_a_price()
    {
        var loading = new NetworkHealth { Available = true, NodeHeight = 1000, OracleLoading = true };

        Assert.False(loading.OraclePriceAvailable);
        Assert.True(loading.OracleLoading);
    }
}
