using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Binds one post's extraction answers to entities inside the caller's transaction, under its container lock.</summary>
/// <remarks>
///   - events: candidates share the container and a similar name or alias (trigram similarity of the normalized keys);
///     a different city or primary domain rules a candidate out; the same city and an overlapping edition each add 0.1
///   - listings: the same post or the same source and external key is the same listing; otherwise candidates are scored
///     by item identity (<see cref="ListingIdentity"/>), the same author adding to the score
///   - the best candidate binds at <see cref="EntityOptions.MinMatchConfidence"/> or more, unless another comes within
///     <see cref="EntityOptions.AmbiguityMargin"/>; a weak or ambiguous best binds nothing and leaves a flag; no candidate
///     at <see cref="EntityOptions.CandidateFloor"/> creates the entity
///   - a candidate kept apart from an entity the post is bound to is no candidate, and the bound entity wins a near tie
///   - roles (#3.1): <c>announcement</c> creates or confirms an occurrence; <c>update</c> applies its changes with history;
///     <c>report</c> marks the occurrence held; <c>mention</c> and <c>unsure</c> only attach. An estimated date never
///     overrides a stated one. A listing's role follows its author: the same author's ad is an <c>update</c>, another
///     author's an <c>announcement</c> with its own offer
/// </remarks>
internal sealed class EntityResolver(NpgsqlConnection connection, NpgsqlTransaction transaction, EntityOptions options, DateTimeOffset now)
{
    private const string EventUseCase = PostExtractionConstants.EventUseCase;
    private const string ListingUseCase = PostExtractionConstants.ListingUseCase;

    /// <summary>Resolves one answer of the post.</summary>
    internal async Task<EntityResolveOutcome> ResolveAsync(EntityPost post, EntityAnswer answer, IReadOnlyList<PostTagsDomain> domains,
        CancellationToken ct)
    {
        if (answer.UseCase == EventUseCase && PostExtractionDefinitions.Parse<ExtractEventAnswer>(answer.Json) is { } eventAnswer)
            return await EventAsync(post, eventAnswer, answer.Role, domains, ct);
        if (answer.UseCase == ListingUseCase && PostExtractionDefinitions.Parse<ExtractListingAnswer>(answer.Json) is { } listingAnswer)
            return await ListingAsync(post, listingAnswer, answer.Role, domains, ct);
        return new EntityResolveOutcome { UseCase = answer.UseCase, Outcome = "unsure", Reason = "answer-unreadable" };
    }

