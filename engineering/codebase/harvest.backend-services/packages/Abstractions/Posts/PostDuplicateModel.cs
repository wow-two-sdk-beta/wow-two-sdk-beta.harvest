namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Represents a content match to a different external URL in the same container.</summary>
/// <param name="GroupId">The reference link identifier shared by the group's members.</param>
/// <param name="ReferencePostId">The post that contains the reference link.</param>
/// <param name="ReferenceUrl">The external URL the copy was matched against.</param>
/// <param name="Match">The comparison result, either <c>exact</c> or <c>similar</c>.</param>
/// <param name="Similarity">The similarity of the two source readings, between zero and one.</param>
public sealed record PostDuplicateModel(Guid GroupId, Guid ReferencePostId, string ReferenceUrl, string Match, float Similarity);
