# Build Log — dotnet-docs-mcp

Log honesto del proceso de construir el primer MCP server en C# para buscar en docs oficiales de .NET / C# desde Microsoft Learn.

Este archivo registra **todos los errores que encontremos**, su causa raíz, cómo los resolvimos y qué aprendimos. Es la materia prima del post del blog: nada de pretender que todo funciona a la primera.

## Stack confirmado

- **.NET SDK**: 10.0.104
- **Runtime**: 10.0.4 (Microsoft.AspNetCore.App + Microsoft.NETCore.App)
- **OS**: Linux Arch (kernel 6.19.14-arch1-1)
- **Editor de código del autor**: Claude Code como pair programmer

## Paquetes NuGet instalados

| Paquete | Versión | Para qué |
|---------|---------|----------|
| `ModelContextProtocol` | 1.2.0 | SDK oficial de MCP para C# (incluye `ModelContextProtocol.Core` 1.2.0) |
| `Microsoft.Extensions.Hosting` | 10.0.7 | `Host.CreateApplicationBuilder()` para configurar servicios y logging |
| `Microsoft.Extensions.Http` | 10.0.7 | `IHttpClientFactory` para HttpClient tipado |

Nota: `ModelContextProtocol` 1.2.0 ya es versión estable, no hubo necesidad de `--prerelease`.

## Decisiones de arquitectura

### 1. ¿Por qué API oficial de Microsoft Learn y no scraping ni embeddings?

- Microsoft Learn expone una API pública sin autenticación: `https://learn.microsoft.com/api/search`. Es la fuente oficial.
- Scraping = frágil, se rompe al primer cambio de HTML.
- Embeddings = más complejo (indexar, vector DB, costos). Vale la pena para v2, no v1.
- La API responde con `results[]`, cada uno con `title`, `url`, `description`, `lastUpdatedDate`. Suficiente para devolver al LLM.

### 2. Clean architecture aplicada en pequeño

```
DotnetDocsMcp/
├── Program.cs                           ← arranque del MCP server (host setup)
├── Tools/
│   └── DotnetDocsSearchTool.cs          ← la herramienta MCP expuesta
├── Services/
│   ├── IMicrosoftLearnSearch.cs         ← contrato del servicio
│   └── MicrosoftLearnSearchService.cs   ← implementación HTTP
└── Models/
    └── SearchResult.cs                  ← DTOs de respuesta
```

Separar la **tool** del **servicio** permite:
- Testear la búsqueda independientemente de MCP.
- Cambiar la fuente (de Learn API a embeddings locales) sin tocar la tool.
- Reusar el servicio en otros contextos (CLI, web app, etc.).

### 3. Por qué clean en lugar de un solo archivo

Un MCP server "ejemplo" se puede escribir en 50 líneas en un solo Program.cs. Pero la idea de este blog es mostrar **patrones que se usan en producción**, no demos de juguete. Si mañana quiero meter un cache, retry policies con Polly, métricas de observabilidad, o cambiar la fuente de datos, la separación me lo permite sin reescribir.

## Detalles técnicos importantes (no son errores pero son aprendizajes clave)

### A. Logging en MCP con stdio transport

**Esto es crítico y casi nadie lo documenta:**

En MCP con transporte stdio, **`stdout` se reserva exclusivamente para el protocolo JSON-RPC**. Cualquier output a stdout que no sea un mensaje del protocolo **rompe el cliente** (Claude, Cursor, etc. no van a saber qué hacer con texto suelto).

Por eso en `Program.cs` configuramos:

```csharp
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
```

Eso fuerza **TODOS los logs (Trace, Debug, Info, Warn, Error)** a `stderr`, dejando `stdout` limpio para JSON-RPC.

**Si te olvidas de esto, el síntoma es:**
- El cliente MCP no se conecta.
- Logs misteriosos sobre "invalid JSON received" o disconnect.
- Funciona en pruebas locales con `Console.WriteLine` y rompe en cliente real.

### B. Naming convention del SDK (snake_case automático)

El SDK detecta el nombre del método y lo convierte a snake_case para el protocolo:

- Método C#: `SearchDotnetDocs(...)`
- Tool name expuesta: `search_dotnet_docs`

No tienes que configurar nada manual, pero hay que saberlo cuando otros componentes (logs, debugging) muestran el nombre snake_case.

### C. Auto-generación del inputSchema

El SDK lee los parámetros del método + sus `[Description]` attributes y genera automáticamente el JSON Schema que se expone al LLM. Por ejemplo, este código:

```csharp
public async Task<string> SearchDotnetDocs(
    [Description("La consulta de búsqueda...")]
    string query,
    [Description("Cuántos resultados devolver...")]
    int top = 5,
    CancellationToken cancellationToken = default)
```

Produce este schema visible al cliente:

```json
{
  "type": "object",
  "properties": {
    "query": { "type": "string", "description": "..." },
    "top": { "type": "integer", "default": 5, "description": "..." }
  },
  "required": ["query"]
}
```

**Notar**: el `CancellationToken` NO aparece en el schema. El SDK sabe que es runtime-only y lo inyecta automáticamente.

### D. Deserialización JSON: case-sensitivity

La API de Microsoft Learn devuelve `camelCase` (`title`, `lastUpdatedDate`). Por defecto, `System.Text.Json` es **case-sensitive**, así que sin configurar nada, los records C# en PascalCase quedarían con todas las propiedades en `null`.

Solución limpia (en `MicrosoftLearnSearchService.cs`):

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
};
```

`JsonNamingPolicy.CamelCase` matchea `Title` (PascalCase en C#) con `title` (camelCase en JSON) automáticamente. Mejor que decorar cada propiedad con `[JsonPropertyName]`.

### E. User-Agent header obligatorio (buena práctica)

Aunque la API de Microsoft Learn es pública sin auth, **siempre identificar tu cliente con un User-Agent descriptivo**. Es buena práctica para APIs públicas y permite que Microsoft pueda contactar si hay abuso o cambios:

```csharp
client.DefaultRequestHeaders.UserAgent.ParseAdd(
    "DotnetDocsMcp/1.0 (+https://github.com/varocode/dotnet-docs-mcp)");
```

## Errores reales encontrados durante la sesión

### [Sin errores graves]

Build pasó a la primera con **0 warnings, 0 errors**. Test end-to-end funcionó al primer intento.

Esto es **inusual** y vale la pena anotarlo en el post: **NO** porque "soy genial", sino porque **trabajamos con un stack maduro y tomamos decisiones conservadoras**:

- SDK estable (1.2.0, no preview).
- API REST oficial sin auth (no hay flujos de OAuth que rompan).
- Sin AOT (lo dejamos para v2).
- Tipado fuerte con records y null reference types.

Si tu primer MCP es más complejo (con SSE transport, auth, AOT), espera más fricción.

## Tests realizados

### Test 1: handshake `initialize`

```bash
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}' \
  | ./bin/Debug/net10.0/DotnetDocsMcp 2>/dev/null
```

**Respuesta**: server devuelve protocolo 2024-11-05, capabilities, server info. ✅

### Test 2: `tools/list`

El server expone correctamente la tool `search_dotnet_docs` con su descripción y schema generado. ✅

### Test 3: `tools/call` end-to-end

Invocación real con `query=IAsyncEnumerable, top=2`. El server llama a Microsoft Learn API, recibe JSON, lo parsea, formatea texto Markdown, y devuelve al cliente MCP. ✅

## Próximos pasos (v2 / mejoras futuras)

- [ ] Caching en memoria con TTL para queries repetidas
- [ ] Soporte de locale dinámico (parámetro de la tool)
- [ ] Retry policy con Polly (resiliencia ante 429 / 5xx)
- [ ] Tests unitarios con xUnit + WireMock para el HttpClient
- [ ] Compilación AOT (publica como single binary nativo)
- [ ] Telemetría con OpenTelemetry
- [ ] Más tools: `fetch_dotnet_doc_content` (traer contenido completo de una URL)

## Cómo usarlo

Ver [`README.md`](./README.md) para instrucciones de uso con Claude Desktop, Claude Code, Cursor o VS Code.
