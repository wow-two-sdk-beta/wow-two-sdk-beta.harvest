namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Configures the entity layer, bound from <see cref="EntityConstants.SectionName"/>.</summary>
/// <remarks>Resolution queues and its worker runs only while <see cref="Enabled"/> is set; the operator verbs read and
/// correct entities either way. A match below <see cref="MinMatchConfidence"/> binds nothing: a false merge is worse than
/// a duplicate.</remarks>
public sealed class EntityOptions
{
    /// <summary>Gets or sets whether a done extraction queues its post's resolution and the worker drains the queue.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the confidence a candidate needs for a post to bind to it, 0 to 1.</summary>
    public double MinMatchConfidence { get; set; } = 0.75;

    /// <summary>Gets or sets the confidence under which a candidate is no candidate at all, so the post makes a new
    /// entity; between this and <see cref="MinMatchConfidence"/> the post binds nothing and is flagged.</summary>
    public double CandidateFloor { get; set; } = 0.5;

    /// <summary>Gets or sets how close the second candidate may come to the best before the post is flagged as
    /// ambiguous instead of bound.</summary>
    public double AmbiguityMargin { get; set; } = 0.05;

    /// <summary>Gets or sets how many days apart two runs' dates may lie and still be one edition.</summary>
    public int EventDateWindowDays { get; set; } = 10;

    /// <summary>Gets or sets how many days a dated update may move an edition, such as a postponement.</summary>
    public int UpdateWindowDays { get; set; } = 400;

    /// <summary>Gets or sets how far apart two ads' areas may be, as a fraction, and still be one listing.</summary>
    public double ListingAreaTolerance { get; set; } = 0.05;

    /// <summary>Gets or sets how far an ad's price may sit outside a listing's offers, as a fraction, and still count as
    /// the same price band.</summary>
    public double ListingPriceBand { get; set; } = 0.2;

    /// <summary>Gets or sets the cadence tolerances and the confidence a prediction needs.</summary>
    public EntityCadenceOptions Cadence { get; set; } = new();

    /// <summary>Gets or sets how many queued resolutions one worker pass claims.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Gets or sets how long the worker waits when nothing is due.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the base delay before a failed resolution is retried; it grows with each attempt.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets how long a claim holds a post before another worker may take it.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>Configures cadence inference: how many days a gap between two stated starts may stray from each period, and
/// the confidence a predicted next occurrence needs.</summary>
public sealed class EntityCadenceOptions
{
    /// <summary>Gets or sets the tolerance around 365 days.</summary>
    public int AnnualDays { get; set; } = 45;

    /// <summary>Gets or sets the tolerance around 182 days.</summary>
    public int SemiannualDays { get; set; } = 25;

    /// <summary>Gets or sets the tolerance around 91 days.</summary>
    public int QuarterlyDays { get; set; } = 15;

    /// <summary>Gets or sets the tolerance around 30 days.</summary>
    public int MonthlyDays { get; set; } = 6;

    /// <summary>Gets or sets the cadence confidence a predicted occurrence needs, 0 to 1.</summary>
    public double PredictMinConfidence { get; set; } = 0.5;
}
