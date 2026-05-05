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
        string scope = ".NET",
        string locale = "en-us",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        // El filtro OData de Microsoft Learn restringe resultados al scope dado.
        // Sin esto, una query como "IAsyncEnumerable cancellation token" trae basura
        // de Power Apps, Azure, certificaciones, etc. — porque el ranking se va a
        // docs populares cuando los términos no son entidades nominadas únicas.
        var odataFilter = $"scopes/any(t:t eq '{scope}')";

        var queryParams = new Dictionary<string, string>
        {
            ["search"] = query,
            ["locale"] = locale,
            ["$top"] = top.ToString(),
            ["category"] = "Documentation",
            ["$filter"] = odataFilter,
        };

        var queryString = string.Join("&", queryParams
            .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var requestUrl = $"{BaseEndpoint}?{queryString}";

        using var response = await httpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<MicrosoftLearnSearchResponse>(
            stream, JsonOptions, cancellationToken);

        return result ?? new MicrosoftLearnSearchResponse([], 0);
    }
}