    private async Task<EntityResolveOutcome> EventAsync(EntityPost post, ExtractEventAnswer answer, string role, IReadOnlyList<PostTagsDomain> domains,
        CancellationToken ct)
    {
        string? english = Clean(answer.Name?.En), original = Clean(answer.Name?.Original);
        string[] keys = [.. new[] { EntityNames.Key(english), EntityNames.Key(original) }.Where(key => key.Length > 0).Distinct()];
        if (keys.Length == 0)
            return await UnsureAsync(post, EventUseCase, "name", "no-name", null, null, ct);

        var spans = Spans(answer.Occurrences);
        int? editionYear = EntityNames.Year(answer.EditionLabel);
        string cityKey = EntityNames.PlaceKey(answer.Place?.City);
        var bindings = await EntityStore.ReadBindingsAsync(connection, transaction, post.PostId, EventUseCase, ct);
        var bound = bindings.Select(binding => binding.EntityId).ToHashSet();
        var apart = await EntityStore.ReadKeptApartAsync(connection, transaction, post.ContainerId, bound, ct);

        var scored = new List<Candidate>();
        foreach (var (candidate, nameScore) in await EntityStore.ReadEventCandidatesAsync(connection, transaction, post.ContainerId, keys,
                     options.CandidateFloor, ct))
        {
            if (apart.Contains(candidate.Id) || !DomainsOverlap(domains, candidate.PrimaryDomain))
                continue;
            double score = nameScore;
            string theirCity = EntityNames.PlaceKey(EntityJson.Text(candidate.Facts, "place", "city"));
            if (cityKey.Length > 0 && theirCity.Length > 0)
            {
                if (!EntityNames.SamePlace(cityKey, theirCity))
                    continue;
                score += 0.1;
            }
            var occurrences = await EntityStore.ReadOccurrencesAsync(connection, transaction, candidate.Id, ct);
            if (spans.Any(span => Match(occurrences, span, editionYear) is not null))
                score += 0.1;
            scored.Add(new(candidate, occurrences, score));
        }

        var decision = Decide(scored, bound);
        if (decision.Kind is DecisionKind.Weak or DecisionKind.Ambiguous)
            return await UnsureAsync(post, EventUseCase, "match", decision.Kind == DecisionKind.Weak ? "weak-match" : "ambiguous",
                decision.Candidate!.Entity.Id, Confidence(decision.Candidate.Score), ct);

        bool created = decision.Kind == DecisionKind.Create;
        var entity = created ? NewEntity(post, EntityConstants.EventType, english ?? original!, domains) : decision.Candidate!.Entity;
        List<OccurrenceRow> rows = created ? [] : decision.Candidate!.Occurrences;
        var before = created ? null : EntityEventLog.Take(entity, rows);
        entity.AddAlias(english);
        entity.AddAlias(original);
        entity.PrimaryDomain ??= Primary(domains);

        Guid? boundOccurrence = bindings.FirstOrDefault(binding => binding.EntityId == entity.Id)?.OccurrenceId;
        var (target, changes) = ApplyEvent(post, entity, rows, answer, role, spans, editionYear, boundOccurrence);
        Recompute(entity, rows);
        if (target is { Deleted: true })
            target = null;
        entity.Seen(post);

        await EntityStore.SaveEntityAsync(connection, transaction, entity, now, ct);
        await EntityStore.SaveOccurrencesAsync(connection, transaction, rows, now, ct);
        decimal confidence = created ? 1m : Confidence(decision.Candidate!.Score);
        await EntityStore.BindAsync(connection, transaction, post, EventUseCase, entity.Id, target?.Id, role, changes, null, confidence, now, ct);
        await EntityEventLog.AppendAsync(connection, transaction, EntityEventLog.Diff(before, entity, rows, post.PostId, now), ct);
        return new EntityResolveOutcome
        {
            UseCase = EventUseCase, Outcome = created ? "created" : "bound", EntityId = entity.Id, OccurrenceId = target?.Id, Role = role,
            Changes = changes, Confidence = confidence,
        };
    }

    private (OccurrenceRow? Target, List<string> Changes) ApplyEvent(EntityPost post, EntityRow entity, List<OccurrenceRow> rows,
        ExtractEventAnswer answer, string role, List<EventSpan> spans, int? editionYear, Guid? boundOccurrence)
    {
        var facts = EventFacts(answer);
        var place = PlaceOf(answer.Place);
        var changes = new List<string>();
        OccurrenceRow? target = null;
        switch (role)
        {
            case "announcement":
                EntityJson.FillMissing(entity.Facts, facts);
                foreach (var span in spans)
                {
                    var match = Match(rows, span, editionYear);
                    if (match is null)
                        rows.Add(match = NewOccurrence(entity, span, Status(answer.Status), answer.EditionLabel, place));
                    else
                        Confirm(match, span, place, post.PostId);
                    target ??= match;
                }
                target ??= spans.Count == 0 ? ByYear(rows, editionYear) : null;
                break;

            case "update":
                changes = [.. (answer.Relation?.Changes ?? []).Select(change => change.ToString().ToLowerInvariant()).Distinct()];
                target = Target(rows, spans, editionYear, boundOccurrence, post, report: false);
                string? moved = answer.Relation?.Status switch
                {
                    RelationStatus.Cancelled => "cancelled",
                    RelationStatus.Postponed => "postponed",
                    _ => null,
                };
                if (target is null && spans.Count > 0)
                    rows.Add(target = NewOccurrence(entity, spans[0], moved ?? Status(answer.Status), answer.EditionLabel, place));
                else if (target is not null)
                {
                    if ((changes.Contains("dates") || moved == "postponed") && NewDates(target, spans) is { } span)
                        MoveDates(target, span, post.PostId);
                    if (moved is not null)
                        SetStatus(target, moved, post.PostId);
                    if (changes.Contains("place") && place is not null && !EntityJson.Same(target.Place, place))
                    {
                        target.Record("place", target.Place, place, post.PostId, now);
                        target.Place = place;
                    }
                }
                foreach (var (change, key) in new[] { ("price", "prices"), ("registration", "registration"), ("deadline", "registration"), ("place", "place") })
                    if (changes.Contains(change))
                        Overwrite(entity.Facts, facts, key, target, post.PostId);
                EntityJson.FillMissing(entity.Facts, facts);
                break;

            case "report":
                target = Target(rows, spans, editionYear, boundOccurrence, post, report: true);
                if (target is null && spans.Count > 0)
                    rows.Add(target = NewOccurrence(entity, spans[0], "held", answer.EditionLabel, place));
                else if (target is not null)
                {
                    if (target.Predicted)
                        Confirm(target, spans.FirstOrDefault(span => Overlaps(target, span)), place, post.PostId);
                    SetStatus(target, "held", post.PostId);
                }
                EntityJson.FillMissing(entity.Facts, facts);
                break;

            default:
                target = spans.Select(span => Match(rows, span, editionYear)).FirstOrDefault(match => match is not null)
                    ?? (spans.Count == 0 ? ByYear(rows, editionYear) : null)
                    ?? rows.FirstOrDefault(row => row.Id == boundOccurrence && !row.Deleted);
                break;
        }
        return (target, changes);
    }

