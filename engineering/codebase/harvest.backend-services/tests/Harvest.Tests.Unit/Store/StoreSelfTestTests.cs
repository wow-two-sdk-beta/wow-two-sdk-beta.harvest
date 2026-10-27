using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Store;

namespace Harvest.Tests.Unit.Store;

/// <summary>The boot self-test's verdicts: UTF-8, <c>pg_trgm</c> on offer, Cyrillic trigrams.</summary>
public sealed class StoreSelfTestTests
{
    [Fact]
    public void Evaluate_ShouldPass_WhenTheDatabaseIsUtf8WithTrigrams()
    {
        StoreSelfTest.Evaluate(new StoreProbe("UTF8", TrigramAvailable: true, TrigramCount: 7)).Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_ShouldPassBeforeMigrating_WhenTheTrigramCountIsNotReadYet()
    {
        StoreSelfTest.Evaluate(new StoreProbe("UTF8", TrigramAvailable: true)).Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_ShouldNameEachMissingPiece_WhenTheDatabaseFailsEveryCheck()
    {
        var failures = StoreSelfTest.Evaluate(new StoreProbe("SQL_ASCII", TrigramAvailable: false, TrigramCount: 0));

        failures.Should().HaveCount(3);
        failures[0].Should().Contain("SQL_ASCII").And.Contain("UTF8");
        failures[1].Should().Contain("pg_trgm");
        failures[2].Should().Contain("show_trgm('привет')");
        StoreSelfTest.Describe(failures).Should().StartWith("Harvest refuses to start on this database: ");
    }
}
