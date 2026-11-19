using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

namespace Harvest.Tests.Unit.Entities;

/// <summary>The opaque cursors of the entity reads: each round-trips and belongs to its container or entity.</summary>
public sealed class EntityCursorsTests
{
    [Fact]
    public void Feed_ShouldRoundTrip_AndBelongToItsContainer()
    {
        var container = Guid.NewGuid();
        var cursor = EntityCursors.Feed(container, 987_654_321UL, 42);

        EntityCursors.TryFeed(container, cursor, out var transaction, out var row).Should().BeTrue();
        (transaction, row).Should().Be((987_654_321UL, 42L));
        EntityCursors.TryFeed(Guid.NewGuid(), cursor, out _, out _).Should().BeFalse();
        EntityCursors.TryFeed(container, null, out transaction, out row).Should().BeTrue();
        (transaction, row).Should().Be((0UL, 0L), "no cursor starts over");
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("AAAA")]
    public void Feed_ShouldRefuseAMalformedCursor(string cursor) =>
        EntityCursors.TryFeed(Guid.NewGuid(), cursor, out _, out _).Should().BeFalse();

    [Fact]
    public void Feed_ShouldRefuseANegativeRowOrAnOverlongCursor()
    {
        var container = Guid.NewGuid();
        var negative = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{container:N}:1:-1"));

        EntityCursors.TryFeed(container, negative, out _, out _).Should().BeFalse();
        EntityCursors.TryFeed(container, new string('A', 600), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Posts_ShouldRoundTripASortKeyWithColons_AndBelongToItsEntity()
    {
        Guid entity = Guid.NewGuid(), post = Guid.NewGuid();
        var cursor = EntityCursors.Posts(entity, post, "2026-03-14T19:00");

        EntityCursors.TryPosts(entity, cursor, out var key, out var after).Should().BeTrue();
        (key, after).Should().Be(("2026-03-14T19:00", post));
        EntityCursors.TryPosts(Guid.NewGuid(), cursor, out _, out _).Should().BeFalse();
        EntityCursors.TryPosts(entity, "%%%", out _, out _).Should().BeFalse();
        EntityCursors.TryPosts(entity, "", out key, out _).Should().BeTrue();
        key.Should().BeNull();
    }
}