    private async Task<EntityResolveOutcome> ListingAsync(EntityPost post, ExtractListingAnswer answer, string role,
        IReadOnlyList<PostTagsDomain> domains, CancellationToken ct)
    {
        var ad = ListingFactsOf(answer);
        var bindings = await EntityStore.ReadBindingsAsync(connection, transaction, post.PostId, ListingUseCase, ct);
        var bound = bindings.Select(binding => binding.EntityId).ToHashSet();
        Guid? linked = bindings.FirstOrDefault()?.EntityId
            ?? await EntityStore.ReadListingByLinkAsync(connection, transaction, post.ContainerId, post.SourceId, post.ExternalKey, post.PostId, ct);

        EntityRow? entity = null;
        bool created = false, sameAuthor = false;
        decimal confidence = 1m;
        if (linked is { } id && await EntityStore.ReadEntityAsync(connection, transaction, id, ct) is { MergedInto: null } existing)
        {
            entity = existing;
            sameAuthor = true;
        }
        else
        {
            var apart = await EntityStore.ReadKeptApartAsync(connection, transaction, post.ContainerId, bound, ct);
            var candidates = (await EntityStore.ReadListingCandidatesAsync(connection, transaction, post.ContainerId, ad.OfferType,
                    Nullable(EntityNames.PlaceKey(ad.City)), ct))
                .Where(candidate => !apart.Contains(candidate.Id) && DomainsOverlap(domains, candidate.PrimaryDomain)).ToList();
            var authored = await EntityStore.ReadAuthoredAsync(connection, transaction, candidates.Select(candidate => candidate.Id).ToArray(),
                post.AuthorKey, ct);
            var scored = candidates
                .Select(candidate => new Candidate(candidate, [], ListingIdentity.Score(ad, ListingFactsOf(candidate.Facts),
                    authored.Contains(candidate.Id), options)))
                .Where(candidate => candidate.Score > 0).ToList();
            var decision = Decide(scored, bound);
            if (decision.Kind is DecisionKind.Weak or DecisionKind.Ambiguous)
                return await UnsureAsync(post, ListingUseCase, "match", decision.Kind == DecisionKind.Weak ? "weak-match" : "ambiguous",
                    decision.Candidate!.Entity.Id, Confidence(decision.Candidate.Score), ct);
            if (decision.Kind == DecisionKind.Create)
            {
                created = true;
                entity = NewEntity(post, EntityConstants.ListingType, Clean(answer.Title) ?? ad.CategoryPath.LastOrDefault() ?? "Listing", domains);
            }
            else
            {
                entity = decision.Candidate!.Entity;
                sameAuthor = authored.Contains(entity.Id);
                confidence = Confidence(decision.Candidate.Score);
            }
        }
        var before = created ? null : EntityEventLog.Take(entity, []);
        entity.PrimaryDomain ??= Primary(domains);

        string effective = role is "mention" or "unsure" ? role : !created && sameAuthor ? "update" : "announcement";
        List<(Guid PostId, JsonObject Offer)> offers = created ? [] : await EntityStore.ReadOffersAsync(connection, transaction, entity.Id, ct);
        var offer = effective is "announcement" or "update" ? Offer(post, answer) : null;
        var changes = new List<string>();
        if (effective == "update")
        {
            var previous = offers.LastOrDefault(row => row.PostId == post.PostId).Offer
                ?? offers.LastOrDefault(row => post.AuthorKey is not null && EntityJson.Text(row.Offer, "authorKey") == post.AuthorKey).Offer;
            if (previous is not null && !EntityJson.Same(previous["price"], offer!["price"]))
                changes.Add("price");
            if (role == "update")
                changes.AddRange((answer.Relation?.Changes ?? []).Select(change => change.ToString().ToLowerInvariant()));
            string? status = answer.Relation?.Status switch
            {
                RelationStatus.SoldOut => "sold",
                RelationStatus.Withdrawn => "withdrawn",
                _ => null,
            };
            if (status is not null && entity.Status != status)
            {
                entity.Status = status;
                changes.Add("status");
            }
            changes = [.. changes.Distinct()];
        }
        EntityJson.FillMissing(entity.Facts, ListingFactsJson(answer));
        if (offer is not null)
        {
            offers.RemoveAll(row => row.PostId == post.PostId);
            offers.Add((post.PostId, offer));
        }
        entity.Facts["priceSpread"] = Spread(offers);
        entity.Seen(post);

        await EntityStore.SaveEntityAsync(connection, transaction, entity, now, ct);
        await EntityStore.BindAsync(connection, transaction, post, ListingUseCase, entity.Id, null, effective, changes, offer, confidence, now, ct);
        await EntityEventLog.AppendAsync(connection, transaction, EntityEventLog.Diff(before, entity, [], post.PostId, now), ct);
        return new EntityResolveOutcome
        {
            UseCase = ListingUseCase, Outcome = created ? "created" : "bound", EntityId = entity.Id, Role = effective, Changes = changes,
            Confidence = confidence,
        };
    }

