using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using OpenAgent.Model.Contracts;

namespace OpenAgent.Tools.Web;

public sealed class FetchUrlTextTool : ITool, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public const int DefaultMaxChars = 4000;
    public const int HardMaxChars = 8192;
    public const int MaxDownloadBytes = 2 * 1024 * 1024; // 2 MB

    public string Name => "fetch_url_text";
    public string Description =>
        "Visita un sito web o URL (HTTP/HTTPS) ed estrae il contenuto testuale pulito della pagina, senza script né pubblicità.";

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
                url = new
                {
                    type = "string",
                    description = "L'indirizzo URL web da visitare (es. 'https://example.com')."
                },
                max_chars = new
                {
                    type = "integer",
                    description = "Numero massimo di caratteri di testo da estrarre (default 4000, max 8192)."
                }
            },
            required = new[] { "url" }
        }
    );

    public FetchUrlTextTool() : this(null) { }

    public FetchUrlTextTool(HttpClient? httpClient)
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
                MaxAutomaticRedirections = 5
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 OpenAgent/0.3"
            );
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd(
                "text/html,application/xhtml+xml,application/xml;q=0.9,text/plain;q=0.8,*/*;q=0.7"
            );
            _ownsHttpClient = true;
        }
    }

    public async Task<ToolResult> ExecuteAsync(JsonDocument arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.RootElement.TryGetProperty("url", out var urlElement))
        {
            return ToolResult.Fail("Parametro obbligatorio 'url' mancante.");
        }

        string rawUrl = urlElement.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return ToolResult.Fail("L'URL fornito è vuoto o non valido.");
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return ToolResult.Fail($"L'URL '{rawUrl}' non è valido. Sono consentiti solo protocolli HTTP e HTTPS.");
        }

        int maxChars = DefaultMaxChars;
        if (arguments.RootElement.TryGetProperty("max_chars", out var maxCharsElement) &&
            maxCharsElement.TryGetInt32(out int customMax))
        {
            maxChars = Math.Clamp(customMax, 500, HardMaxChars);
        }

        NetworkAccessRequested?.Invoke(uri.ToString());

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            ).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return ToolResult.Fail(
                    $"Richiesta HTTP fallita con codice {(int)response.StatusCode} ({response.ReasonPhrase}) per l'URL: {uri}"
                );
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "text/html";
            if (!mediaType.Contains("text") && !mediaType.Contains("html") && !mediaType.Contains("xml") && !mediaType.Contains("json"))
            {
                return ToolResult.Fail($"Il tipo di risorsa restituito ('{mediaType}') non è testuale.");
            }

            // Stream download with 2 MB safety cap
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var ms = new MemoryStream();
            byte[] buffer = new byte[8192];
            int totalRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > MaxDownloadBytes)
                {
                    ms.Write(buffer, 0, bytesRead);
                    break;
                }
                ms.Write(buffer, 0, bytesRead);
            }

            string rawContent = Encoding.UTF8.GetString(ms.ToArray());
            if (mediaType.Contains("json") || mediaType == "text/plain")
            {
                string cleanPlain = rawContent.Trim();
                if (cleanPlain.Length > maxChars)
                {
                    cleanPlain = cleanPlain[..maxChars] + $"\n\n[... Contenuto troncato a {maxChars} caratteri per tutelare il contesto del modello ...]";
                }
                return ToolResult.Ok($"Contenuto testuale da {uri}:\n\n{cleanPlain}");
            }

            // Parse HTML with HtmlAgilityPack
            string extractedText = ExtractCleanTextFromHtml(rawContent, uri, maxChars);
            return ToolResult.Ok(extractedText);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ToolResult.Fail("Recupero URL annullato dall'utente.");
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Fail($"Timeout durante il recupero della pagina web ({uri}): il server non ha risposto in tempo.");
        }
        catch (HttpRequestException ex)
        {
            return ToolResult.Fail($"Errore di connessione rete verso {uri}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Errore imprevisto durante l'estrazione da {uri}: {ex.Message}");
        }
    }

    private static string ExtractCleanTextFromHtml(string html, Uri uri, int maxChars)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove script, style, noscript, svg, iframe, nav, header, footer, etc.
        var removeNodes = doc.DocumentNode.SelectNodes(
            "//script|//style|//noscript|//svg|//iframe|//nav|//footer|//header|//aside|//form|//style"
        );

        if (removeNodes != null)
        {
            foreach (var node in removeNodes)
            {
                node.Remove();
            }
        }

        string pageTitle = string.Empty;
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (titleNode != null)
        {
            pageTitle = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(pageTitle))
        {
            sb.AppendLine($"# Titolo: {pageTitle}");
            sb.AppendLine($"# Fonte: {uri}");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine($"# Fonte: {uri}");
            sb.AppendLine();
        }

        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;

        // Extract paragraphs, headings, list items
        var contentNodes = body.SelectNodes(
            ".//h1|.//h2|.//h3|.//h4|.//p|.//li|.//blockquote|.//pre|.//article"
        );

        if (contentNodes != null && contentNodes.Count > 0)
        {
            foreach (var node in contentNodes)
            {
                string text = HtmlEntity.DeEntitize(node.InnerText).Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                // Format headings nicely
                if (node.Name.StartsWith('h') && node.Name.Length == 2)
                {
                    sb.AppendLine($"\n### {text}\n");
                }
                else if (node.Name == "li")
                {
                    sb.AppendLine($"- {text}");
                }
                else
                {
                    sb.AppendLine(text);
                    sb.AppendLine();
                }

                if (sb.Length >= maxChars)
                {
                    break;
                }
            }
        }
        else
        {
            // Fallback to body inner text
            string allText = HtmlEntity.DeEntitize(body.InnerText).Trim();
            // Normalize whitespace
            var lines = allText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                               .Select(l => l.Trim())
                               .Where(l => !string.IsNullOrWhiteSpace(l));
            sb.AppendLine(string.Join("\n\n", lines));
        }

        string result = sb.ToString().Trim();
        if (result.Length > maxChars)
        {
            result = result[..maxChars] + $"\n\n[... Contenuto troncato a {maxChars} caratteri per tutelare il contesto del modello ...]";
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            return $"Nessun contenuto testuale significativo trovato sulla pagina: {uri}";
        }

        return result;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
