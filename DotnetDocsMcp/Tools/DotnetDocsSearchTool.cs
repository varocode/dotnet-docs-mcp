using System.ComponentModel;
using System.Text;
using DotnetDocsMcp.Services;
using ModelContextProtocol.Server;

namespace DotnetDocsMcp.Tools;

[McpServerToolType]
public sealed class DotnetDocsSearchTool(IMicrosoftLearnSearch searchService)
{
    [McpServerTool]
    [Description(
        "Busca en la documentación oficial de .NET / C# / ASP.NET Core / EF Core en Microsoft Learn. " +
        "Devuelve resultados oficiales filtrados al scope .NET (no contamina con docs de Azure, Power Platform o certificaciones). " +
        "Para mejores resultados, formula la query en INGLÉS con keywords técnicas específicas " +
        "(ej: 'IAsyncEnumerable cancellation token', 'JsonSerializer source generators', 'minimal API authentication'). " +
        "El parámetro 'scope' permite cambiar el filtro a otros productos del ecosistema Microsoft.")]
    public async Task<string> SearchDotnetDocs(
        [Description("La consulta de búsqueda en inglés con keywords técnicas. Funciona mejor con nombres específicos del BCL, namespaces, tipos o features (ej: 'System.Text.Json polymorphism', 'primary constructors C# 12', 'minimal API filters').")]
        string query,
        [Description("Cuántos resultados devolver. Default: 5. Máximo recomendado: 10.")]
        int top = 5,
        [Description("Scope del filtro Microsoft Learn. Default: '.NET'. Otros valores válidos: 'ASP.NET Core', 'Entity Framework Core', '.NET MAUI', 'Azure', 'Power Platform'. Usa este parámetro solo si necesitás explícitamente buscar fuera de .NET.")]
        string scope = ".NET",
        CancellationToken cancellationToken = default)
    {
        var response = await searchService.SearchAsync(query, top, scope, cancellationToken: cancellationToken);

        if (response.Results.Count == 0)
        {
            return $"No se encontraron resultados en Microsoft Learn (scope: {scope}) para: \"{query}\"";
        }

        var output = new StringBuilder();
        output.AppendLine($"Resultados de Microsoft Learn (scope: {scope}, mostrando {response.Results.Count}):");
        output.AppendLine();

        for (var i = 0; i < response.Results.Count; i++)
        {
            var result = response.Results[i];

            if (i > 0)
            {
                output.AppendLine();
                output.AppendLine("---");
                output.AppendLine();
            }

            output.AppendLine($"**{result.Title}**");
            output.AppendLine($"URL: {result.Url}");

            if (result.LastUpdatedDate.HasValue)
            {
                output.AppendLine($"Última actualización: {result.LastUpdatedDate.Value:yyyy-MM-dd}");
            }

            output.AppendLine();
            output.AppendLine(result.Description);
        }

        return output.ToString();
    }
}