    /// <summary>Infers the event's cadence from its stated real occurrences and keeps exactly one predicted occurrence
    /// after the last real one while the cadence predicts; otherwise none.</summary>
    internal void Recompute(EntityRow entity, List<OccurrenceRow> rows)
    {
        var real = rows.Where(row => !row.Deleted && !row.Predicted).ToList();
        var estimate = EntityCadence.Infer(real.Where(row => row.Certainty == "stated").Select(row => row.Starts), options.Cadence);
        entity.Cadence = estimate.Cadence;
        entity.CadenceConfidence = estimate.Confidence;
        var predicted = rows.Where(row => !row.Deleted && row.Predicted).ToList();
        var last = real.MaxBy(row => row.Starts);
        OccurrenceRow? keep = null;
        if (last is not null && EntityCadence.Predicts(estimate, options.Cadence) && EntityCadence.Next(last.Starts, estimate.Cadence) is { } next)
        {
            DateOnly? ends = last.Ends is { } end ? next.AddDays(end.DayNumber - last.Starts.DayNumber) : null;
            keep = predicted.FirstOrDefault(row => row.Starts == next && row.Ends == ends);
            if (keep is null)
                rows.Add(keep = new OccurrenceRow
                {
                    Id = Guid.CreateVersion7(now), EntityId = entity.Id, Label = next.Year.ToString(CultureInfo.InvariantCulture), Starts = next,
                    Ends = ends, Precision = last.Precision is "year" or "month" ? last.Precision : "day", Certainty = "estimated",
                    Predicted = true, Status = "scheduled", Place = last.Place?.DeepClone() as JsonObject, IsNew = true,
                });
        }
        foreach (var row in predicted.Where(row => row != keep))
            row.Deleted = true;
    }

    /// <summary>Returns the occurrence a span is: dates overlapping within the window, the closest first and a real one
    /// before a predicted one; else the one occurrence of the edition label's year.</summary>
    internal OccurrenceRow? Match(IEnumerable<OccurrenceRow> rows, EventSpan span, int? editionYear)
    {
        var live = rows.Where(row => !row.Deleted).ToList();
        var overlapping = live.Where(row => Overlaps(row, span))
            .OrderBy(row => Math.Abs(row.Starts.DayNumber - span.Starts.DayNumber)).ThenBy(row => row.Predicted).FirstOrDefault();
        return overlapping ?? ByYear(live, editionYear);
    }

    private bool Overlaps(OccurrenceRow row, EventSpan span) =>
        EntityDates.Overlap(row.Starts, row.Last, span.Starts, span.Last, options.EventDateWindowDays);

