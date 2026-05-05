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
        "Busca en la documentación oficial de .NET / C# en Microsoft Learn. " +
        "Devuelve los resultados más relevantes con título, URL, descripción " +
        "y fecha de última actualización. Útil para responder preguntas técnicas " +
        "con fuentes oficiales y actualizadas.")]
    public async Task<string> SearchDotnetDocs(
        [Description("La consulta de búsqueda. Puede ser lenguaje natural o keywords técnicas (ejemplo: 'IAsyncEnumerable performance' o 'cómo configurar HttpClient con Polly').")]
        string query,
        [Description("Cuántos resultados devolver. Default: 5. Máximo recomendado: 10.")]
        int top = 5,
        CancellationToken cancellationToken = default)
    {
        var response = await searchService.SearchAsync(query, top, cancellationToken: cancellationToken);

        if (response.Results.Count == 0)
        {
            return $"No se encontraron resultados en Microsoft Learn para: \"{query}\"";
        }

        var output = new StringBuilder();
        output.AppendLine($"Encontrados {response.Count} resultados (mostrando {response.Results.Count}):");
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
