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

### [Sin errores graves de compilación o protocolo]

Build pasó a la primera con **0 warnings, 0 errors**. Test end-to-end funcionó al primer intento.

Esto es **inusual** y vale la pena anotarlo en el post: **NO** porque "soy genial", sino porque **trabajamos con un stack maduro y tomamos decisiones conservadoras**:

- SDK estable (1.2.0, no preview).
- API REST oficial sin auth (no hay flujos de OAuth que rompan).
- Sin AOT (lo dejamos para v2).
- Tipado fuerte con records y null reference types.

Si tu primer MCP es más complejo (con SSE transport, auth, AOT), espera más fricción.

### Observación honesta tras el primer test real con MCP Inspector

Probamos con MCP Inspector y la primera query que probamos fue **"como se configura una api web basica en .net"** (en español, frase natural). Los resultados que devolvió son **páginas índice generales**:

1. `.NET documentation` (raíz)
2. `Azure Architecture Center`
3. `Azure for .NET developers`
4. `.NET MAUI documentation`
5. `Visual Studio documentation`

**Análisis honesto: el resultado no es lo que un dev experimentado esperaba.** La query era específica ("API web básica") pero devolvió raíces de docs genéricas.

**Causa raíz:**

1. **`locale=en-us` hardcoded en el código.** La API de Microsoft Learn busca el contenido en inglés, pero el query estaba en español. Las palabras "como", "se", "configura", "una", "basica" no aportan signal de matching contra documentos en inglés.
2. **Categoría `Documentation`** captura overview pages, que tienden a rankear alto en searches genéricas.
3. **El campo `count` (4,106,488)** es el total del índice de Microsoft Learn, NO el resultado filtrado por relevancia. Es engañoso mostrarlo así.

**Pruebas con queries en inglés y palabras técnicas:**

Repetimos con queries más específicas y en inglés:

- `Minimal API tutorial` → resultados muy relevantes (tutorial oficial de Minimal APIs).
- `IAsyncEnumerable performance` → docs específicos de performance.
- `primary constructors C# 12` → spec del language feature.

**Conclusión:**

La tool funciona correctamente, pero su utilidad real depende mucho de **cómo se formula la query**. Para un blog en español sobre .NET, eso plantea una decisión interesante para el v1.1:

### Propuestas de mejora para v1.1

1. **Exponer `locale` como parámetro de la tool** (no hardcoded). El cliente MCP debería poder pedir `locale=es-es` o `locale=en-us` según contexto.
2. **Considerar quitar el filtro `category=Documentation`** o hacerlo configurable (algunos queries se beneficiarían de incluir `category=Tutorials` o incluso sin filtro).
3. **No reportar el `count` global** al LLM (engañoso). Solo informar cuántos resultados se devuelven realmente.
4. **Agregar al description de la tool** un hint para que el LLM sepa formular queries en inglés con keywords técnicas, que es lo que mejor matchea con la API.

### Aprendizaje meta del proceso

Esto es exactamente por qué construir e iterar > escribir teoría. Si nos hubiéramos quedado solo con "el server compila y responde", el blog post hubiera dicho "todo perfecto". Pero al probarlo con un query realista en español, descubrimos limitaciones reales que ahora podemos documentar honestamente y proponer mejoras concretas.

**Esto es el "build in public" de verdad: el código del v1 quedó funcional, las limitaciones reales quedaron documentadas, y el v1.1 ya tiene un roadmap claro basado en evidencia.**

### Error real #1: MCP Inspector crashea ante desconexión del cliente

Durante las pruebas con MCP Inspector v0.21.2, el panel del navegador empezó a mostrar:

```
GET http://localhost:6277/config net::ERR_CONNECTION_REFUSED
GET http://localhost:6277/health net::ERR_CONNECTION_REFUSED
Couldn't connect to MCP Proxy Server: Failed to fetch
```

**Causa raíz** (encontrada en los logs del proceso del Inspector):

```
file:///home/.../@modelcontextprotocol/sdk/dist/esm/server/sse.js:152
            throw new Error('Not connected');
                  ^

Error: Not connected
    at SSEServerTransport.send (.../sse.js:152:19)
    at PassThrough.<anonymous> (.../inspector/server/build/index.js:577:33)
    ...
Node.js v25.9.0
```