    private static OccurrenceRow? ByYear(IEnumerable<OccurrenceRow> rows, int? year)
    {
        if (year is null)
            return null;
        var same = rows.Where(row => !row.Deleted && (EntityNames.Year(row.Label) ?? row.Starts.Year) == year).Take(2).ToList();
        return same.Count == 1 ? same[0] : null;
    }

    /// <summary>Finds the occurrence an update or report is about: the one its dates or edition name, the one this post
    /// already points at, else the nearest real one within the update window (for a report, the latest one already
    /// started by the post's date).</summary>
    private OccurrenceRow? Target(List<OccurrenceRow> rows, List<EventSpan> spans, int? editionYear, Guid? boundOccurrence, EntityPost post,
        bool report)
    {
        var matched = spans.Select(span => Match(rows, span, editionYear)).FirstOrDefault(match => match is not null)
            ?? (spans.Count == 0 ? ByYear(rows, editionYear) : null)
            ?? rows.FirstOrDefault(row => row.Id == boundOccurrence && !row.Deleted);
        if (matched is not null)
            return matched;
        var reference = spans.Count > 0 ? spans[0].Starts : post.PostedOn ?? DateOnly.FromDateTime(now.UtcDateTime);
        var real = rows.Where(row => !row.Deleted && !row.Predicted
            && Math.Abs(row.Starts.DayNumber - reference.DayNumber) <= options.UpdateWindowDays).ToList();
        if (report)
            return real.Where(row => row.Starts <= reference && row.Status != "cancelled").MaxBy(row => row.Starts);
        var open = real.Where(row => row.Status is not ("held" or "cancelled")).ToList();
        return open.Where(row => row.Last >= reference).MinBy(row => row.Starts)
            ?? open.MinBy(row => Math.Abs(row.Starts.DayNumber - reference.DayNumber));
    }

    /// <summary>Returns the span an update moves its occurrence to: the first one apart from the occurrence, else the
    /// first with other dates.</summary>
    private EventSpan? NewDates(OccurrenceRow target, List<EventSpan> spans) =>
        spans.FirstOrDefault(span => !EntityDates.Overlap(target.Starts, target.Last, span.Starts, span.Last, 0))
        ?? spans.FirstOrDefault(span => span.Starts != target.Starts || span.Ends != target.Ends || span.Precision != target.Precision);

    /// <summary>Turns a predicted occurrence real with the post's dates, or refines a real one's dates when the post
    /// states what was estimated or states it more precisely.</summary>
    private void Confirm(OccurrenceRow row, EventSpan? span, JsonObject? place, Guid postId)
    {
        if (row.Predicted)
        {
            row.Record("predicted", row.Dates(), span is null ? null : Dates(span), postId, now);
            row.Predicted = false;
            if (span is not null)
                SetDates(row, span);
            if (row.Place is null && place is not null)
                row.Place = place;
            return;
        }
        if (span is null)
            return;
        if (row.Certainty == "estimated" && span.Stated || row.Certainty == span.Certainty && Rank(span.Precision) > Rank(row.Precision))
            MoveDates(row, span, postId);
        if (row.Place is null && place is not null)
        {
            row.Place = place;
            row.Dirty = true;
        }
    }

    private void MoveDates(OccurrenceRow row, EventSpan span, Guid postId)
    {
        if (row.Certainty == "stated" && !span.Stated)
            return;
        if (row.Starts == span.Starts && row.Ends == span.Ends && row.Precision == span.Precision && row.Certainty == span.Certainty)
            return;
        row.Record("dates", row.Dates(), Dates(span), postId, now);
        SetDates(row, span);
    }

    private static void SetDates(OccurrenceRow row, EventSpan span)
    {
        if (row.Label == row.Starts.Year.ToString(CultureInfo.InvariantCulture))
            row.Label = span.Starts.Year.ToString(CultureInfo.InvariantCulture);
        row.Starts = span.Starts;
        row.Ends = span.Ends;
        row.Precision = span.Precision;
        row.Certainty = span.Certainty;
        row.Dirty = true;
    }

    private void SetStatus(OccurrenceRow row, string status, Guid postId)
    {
        if (row.Status == status)
            return;
        row.Record("status", row.Status, status, postId, now);
        row.Status = status;
    }

