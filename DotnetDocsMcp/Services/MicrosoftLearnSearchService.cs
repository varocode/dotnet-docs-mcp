using System.Text.Json;
using DotnetDocsMcp.Models;

namespace DotnetDocsMcp.Services;

public sealed class MicrosoftLearnSearchService(HttpClient httpClient) : IMicrosoftLearnSearch
{
    private const string BaseEndpoint = "https://learn.microsoft.com/api/search";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<MicrosoftLearnSearchResponse> SearchAsync(
        string query,
        int top = 5,
        string locale = "en-us",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var encodedQuery = Uri.EscapeDataString(query);
        var requestUrl = $"{BaseEndpoint}?search={encodedQuery}&locale={locale}&%24top={top}&category=Documentation";

        using var response = await httpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<MicrosoftLearnSearchResponse>(
            stream, JsonOptions, cancellationToken);

        return result ?? new MicrosoftLearnSearchResponse([], 0);
    }
}
