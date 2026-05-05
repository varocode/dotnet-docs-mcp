# dotnet-docs-mcp

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=.net)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**MCP server en C# para buscar en la documentación oficial de .NET / C# desde Microsoft Learn.**

Permite que Claude, Cursor, VS Code o cualquier cliente compatible con [Model Context Protocol](https://modelcontextprotocol.io/) consulten directamente la documentación oficial de Microsoft Learn como contexto fresco. Útil para responder preguntas técnicas de .NET / C# / ASP.NET Core con fuentes actualizadas.

## Por qué existe

La mayoría de LLMs tienen un cutoff de conocimiento que los deja desactualizados respecto a las APIs y patrones modernos de .NET. Este MCP server resuelve eso conectando al cliente con la API oficial de Microsoft Learn (la misma que usa la barra de búsqueda de `learn.microsoft.com`), devolviendo resultados frescos directamente desde la fuente.

## Características

- ✅ Una herramienta MCP: `search_dotnet_docs`
- ✅ Conexión directa a la API oficial de Microsoft Learn (sin scraping)
- ✅ Sin auth, sin API keys, sin costos
- ✅ Configurable: cantidad de resultados (`top`)
- ✅ Stdio transport (compatible con todos los clientes MCP estándar)
- ✅ Logging a stderr (no rompe el protocolo)
- ✅ Clean architecture en pequeño (separación tool / service / models)

## Requisitos

- .NET 10.0 SDK o superior
- Cliente compatible con MCP (Claude Desktop, Claude Code, Cursor, VS Code con extensión, etc.)

## Build

```bash
git clone https://github.com/varocode/dotnet-docs-mcp.git
cd dotnet-docs-mcp
dotnet build
```

El binario queda en `DotnetDocsMcp/bin/Debug/net10.0/DotnetDocsMcp` (o `.exe` en Windows).

Para release optimizado:

```bash
dotnet publish -c Release -o ./publish
```

## Configuración por cliente

### Claude Desktop / Claude Code

Editá el archivo de configuración:

- **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Linux**: `~/.config/Claude/claude_desktop_config.json`
- **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`

Agregá:

```json
{
  "mcpServers": {
    "dotnet-docs": {
      "command": "/ruta/absoluta/a/DotnetDocsMcp/bin/Debug/net10.0/DotnetDocsMcp"
    }
  }
}
```

Reinicia Claude Desktop / Claude Code.

### Cursor

En `~/.cursor/mcp.json` (o `.cursor/mcp.json` en tu proyecto):

```json
{
  "mcpServers": {
    "dotnet-docs": {
      "command": "/ruta/absoluta/a/DotnetDocsMcp/bin/Debug/net10.0/DotnetDocsMcp"
    }
  }
}
```

### VS Code

Con la extensión de MCP, agregá la misma configuración en `.vscode/mcp.json`.

## Uso

Una vez configurado, simplemente hacé preguntas técnicas de .NET / C# y el cliente va a invocar la herramienta automáticamente. Ejemplos:

- "¿Cómo configurar IHttpClientFactory con Polly en .NET 10?"
- "Buscá las novedades de C# 14 sobre primary constructors."
- "¿Cuál es la diferencia entre `IAsyncEnumerable` y `IEnumerable` en performance?"

El LLM va a llamar a `search_dotnet_docs` con tu consulta, recibirá los resultados oficiales de Microsoft Learn, y los integrará en su respuesta con links a la documentación real.

## Test manual del protocolo

Para verificar que el server responde correctamente sin necesidad de cliente:

```bash
(echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}'; \
 echo '{"jsonrpc":"2.0","method":"notifications/initialized"}'; \
 echo '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'; \
 sleep 1) | ./DotnetDocsMcp/bin/Debug/net10.0/DotnetDocsMcp 2>/dev/null
```

## Arquitectura

```
DotnetDocsMcp/
├── Program.cs                           Setup del host genérico + MCP + DI
├── Tools/
│   └── DotnetDocsSearchTool.cs          Tool MCP expuesta
├── Services/
│   ├── IMicrosoftLearnSearch.cs         Contrato del servicio de búsqueda
│   └── MicrosoftLearnSearchService.cs   Implementación HTTP
└── Models/
    └── SearchResult.cs                  DTOs de la respuesta de la API
```

## Build log

Ver [`BUILD_LOG.md`](./BUILD_LOG.md) para el registro detallado del proceso de construcción: decisiones de arquitectura, errores encontrados, y aprendizajes.

## Sobre el autor

Construido por [Alvaro Acevedo](https://alvaroacevedo.dev/) como parte de su serie **build in public** sobre **.NET + IA aplicada en español**. El proceso de construcción está documentado en el blog: [alvaroacevedo.dev](https://alvaroacevedo.dev/).

## Licencia

MIT — ver [`LICENSE`](./LICENSE).

## Contribuir

PRs, issues y sugerencias bienvenidas. Si querés agregar una tool nueva (por ejemplo `fetch_dotnet_doc_content`), abrí un issue primero para discutir el diseño.