    private void Overwrite(JsonObject facts, JsonObject incoming, string key, OccurrenceRow? target, Guid postId)
    {
        if (!incoming.TryGetPropertyValue(key, out var value) || value is null or JsonArray { Count: 0 })
            return;
        var previous = facts.TryGetPropertyValue(key, out var old) ? old : null;
        if (EntityJson.Same(previous, value))
            return;
        target?.Record($"facts.{key}", previous, value, postId, now);
        facts[key] = value.DeepClone();
    }

    private OccurrenceRow NewOccurrence(EntityRow entity, EventSpan span, string status, string? editionLabel, JsonObject? place) => new()
    {
        Id = Guid.CreateVersion7(now), EntityId = entity.Id,
        Label = EntityJson.Clip(Clean(editionLabel), 40) ?? span.Starts.Year.ToString(CultureInfo.InvariantCulture),
        Starts = span.Starts, Ends = span.Ends, Precision = span.Precision, Certainty = span.Certainty, Status = status, Place = place,
        IsNew = true,
    };

    private Decision Decide(List<Candidate> scored, HashSet<Guid> bound)
    {
        var ordered = scored.OrderByDescending(candidate => candidate.Score).ThenByDescending(candidate => bound.Contains(candidate.Entity.Id))
            .ThenByDescending(candidate => candidate.Entity.LastSeenAt).ToList();
        if (ordered.Count == 0 || ordered[0].Score < options.CandidateFloor)
            return new(DecisionKind.Create, null);
        var best = ordered[0];
        if (best.Score < options.MinMatchConfidence)
            return new(DecisionKind.Weak, best);
        var close = ordered.Where(candidate => candidate.Score >= options.MinMatchConfidence
            && best.Score - candidate.Score < options.AmbiguityMargin).ToList();
        if (close.FirstOrDefault(candidate => bound.Contains(candidate.Entity.Id)) is { } sticky)
            return new(DecisionKind.Bind, sticky);
        return close.Count > 1 ? new(DecisionKind.Ambiguous, best) : new(DecisionKind.Bind, best);
    }

    private async Task<EntityResolveOutcome> UnsureAsync(EntityPost post, string useCase, string field, string reason, Guid? candidate,
        decimal? confidence, CancellationToken ct)
    {
        await EntityStore.FlagAsync(connection, transaction, post, useCase, field, reason, candidate, confidence, now, ct);
        return new EntityResolveOutcome { UseCase = useCase, Outcome = "unsure", EntityId = candidate, Confidence = confidence, Reason = reason };
    }

    private EntityRow NewEntity(EntityPost post, string type, string name, IReadOnlyList<PostTagsDomain> domains)
    {
        string canonical = EntityJson.Clip(name.Trim(), 300)!;
        return new EntityRow
        {
            Id = Guid.CreateVersion7(now), ContainerId = post.ContainerId, EntityType = type, Name = canonical, NameKey = EntityNames.Key(canonical),
            PrimaryDomain = Primary(domains), FirstSeenAt = post.FirstSeenAt, LastSeenAt = post.LastChangedAt, IsNew = true,
        };
    }

    private static List<EventSpan> Spans(IReadOnlyList<EventOccurrence>? occurrences)
    {
        var spans = new List<EventSpan>();
        foreach (var occurrence in occurrences ?? [])
        {
            if (occurrence is null)
                continue;
            string precision = occurrence.Precision.ToString().ToLowerInvariant();
            if (EntityDates.Parse(occurrence.Starts, precision) is not { } starts)
                continue;
            var ends = EntityDates.Parse(occurrence.Ends, precision);
            if (ends is { } end && end < starts)
                ends = null;
            spans.Add(new(starts, ends, precision, occurrence.Certainty == DateCertainty.Stated ? "stated" : "estimated"));
        }
        return spans;
    }

    private static JsonObject Dates(EventSpan span) => new()
    {
        ["starts"] = EntityDates.Format(span.Starts), ["ends"] = span.Ends is { } ends ? EntityDates.Format(ends) : null,
        ["precision"] = span.Precision, ["certainty"] = span.Certainty,
    };

    private static int Rank(string precision) => precision switch
    {
        "year" => 0,
        "month" => 1,
        "day" => 2,
        _ => 3,
    };

