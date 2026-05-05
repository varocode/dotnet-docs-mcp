using DotnetDocsMcp.Models;

namespace DotnetDocsMcp.Services;

public interface IMicrosoftLearnSearch
{
    Task<MicrosoftLearnSearchResponse> SearchAsync(
        string query,
        int top = 5,
        string scope = ".NET",
        string locale = "en-us",
        CancellationToken cancellationToken = default);
}
