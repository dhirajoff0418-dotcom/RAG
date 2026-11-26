using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BookRag.Config;

namespace BookRag.Generation;

/// <summary>
/// Sends chat-completion requests to the Groq API.
///
/// The Groq API is OpenAI-compatible, so the same request/response format
/// is used.  See https://console.groq.com/docs/openai for the API spec.
///
/// HttpClient is static to avoid socket exhaustion.  The auth header is
/// set per-instance so each AppConfig can have a different key.
/// </summary>
public sealed class GroqClient : ILlmClient
{
    // Static HttpClient: reused across all requests to avoid socket exhaustion.
    // Headers that differ between instances (Authorization) are set on the
    // request message, not on the shared client.
    private static readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.groq.com/openai/v1/")
    };

    private readonly string _apiKey;
    private readonly string _model;
    private readonly double _temperature;

    public GroqClient(AppConfig config)
    {
        _apiKey      = config.GroqApiKey;
        _model       = config.GroqModel;
        _temperature = config.LlmTemperature;
    }

    /// <inheritdoc/>
    public async Task<string> ChatAsync(string systemPrompt, string userPrompt)
    {
        var body = new
        {
            model       = _model,
            temperature = _temperature,
            messages    = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   }
            }
        };

        // Build the request manually so we can attach the Authorization header
        // without mutating the shared HttpClient.
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Groq API error {(int)response.StatusCode} (model: {_model}): {raw}");

        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement
                  .GetProperty("choices")[0]
                  .GetProperty("message")
                  .GetProperty("content")
                  .GetString() ?? "";
    }
}