**Análisis:**

- El Inspector usa **SSE (Server-Sent Events)** entre el browser y su proxy server (puerto 6277).
- Cuando el cliente web cierra la pestaña / la refresca / pierde conexión, la conexión SSE se rompe.
- El proxy del Inspector intenta seguir mandando eventos al cliente desconectado y la llamada `transport.send()` lanza `Error: Not connected` **sin ser capturado**.
- Eso crashea **todo el proceso de Node.js del proxy**, no solo la sesión.
- Resultado: la siguiente vez que el browser intenta reconectar, el puerto 6277 ya no responde.

**Esto NO es bug de nuestro MCP server.** El binario `DotnetDocsMcp` siguió corriendo perfectamente — el que crasheó fue el wrapper Inspector.

**Workaround:**

1. Antes de cerrar/refrescar la pestaña del Inspector, hacer click en **"Disconnect"** en la sidebar de la UI. Eso le da al proxy una salida limpia.
2. Si ya pasó, matar procesos zombies con `pkill -f "modelcontextprotocol/inspector"` y relanzar el Inspector.

**Aprendizaje para el blog:**

Las herramientas oficiales de MCP en 2026 todavía están en versiones tempranas (Inspector v0.21.2). No es raro encontrar bugs en el manejo de edge cases (desconexiones, timeouts). Esto NO debe asustar a nadie que arranque con MCP — el ecosistema está creciendo muy rápido y los bugs se reportan/arreglan en cuestión de semanas. Pero documentar estos detalles ahorra horas de debugging a otros.

