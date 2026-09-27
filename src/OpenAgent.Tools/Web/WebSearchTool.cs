using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using OpenAgent.Model.Contracts;

namespace OpenAgent.Tools.Web;

public sealed class WebSearchTool : ITool, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public const int DefaultMaxResults = 4;
    public const int MaxAllowedResults = 8;

    public string Name => "search_web";
    public string Description =>
        "Cerca informazioni sul web (motore leggero DuckDuckGo HTML) e restituisce un elenco sintetico dei risultati " +
        "con titolo, snippet riassuntivo e relativo URL, senza aprire browser pesanti.";

    public ToolRiskLevel RiskLevel => ToolRiskLevel.Read;

    public event Action<string>? NetworkAccessRequested;

    public ToolDefinition Definition => ToolDefinition.CreateFunction(
        Name,
        Description,
        new
        {
            type = "object",
            properties = new
            {
                query = new
                {
                    type = "string",
                    description = "I termini o le parole chiave da cercare sul web."
                },
                max_results = new
                {
                    type = "integer",
                    description = "Numero massimo di risultati da restituire (default 4, max 8)."
                }
            },
            required = new[] { "query" }
        }
    );

    public WebSearchTool() : this(null) { }

    public WebSearchTool(HttpClient? httpClient)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 OpenAgent/0.3"
            );
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _ownsHttpClient = true;
        }
    }

    public async Task<ToolResult> ExecuteAsync(JsonDocument arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.RootElement.TryGetProperty("query", out var queryElement))
        {
            return ToolResult.Fail("Parametro obbligatorio 'query' mancante.");
        }

        string query = queryElement.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Fail("La query di ricerca fornita è vuota.");
        }

        int maxResults = DefaultMaxResults;
        if (arguments.RootElement.TryGetProperty("max_results", out var maxElem) &&
            maxElem.TryGetInt32(out int customMax))
        {
            maxResults = Math.Clamp(customMax, 1, MaxAllowedResults);
        }

        string searchUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
        NetworkAccessRequested?.Invoke(searchUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://html.duckduckgo.com/html/")
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("q", query)])
            };

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return ToolResult.Fail($"La ricerca web ha risposto con codice di errore {(int)response.StatusCode}.");
            }

            string html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var results = ParseSearchResults(html, maxResults);

            if (results.Count == 0)
            {
                return ToolResult.Ok($"Nessun risultato trovato per la ricerca: \"{query}\".");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Risultati ricerca web per \"{query}\":\n");

            int idx = 1;
            foreach (var r in results)
            {
                sb.AppendLine($"{idx}. **{r.Title}**");
                if (!string.IsNullOrWhiteSpace(r.Snippet))
                {
                    sb.AppendLine($"   {r.Snippet}");
                }
                sb.AppendLine($"   Link: {r.Url}\n");
                idx++;
            }

            return ToolResult.Ok(sb.ToString().Trim());
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ToolResult.Fail("Ricerca web annullata dall'utente.");
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Fail("Timeout durante la ricerca web: il server non ha risposto entro i limiti.");
        }
        catch (HttpRequestException ex)
        {
            return ToolResult.Fail($"Errore di connessione durante la ricerca web: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Errore imprevisto durante la ricerca web: {ex.Message}");
        }
    }

    private static List<(string Title, string Snippet, string Url)> ParseSearchResults(string html, int maxResults)
    {
        var items = new List<(string Title, string Snippet, string Url)>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var resultNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'result__body')]")
                          ?? doc.DocumentNode.SelectNodes("//div[contains(@class, 'results_links')]");

        if (resultNodes == null) return items;

        foreach (var node in resultNodes)
        {
            if (items.Count >= maxResults) break;

            var titleLink = node.SelectSingleNode(".//a[contains(@class, 'result__url')]")
                            ?? node.SelectSingleNode(".//a[contains(@class, 'result__snippet')]")
                            ?? node.SelectSingleNode(".//h2//a");

            if (titleLink == null) continue;

            string title = HtmlEntity.DeEntitize(titleLink.InnerText).Trim();
            string rawHref = titleLink.GetAttributeValue("href", "").Trim();

            // DuckDuckGo redirects often look like //duckduckgo.com/l/?uddg=https%3A%2F%2F...
            string finalUrl = ExtractCleanUrl(rawHref);

            var snippetNode = node.SelectSingleNode(".//a[contains(@class, 'result__snippet')]")
                              ?? node.SelectSingleNode(".//div[contains(@class, 'result__snippet')]");

            string snippet = snippetNode != null
                ? HtmlEntity.DeEntitize(snippetNode.InnerText).Trim()
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(finalUrl))
            {
                items.Add((title, snippet, finalUrl));
            }
        }

        return items;
    }

    private static string ExtractCleanUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return string.Empty;

        if (href.Contains("uddg="))
        {
            try
            {
                int start = href.IndexOf("uddg=", StringComparison.Ordinal);
                if (start >= 0)
                {
                    string substr = href[(start + 5)..];
                    int end = substr.IndexOf('&');
                    if (end >= 0) substr = substr[..end];
                    return Uri.UnescapeDataString(substr);
                }
            }
            catch
            {
                // Fallback
            }
        }

        if (href.StartsWith("//"))
        {
            return "https:" + href;
        }

        return href;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