    private static string Status(EventStatus status) => status switch
    {
        EventStatus.Scheduled => "scheduled",
        EventStatus.Cancelled => "cancelled",
        EventStatus.Postponed => "postponed",
        EventStatus.Held => "held",
        _ => "unknown",
    };

    /// <summary>Returns the event's facts without occurrences, relation, flags, status or evidence quotes.</summary>
    private static JsonObject EventFacts(ExtractEventAnswer answer)
    {
        var facts = JsonSerializer.SerializeToNode(answer, EvidenceWire.Options) as JsonObject ?? new JsonObject();
        foreach (var key in new[] { "occurrences", "relation", "flags", "status", "editionLabel" })
            facts.Remove(key);
        StripEvidence(facts);
        return facts;
    }

    private static JsonObject? PlaceOf(EventPlace? place)
    {
        if (place is null || Clean(place.Venue) is null && Clean(place.City) is null && Clean(place.Country) is null)
            return null;
        return new JsonObject { ["venue"] = Clean(place.Venue), ["city"] = Clean(place.City), ["country"] = Clean(place.Country) };
    }

    private static ListingFacts ListingFactsOf(ExtractListingAnswer answer) => new()
    {
        OfferType = Offer(answer.OfferType), CategoryPath = answer.CategoryPath ?? [], City = Clean(answer.Location?.City),
        District = Clean(answer.Location?.District), Rooms = answer.Item?.Rooms, AreaM2 = answer.Item?.AreaM2?.Total, Floor = answer.Item?.Floor,
        Floors = answer.Item?.Floors, Building = Clean(answer.Item?.Building), YearBuilt = answer.Item?.YearBuilt,
        Prices = answer.Price is { } price
            ? [new ListingPrice(price.Amount, CurrencyCode(price.Currency), PeriodName(price.Period))] : [],
    };

    /// <summary>Reads a listing's merged facts and its offers' price range back into comparable facts.</summary>
    internal static ListingFacts ListingFactsOf(JsonObject facts)
    {
        var prices = new List<ListingPrice>();
        if (facts["priceSpread"] is JsonArray spread)
            foreach (var band in spread)
                foreach (var bound in new[] { "min", "max" })
                    if (EntityJson.Number(band, bound) is { } amount)
                        prices.Add(new(amount, EntityJson.Text(band, "currency"), EntityJson.Text(band, "period")));
        return new ListingFacts
        {
            OfferType = EntityJson.Text(facts, "offerType"),
            CategoryPath = facts["categoryPath"] is JsonArray path ? [.. path.Select(step => step?.GetValue<string>()).OfType<string>()] : [],
            City = EntityJson.Text(facts, "location", "city"), District = EntityJson.Text(facts, "location", "district"),
            Rooms = (int?)EntityJson.Number(facts, "item", "rooms"), AreaM2 = EntityJson.Number(facts, "item", "areaM2"),
            Floor = (int?)EntityJson.Number(facts, "item", "floor"), Floors = (int?)EntityJson.Number(facts, "item", "floors"),
            Building = EntityJson.Text(facts, "item", "building"), YearBuilt = (int?)EntityJson.Number(facts, "item", "yearBuilt"),
            Prices = prices,
        };
    }

    private static JsonObject ListingFactsJson(ExtractListingAnswer answer)
    {
        var item = answer.Item;
        return new JsonObject
        {
            ["title"] = Clean(answer.Title),
            ["offerType"] = Offer(answer.OfferType),
            ["categoryPath"] = new JsonArray([.. (answer.CategoryPath ?? []).Select(step => (JsonNode?)JsonValue.Create(step))]),
            ["location"] = new JsonObject
            {
                ["district"] = Clean(answer.Location?.District), ["city"] = Clean(answer.Location?.City),
                ["cityKey"] = Nullable(EntityNames.PlaceKey(answer.Location?.City)),
            },
            ["item"] = new JsonObject
            {
                ["rooms"] = item?.Rooms, ["areaM2"] = item?.AreaM2?.Total, ["floor"] = item?.Floor, ["floors"] = item?.Floors,
                ["building"] = Clean(item?.Building), ["yearBuilt"] = item?.YearBuilt,
            },
            ["price"] = Price(answer.Price),
        };
    }

