using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Model.Contracts;

namespace OpenAgent.Model;

public class GeminiClient : IModelProvider, IDisposable
{
    public const string DefaultPrimaryModel = "gemini-3.8-flash";
    public const string DefaultFallbackModel = "gemini-3.5-flash-lite";
    public const string GoogleOpenAiEndpoint = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _apiKey;
    private readonly string _endpointUrl;

    public string PrimaryModel { get; set; } = DefaultPrimaryModel;
    public string FallbackModel { get; set; } = DefaultFallbackModel;
    public bool EnableFallback { get; set; } = true;

    public event Action<string, string, string>? FallbackTriggered; // (primaryModel, fallbackModel, reason)

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public GeminiClient(string apiKey, string? primaryModel = null, string? fallbackModel = null, HttpClient? httpClient = null, string? endpointUrl = null)
    {
        _apiKey = apiKey?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(primaryModel)) PrimaryModel = primaryModel.Trim();
        if (!string.IsNullOrWhiteSpace(fallbackModel)) FallbackModel = fallbackModel.Trim();
        _endpointUrl = !string.IsNullOrWhiteSpace(endpointUrl) ? endpointUrl.Trim() : GoogleOpenAiEndpoint;

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(2)
            };
            _ownsHttpClient = true;
        }
    }

    public async Task<ChatCompletionResponse> GenerateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("Chiave API Google Gemini non configurata. Inseriscila nelle Impostazioni per utilizzare il Cloud.");
        }

        string targetModel = string.IsNullOrWhiteSpace(request.Model) || request.Model.StartsWith("Qwen", StringComparison.OrdinalIgnoreCase) || request.Model.StartsWith("Spark", StringComparison.OrdinalIgnoreCase) || request.Model.StartsWith("Gemma", StringComparison.OrdinalIgnoreCase)
            ? PrimaryModel
            : request.Model;

        var primaryRequest = CloneRequestWithModel(request, targetModel);

        try
        {
            return await ExecuteRequestAsync(primaryRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (EnableFallback && !string.Equals(targetModel, FallbackModel, StringComparison.OrdinalIgnoreCase))
        {
            // Transient error, rate limit (429), or service overload (503) -> Fallback to Gemini 3.5 Flash-Lite
            string reason = $"Errore chiamata a {targetModel}: {ex.Message}. Attivazione fallback a {FallbackModel}.";
            FallbackTriggered?.Invoke(targetModel, FallbackModel, reason);

            var fallbackRequest = CloneRequestWithModel(request, FallbackModel);
            return await ExecuteRequestAsync(fallbackRequest, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ChatCompletionResponse> ExecuteRequestAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        string jsonPayload = JsonSerializer.Serialize(request, JsonOptions);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpointUrl);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        httpRequest.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Errore di rete durante la connessione ai server Google Gemini: {ex.Message}", ex);
        }

        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Gemini API ha restituito errore ({(int)response.StatusCode} {response.ReasonPhrase}): {responseJson}");
        }

        try
        {
            var result = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson, JsonOptions);
            return result ?? throw new InvalidOperationException("Risposta JSON vuota ricevuta da Google Gemini.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Impossibile deserializzare la risposta da Google Gemini: {ex.Message}\nRaw: {responseJson}", ex);
        }
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return false;
        }

        try
        {
            var testRequest = new ChatCompletionRequest
            {
                Model = PrimaryModel,
                Messages = [ChatMessage.User("ping")],
                MaxTokens = 2,
                Temperature = 0.0
            };

            var resp = await ExecuteRequestAsync(testRequest, cancellationToken).ConfigureAwait(false);
            return resp.Choices.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<(bool Success, string Message)> ValidateApiKeyAsync(
        string apiKey,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        string trimmed = apiKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return (false, "La chiave API è vuota. Inserisci la chiave API ottenuta da Google AI Studio.");
        }

        string targetModel = !string.IsNullOrWhiteSpace(model) ? model.Trim() : DefaultPrimaryModel;

        try
        {
            using var client = new GeminiClient(trimmed, primaryModel: targetModel);
            var testRequest = new ChatCompletionRequest
            {
                Model = targetModel,
                Messages = [ChatMessage.User("ping")],
                MaxTokens = 2,
                Temperature = 0.0
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var resp = await client.ExecuteRequestAsync(testRequest, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            if (resp.Choices.Count > 0)
            {
                return (true, $"Chiave valida! Connessione a {targetModel} stabilita ({sw.ElapsedMilliseconds} ms).");
            }

            return (false, $"Risposta vuota o non valida ricevuta da Google Gemini per {targetModel}.");
        }
        catch (Exception ex)
        {
            string msg = ex.Message;
            if (msg.Contains("400") || msg.Contains("API_KEY_INVALID"))
            {
                msg = "Chiave API non riconosciuta da Google AI Studio (API_KEY_INVALID).";
            }
            else if (msg.Contains("429"))
            {
                msg = "Quota o rate limit raggiunto per il modello selezionato (429 Too Many Requests).";
            }
            return (false, $"Errore di verifica: {msg}");
        }
    }

    private static ChatCompletionRequest CloneRequestWithModel(ChatCompletionRequest original, string newModel)
    {
        return new ChatCompletionRequest
        {
            Model = newModel,
            Messages = original.Messages != null ? [.. original.Messages] : [],
            Tools = original.Tools != null ? [.. original.Tools] : null,
            ToolChoice = original.ToolChoice,
            Temperature = original.Temperature,
            TopP = original.TopP,
            MaxTokens = original.MaxTokens,
            Stream = original.Stream
        };
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
