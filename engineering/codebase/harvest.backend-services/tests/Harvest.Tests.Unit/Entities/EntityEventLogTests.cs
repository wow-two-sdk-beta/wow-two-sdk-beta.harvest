using System.Text.Json.Nodes;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

namespace Harvest.Tests.Unit.Entities;

/// <summary>The change feed's comparison of an entity before and after a change: which events it appends, in which
/// order, and what their payloads carry.</summary>
public sealed class EntityEventLogTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Post = Guid.NewGuid();

    [Fact]
    public void Diff_ShouldCreateTheEntityThenAddItsRunsEarliestFirst_WhenTheEntityIsNew()
    {
        var entity = Entity();
        var later = Row(entity, new DateOnly(2026, 10, 19));
        var earlier = Row(entity, new DateOnly(2025, 10, 20));
        var predicted = Row(entity, new DateOnly(2027, 10, 18), predicted: true);

        var events = EntityEventLog.Diff(null, entity, [later, predicted, earlier], Post, At);

        events.Select(row => (row.Type, row.OccurrenceId)).Should().Equal((EntityEventTypes.EntityCreated, (Guid?)null),
            (EntityEventTypes.OccurrenceAdded, earlier.Id), (EntityEventTypes.OccurrenceAdded, later.Id), (EntityEventTypes.OccurrencePredicted, predicted.Id));
        events.Should().OnlyContain(row => row.EntityId == entity.Id && row.ContainerId == entity.ContainerId && row.PostId == Post && row.At == At
            && row.Domain == "style-fashion" && row.EntityType == "event");
        events.Select(row => row.EventId).Should().OnlyHaveUniqueItems();
        events[0].Payload["name"]!.GetValue<string>().Should().Be("Tashkent Fashion Week");
        events[1].Payload["occurrence"]!["starts"]!.GetValue<string>().Should().Be("2025-10-20");
        events[1].Payload["occurrence"]!["place"]!["city"]!.GetValue<string>().Should().Be("Tashkent");
        events[3].Payload["replacesOccurrenceId"].Should().BeNull("nothing was predicted before");
    }

    [Fact]
    public void Diff_ShouldConfirmThePredictedRun_AndPredictTheNextOne()
    {
        var entity = Entity();
        var held = Row(entity, new DateOnly(2025, 10, 20));
        var predicted = Row(entity, new DateOnly(2026, 10, 19), predicted: true);
        var before = EntityEventLog.Take(entity, [held, predicted]);
        predicted.Predicted = false;
        predicted.Certainty = "stated";
        predicted.Starts = new DateOnly(2026, 10, 18);
        var next = Row(entity, new DateOnly(2027, 10, 18), predicted: true);

        var events = EntityEventLog.Diff(before, entity, [held, predicted, next], Post, At);

        events.Select(row => (row.Type, row.OccurrenceId)).Should().Equal((EntityEventTypes.OccurrenceConfirmed, predicted.Id),
            (EntityEventTypes.OccurrencePredicted, next.Id));
        events[0].Payload["previous"]!["predicted"]!.GetValue<bool>().Should().BeTrue();
        events[0].Payload["occurrence"]!["predicted"]!.GetValue<bool>().Should().BeFalse();
        events[0].Payload["changes"]!.AsArray().Select(change => change!.GetValue<string>()).Should().Equal("dates");
        events[1].Payload["replacesOccurrenceId"].Should().BeNull("the earlier prediction turned real");
    }

    [Fact]
    public void Diff_ShouldReplaceAPrediction_AndRemoveOneWithNoReplacement()
    {
        var entity = Entity();
        var old = Row(entity, new DateOnly(2026, 10, 19), predicted: true);
        var before = EntityEventLog.Take(entity, [old]);
        old.Deleted = true;
        var moved = Row(entity, new DateOnly(2026, 11, 2), predicted: true);

        var replaced = EntityEventLog.Diff(before, entity, [old, moved], Post, At);
        var removed = EntityEventLog.Diff(before, entity, [old], Post, At);

        replaced.Should().ContainSingle().Which.Type.Should().Be(EntityEventTypes.OccurrencePredicted);
        replaced[0].Payload["replacesOccurrenceId"]!.GetValue<string>().Should().Be(old.Id.ToString("D"));
        removed.Should().ContainSingle().Which.Type.Should().Be(EntityEventTypes.OccurrenceRemoved);
        removed[0].OccurrenceId.Should().Be(old.Id);
        removed[0].Payload["occurrence"]!["predicted"]!.GetValue<bool>().Should().BeTrue("the removed run's last state");
    }

    [Fact]
    public void Diff_ShouldNameWhatMoved_WhenARealRunIsPostponedAndMoved()
    {
        var entity = Entity();
        var run = Row(entity, new DateOnly(2026, 10, 19));
        var before = EntityEventLog.Take(entity, [run]);
        run.Starts = new DateOnly(2026, 11, 16);
        run.Ends = new DateOnly(2026, 11, 20);
        run.Status = "postponed";
        run.Place = new JsonObject { ["venue"] = "Humo Arena", ["city"] = "Tashkent", ["country"] = "Uzbekistan" };

        var events = EntityEventLog.Diff(before, entity, [run], Post, At);

        var changed = events.Should().ContainSingle().Which;
        changed.Type.Should().Be(EntityEventTypes.OccurrenceChanged);
        changed.Payload["changes"]!.AsArray().Select(change => change!.GetValue<string>()).Should().Equal("dates", "status", "place");
        changed.Payload["previous"]!["status"]!.GetValue<string>().Should().Be("scheduled");
        changed.Payload["occurrence"]!["status"]!.GetValue<string>().Should().Be("postponed");
        changed.Payload["occurrence"]!["place"]!["venue"]!.GetValue<string>().Should().Be("Humo Arena");
    }

    [Fact]
    public void Diff_ShouldChangeTheEntity_WhenItsStatusCadenceOrPriceMoves_AndAppendNothingOtherwise()
    {
        var listing = Entity("listing");
        listing.Facts["priceSpread"] = new JsonArray(new JsonObject { ["currency"] = "USD", ["min"] = 500, ["max"] = 500 });
        var before = EntityEventLog.Take(listing, []);

        EntityEventLog.Diff(before, listing, [], Post, At).Should().BeEmpty();

        listing.Status = "sold";
        listing.Facts["priceSpread"] = new JsonArray(new JsonObject { ["currency"] = "USD", ["min"] = 450, ["max"] = 500 });
        var changed = EntityEventLog.Diff(before, listing, [], Post, At).Should().ContainSingle().Which;
        changed.Type.Should().Be(EntityEventTypes.EntityChanged);
        changed.Payload["changes"]!.AsArray().Select(change => change!.GetValue<string>()).Should().Equal("status", "price");
        changed.Payload["status"]!.GetValue<string>().Should().Be("sold");

        var annual = Entity();
        var cadence = EntityEventLog.Take(annual, []);
        annual.Cadence = "annual";
        EntityEventLog.Diff(cadence, annual, [], Post, At).Single().Payload["changes"]!.AsArray().Single()!.GetValue<string>().Should().Be("cadence");
    }

    [Fact]
    public void MergedAndSplit_ShouldNameTheOtherEntity_AndNoPost()
    {
        var from = Entity();
        var into = Guid.NewGuid();

        var merged = EntityEventLog.Merged(from, into, At);
        var split = EntityEventLog.Split(from, into, At);

        (merged.Type, merged.PostId, merged.Payload["relatedEntityId"]!.GetValue<string>()).Should().Be((EntityEventTypes.EntityMerged, (Guid?)null, into.ToString("D")));
        (split.Type, split.Payload["relatedEntityId"]!.GetValue<string>()).Should().Be((EntityEventTypes.EntitySplit, into.ToString("D")));
    }

    [Fact]
    public void Model_ShouldLeaveAnEmptyPlaceOut()
    {
        var entity = Entity();
        var row = Row(entity, new DateOnly(2026, 1, 1));
        row.Place = new JsonObject { ["venue"] = null, ["city"] = " ", ["country"] = null };

        EntityEventLog.Model(row).Place.Should().BeNull();
    }

    private static EntityRow Entity(string type = "event") => new()
    {
        Id = Guid.NewGuid(), ContainerId = Guid.NewGuid(), EntityType = type, Name = "Tashkent Fashion Week", NameKey = "fashion tashkent week",
        PrimaryDomain = "style-fashion", FirstSeenAt = At, LastSeenAt = At,
    };

    private static OccurrenceRow Row(EntityRow entity, DateOnly starts, bool predicted = false) => new()
    {
        Id = Guid.NewGuid(), EntityId = entity.Id, Label = starts.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), Starts = starts,
        Ends = starts.AddDays(5), Precision = "day", Certainty = predicted ? "estimated" : "stated", Predicted = predicted, Status = "scheduled",
        Place = new JsonObject { ["venue"] = "Uzexpocentre", ["city"] = "Tashkent", ["country"] = "Uzbekistan" }, IsNew = true,
    };
}