    private static JsonObject Offer(EntityPost post, ExtractListingAnswer answer) => new()
    {
        ["authorKind"] = Wire(answer.Author?.Kind ?? ListingAuthorKind.Unknown),
        ["authorKey"] = post.AuthorKey,
        ["price"] = Price(answer.Price),
        ["link"] = post.Url,
        ["sourceId"] = post.SourceId?.ToString("D"),
        ["source"] = post.Source,
        ["externalKey"] = post.ExternalKey,
        ["firstSeenAt"] = post.FirstSeenAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
    };

    private static JsonObject? Price(ListingAsk? ask) => ask is { } stated
        ? new JsonObject { ["amount"] = stated.Amount, ["currency"] = CurrencyCode(stated.Currency), ["period"] = PeriodName(stated.Period) }
        : null;

    /// <summary>Computes the listing's price spread over each author's latest offer, per currency and period.</summary>
    internal static JsonArray Spread(IEnumerable<(Guid PostId, JsonObject Offer)> offers)
    {
        var latest = offers.GroupBy(row => EntityJson.Text(row.Offer, "authorKey") ?? row.PostId.ToString("D")).Select(group => group.Last().Offer);
        var bands = latest
            .Select(offer => (Amount: EntityJson.Number(offer, "price", "amount"), Currency: EntityJson.Text(offer, "price", "currency"),
                Period: EntityJson.Text(offer, "price", "period")))
            .Where(price => price.Amount is > 0)
            .GroupBy(price => (price.Currency, price.Period))
            .OrderBy(group => group.Key.Currency, StringComparer.Ordinal).ThenBy(group => group.Key.Period, StringComparer.Ordinal);
        return new JsonArray([.. bands.Select(group => (JsonNode?)new JsonObject
        {
            ["currency"] = group.Key.Currency, ["period"] = group.Key.Period, ["min"] = group.Min(price => price.Amount),
            ["max"] = group.Max(price => price.Amount), ["offers"] = group.Count(),
        })]);
    }

    private static string? Offer(ListingOfferType type) => type == ListingOfferType.Unsure ? null : Wire(type);

    /// <summary>Names a price's currency as the entity facts keep it (<c>USD</c>, <c>UZS</c>); an unsure one has none.</summary>
    private static string? CurrencyCode(ListingCurrency currency) => currency switch
    {
        ListingCurrency.Usd => "USD",
        ListingCurrency.Uzs => "UZS",
        _ => null,
    };

    /// <summary>Names a rent price's period as the answer writes it; none and an unsure period read as none.</summary>
    private static string? PeriodName(ListingAskPeriod? period) => period is { } named && named != ListingAskPeriod.Unsure ? Wire(named) : null;

    /// <summary>Names an enum value as the answer writes it (<c>rent-or-sale</c>, <c>management-company</c>).</summary>
    private static string Wire<T>(T value) where T : struct, Enum => JsonSerializer.SerializeToElement(value, EvidenceWire.Options).GetString()!;

    private static void StripEvidence(JsonNode? node)
    {
        if (node is JsonObject json)
        {
            json.Remove("evidence");
            foreach (var (_, value) in json.ToList())
                StripEvidence(value);
        }
        else if (node is JsonArray array)
            foreach (var value in array)
                StripEvidence(value);
    }

    /// <summary>Whether an entity's primary domain shares its tier-1 domain with one of the post's nodes; unknown on either
    /// side never rules a candidate out.</summary>
    internal static bool DomainsOverlap(IReadOnlyList<PostTagsDomain> domains, string? primary)
    {
        if (string.IsNullOrEmpty(primary) || domains.Count == 0)
            return true;
        string top = Top(primary);
        return domains.Any(domain => Top(domain.Node) == top);
    }

    private static string Top(string node) => node.Split('/', 2)[0];

    private static string? Primary(IReadOnlyList<PostTagsDomain> domains) =>
        domains.FirstOrDefault(domain => domain.Primary)?.Node ?? domains.FirstOrDefault()?.Node;

    private static decimal Confidence(double score) => Math.Round((decimal)Math.Clamp(score, 0, 1), 3, MidpointRounding.AwayFromZero);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Nullable(string value) => value.Length == 0 ? null : value;

    private enum DecisionKind { Create, Bind, Weak, Ambiguous }

    private sealed record Candidate(EntityRow Entity, List<OccurrenceRow> Occurrences, double Score);

    private sealed record Decision(DecisionKind Kind, Candidate? Candidate);
}
