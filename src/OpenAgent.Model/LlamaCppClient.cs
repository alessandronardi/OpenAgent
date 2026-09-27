using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Model.Contracts;

namespace OpenAgent.Model;

public class LlamaCppClient : IModelProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public string BaseUrl => _baseUrl;

    public LlamaCppClient(string baseUrl = "http://127.0.0.1:8080", HttpClient? httpClient = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            _ownsHttpClient = true;
        }
    }

    public async Task<ChatCompletionResponse> GenerateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        string url = $"{_baseUrl}/v1/chat/completions";
        string jsonRequest = JsonSerializer.Serialize(request, JsonOptions);

        using var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Impossibile connettersi a llama-server su {url}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timeout durante la chiamata di inferenza a llama-server ({url}).", ex);
        }

        string jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"llama-server ha restituito errore {(int)response.StatusCode} ({response.ReasonPhrase}): {jsonResponse}");
        }

        var completionResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(jsonResponse, JsonOptions);
        if (completionResponse == null)
        {
            throw new InvalidOperationException($"Risposta non valida o vuota ricevuta da llama-server: {jsonResponse}");
        }

        return completionResponse;
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string url = $"{_baseUrl}/health";
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;

            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("status", out var statusProp))
            {
                string? status = statusProp.GetString();
                return string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
