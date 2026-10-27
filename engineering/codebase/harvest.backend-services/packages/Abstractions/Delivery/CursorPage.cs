namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Represents one page of a pull cursor: <c>{ data, next, hasMore }</c>.</summary>
/// <remarks>The client holds the cursor; passing an older one within retention replays, omitting it starts over.</remarks>
/// <typeparam name="T">The item type, such as <see cref="DeliveryEvent"/>.</typeparam>
public sealed record CursorPage<T>
{
    /// <summary>Gets the page's items, in log order.</summary>
    public required IReadOnlyList<T> Data { get; init; }

    /// <summary>Gets the opaque cursor to pass for the next page; the same cursor when nothing new arrived.</summary>
    public required string Next { get; init; }

    /// <summary>Gets whether more items wait beyond this page.</summary>
    public required bool HasMore { get; init; }
}