**Reportable upstream** (TODO: investigar si el bug ya está reportado en https://github.com/modelcontextprotocol/inspector/issues; si no, abrir issue).

### Comparación de calidad de resultados con queries reales

Después de probar varios queries en MCP Inspector, se construyó esta tabla con evidencia empírica:

| Query | Idioma | Relevantes/5 | Comentario |
|-------|--------|--------------|------------|
| `como se configura una api web basica en .net` | español natural | **0/5** | Solo páginas índice (.NET docs root, Azure Architecture, Visual Studio docs) |
| `Minimal API tutorial` | inglés con palabras comunes | **2-3/5** | Mezcla overviews + relevantes |
| `IAsyncEnumerable performance` | inglés técnico con palabra ambigua | **1/5** ⚠️ | Trajo Azure VM disk types, Edge DevTools — "performance" matchea miles de docs |
| `Semantic Kernel agents` | inglés con entidad nominada única | **5/5** ✅ | Todos relevantes incluido Azure MCP Server |

**Patrón descubierto:**

> El MCP server es **excelente** cuando la query incluye **nombres propios técnicos únicos** (Semantic Kernel, Agent Framework, MCP, Aspire, Blazor, MAUI). Es **mediocre** con queries que incluyen palabras técnicas comunes (performance, tutorial, async). Es **inútil** con queries en español de conceptos genéricos.

**Por qué pasa esto:**

- **Entidades nominadas únicas** ("Semantic Kernel") tienen alta especificidad en el corpus de Microsoft Learn → matching casi perfecto.
- **Palabras genéricas** ("performance") aparecen en miles de docs no relacionados → ruido alto, recall bajo.
- **Español** sufre porque las stop words ("como", "se", "una", "básica") no aportan signal de matching contra documentos en inglés.

**Propuestas concretas para v1.1 (basadas en esta evidencia):**

1. **Mejorar el `[Description]` de la tool** para que el LLM sepa formular mejores queries: indicar que use nombres técnicos específicos en inglés (ej: `Semantic Kernel agents`, no `cómo hacer agents`).
2. **Filtros de categoría dinámicos** (`Tutorials` para queries con "tutorial", `Reference` para queries con "API", etc.).
3. **No reportar el `count` global** al LLM — engañoso, no aporta utilidad.
4. **Agregar tool `fetch_doc_content`** que tome una URL y devuelva el contenido completo de la página, no solo el snippet.

**Aprendizaje meta:**

Una herramienta no es buena o mala — es buena para CIERTOS contextos. La calidad de UX depende casi totalmente de cómo el LLM aprende a formular queries para esa tool. Por eso la `[Description]` que se le pasa al LLM es **el feature más importante de un MCP**, no el código.

### Error real #2 (CRÍTICO): el MCP `dotnet-docs` no filtraba por .NET → fix con OData $filter

Después de registrar el MCP server en Claude Code, otra IA (Claude) lo probó y dejó un code review brutal y honesto. Encontró un bug que nuestros tests previos no habían detectado.

**El bug:**

Query: `IAsyncEnumerable cancellation token best practices` (100% .NET, no podría ser más .NET).

Resultados que devolvió:
1. Azure Architecture Center
2. Official Microsoft Power Apps documentation
3. Official Microsoft Power Platform documentation
4. Microsoft Learn resources (página de soporte)
5. Exam SC-100: Microsoft Cybersecurity Architect

**Cero resultados de .NET para una query 100% .NET.**

**Causa raíz:**

El nombre del MCP es `dotnet-docs`. El contrato implícito al usuario es "buscar en docs de .NET". Pero el código del v1 NO filtraba el scope: usaba `category=Documentation` que captura **toda Microsoft Learn** (.NET, Azure, Power Platform, Office, Windows, certificaciones). Cuando la query lleva entidades nominadas únicas (`Semantic Kernel`, `Minimal API`), el ranking semántico la salva. Cuando la query es conceptual (`best practices`, `cancellation`, `performance`), el ranking se va a docs populares de cualquier producto y trae basura no-.NET.

**Por qué nuestros tests no lo detectaron:**

Las queries que usamos en MCP Inspector tenían **alta especificidad** por accidente: `Semantic Kernel agents`, `Minimal API tutorial`. Si hubiéramos probado queries más conceptuales como hizo la otra IA, hubiéramos visto el bug. **Lección: testear con queries variadas, especialmente conceptuales, no solo entidades nombradas.**

**Investigación de la API:**

Probamos varios parámetros de la API de Microsoft Learn:

- `products=dotnet` → ignorado por la API (mismos resultados con o sin él)
- `scope=.NET` → ignorado
- `$facet=products` → ignorado, no devolvió facetas
- `$filter=scopes/any(t:t eq '.NET')` → **¡FUNCIONA!** Es OData syntax sobre el campo `scopes` (array)

**La fix:**

Construir el `$filter` OData en `MicrosoftLearnSearchService.cs`:

```csharp
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
```

Y exponer `scope` como parámetro de la tool con default `.NET`:

```csharp
[McpServerTool]
public async Task<string> SearchDotnetDocs(
    string query,
    int top = 5,
    string scope = ".NET",  // NUEVO
    CancellationToken cancellationToken = default)
```

**Resultado tras la fix:**

Misma query, mismo MCP, scope filter aplicado:

| Query | Antes (v1) | Después (v1.1) |
|-------|------------|-----------------|
| `IAsyncEnumerable cancellation token best practices` | 0/5 .NET | **5/5 .NET** ✅ |

Resultados nuevos:
1. .NET documentation - .NET
2. GitHub Copilot modernization overview (.NET)
3. Best practices for exceptions - .NET
4. **Cancellation in Managed Threads - .NET** ← perfecto match
5. TLS best practices with .NET Framework

**Lecciones del episodio:**

1. **El nombre del MCP es un contrato.** Si decís `dotnet-docs`, debe devolver docs de .NET. No "todo Microsoft Learn pero filtrado por categoría documentation".
2. **OData `$filter` en APIs Microsoft.** Cuando una API parece no soportar filtros, probar OData syntax es buena apuesta — es el estándar interno de Microsoft.
3. **El feedback externo es ORO.** La otra IA encontró en 2 queries lo que nosotros no vimos en 5. Pedir review honesto a otro modelo / colega / herramienta es parte del proceso, no debilidad.
4. **Probar queries variadas.** Específicas + conceptuales + en múltiples idiomas. Nuestros tests sesgados con queries de entidades nominadas ocultaron el bug.
5. **Mejoras adicionales aplicadas mientras estábamos ahí:**
   - Quitamos el campo `count` del output (era engañoso, mostraba el total global de Microsoft Learn).
   - Mejoramos la `[Description]` de la tool para guiar al LLM a formular queries en inglés con keywords técnicas.
   - Documentamos el parámetro `scope` con valores válidos comunes (`.NET`, `ASP.NET Core`, `Entity Framework Core`, `.NET MAUI`, `Azure`, `Power Platform`).

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
