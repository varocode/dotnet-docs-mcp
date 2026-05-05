namespace DotnetDocsMcp.Models;

public sealed record SearchResult(
    string Title,
    string Url,
    string Description,
    DateTimeOffset? LastUpdatedDate);

public sealed record MicrosoftLearnSearchResponse(
    IReadOnlyList<SearchResult> Results,
    int Count);
