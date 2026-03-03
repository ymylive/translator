using System.Net;
using System.Text;
using System.Text.Json;
using EGT.Contracts.Translation;
using Microsoft.Extensions.Logging;
using Polly;

namespace EGT.Translators.Microsoft;

public sealed class MicrosoftTranslationProvider : ITranslationProvider
{
  private readonly IHttpClientFactory _httpClientFactory;
  private readonly ILogger<MicrosoftTranslationProvider> _logger;

  public MicrosoftTranslationProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<MicrosoftTranslationProvider> logger)
  {
    _httpClientFactory = httpClientFactory;
    _logger = logger;
  }

  public string Name => "microsoft";

  public async Task<TranslateResult> TranslateBatchAsync(
    IReadOnlyList<TranslateItem> items,
    TranslateOptions options,
    CancellationToken ct)
  {
    if (items.Count == 0)
    {
      return Empty();
    }

    if (string.IsNullOrWhiteSpace(options.ProviderApiKey))
    {
      return FailAll(items, "missing_api_key", "Microsoft Translator API key is missing.");
    }

    var endpoint = string.IsNullOrWhiteSpace(options.ProviderEndpoint)
      ? "https://api.cognitive.microsofttranslator.com"
      : options.ProviderEndpoint.TrimEnd('/');

    var sourceLang = string.Equals(options.SourceLang, "auto", StringComparison.OrdinalIgnoreCase)
      ? null
      : options.SourceLang;

    var uri = sourceLang is null
      ? $"{endpoint}/translate?api-version=3.0&to={Uri.EscapeDataString(options.TargetLang)}"
      : $"{endpoint}/translate?api-version=3.0&from={Uri.EscapeDataString(sourceLang)}&to={Uri.EscapeDataString(options.TargetLang)}";

    var body = JsonSerializer.Serialize(items.Select(x => new { Text = x.Source }));

    var policy = Policy
      .Handle<HttpRequestException>()
      .OrResult<HttpResponseMessage>(r =>
        r.StatusCode == HttpStatusCode.TooManyRequests || (int)r.StatusCode >= 500)
      .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
        onRetry: (_, delay, retryCount, _) =>
        {
          _logger.LogWarning("Microsoft translator retry {RetryCount}, waiting {Delay}", retryCount, delay);
        });

    var client = _httpClientFactory.CreateClient(Name);
    var response = await policy.ExecuteAsync(async token =>
    {
      using var request = new HttpRequestMessage(HttpMethod.Post, uri);
      request.Headers.Add("Ocp-Apim-Subscription-Key", options.ProviderApiKey);
      if (!string.IsNullOrWhiteSpace(options.ProviderRegion))
      {
        request.Headers.Add("Ocp-Apim-Subscription-Region", options.ProviderRegion);
      }

      request.Headers.Add("X-ClientTraceId", Guid.NewGuid().ToString());
      request.Content = new StringContent(body, Encoding.UTF8, "application/json");
      return await client.SendAsync(request, token);
    }, ct);

    if (!response.IsSuccessStatusCode)
    {
      return FailAll(items, "provider_http_error", $"Microsoft returned {(int)response.StatusCode}");
    }

    var content = await response.Content.ReadAsStringAsync(ct);
    using var doc = JsonDocument.Parse(content);
    var root = doc.RootElement;

    var translated = new List<TranslatedItem>();
    for (var i = 0; i < items.Count; i++)
    {
      var text = items[i].Source;
      if (i < root.GetArrayLength() &&
          root[i].TryGetProperty("translations", out var transArray) &&
          transArray.GetArrayLength() > 0)
      {
        text = transArray[0].GetProperty("text").GetString() ?? text;
      }

      translated.Add(new TranslatedItem { Id = items[i].Id, TranslatedText = text });
    }

    return new TranslateResult
    {
      Items = translated,
      Errors = Array.Empty<ProviderError>()
    };
  }

  private static TranslateResult Empty() => new()
  {
    Items = Array.Empty<TranslatedItem>(),
    Errors = Array.Empty<ProviderError>()
  };

  private static TranslateResult FailAll(IReadOnlyList<TranslateItem> items, string code, string message) =>
    new()
    {
      Items = Array.Empty<TranslatedItem>(),
      Errors = items.Select(x => new ProviderError
      {
        Id = x.Id,
        Code = code,
        Message = message,
        IsTransient = false
      }).ToList()
    };
}

