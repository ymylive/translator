using System.Net;
using System.Text.Json;
using EGT.Contracts.Translation;
using Microsoft.Extensions.Logging;
using Polly;

namespace EGT.Translators.DeepL;

public sealed class DeepLTranslationProvider : ITranslationProvider
{
  private readonly IHttpClientFactory _httpClientFactory;
  private readonly ILogger<DeepLTranslationProvider> _logger;

  public DeepLTranslationProvider(IHttpClientFactory httpClientFactory, ILogger<DeepLTranslationProvider> logger)
  {
    _httpClientFactory = httpClientFactory;
    _logger = logger;
  }

  public string Name => "deepl";

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
      return FailAll(items, "missing_api_key", "DeepL API key is missing.");
    }

    var endpoint = string.IsNullOrWhiteSpace(options.ProviderEndpoint)
      ? "https://api-free.deepl.com/v2/translate"
      : options.ProviderEndpoint.TrimEnd('/');

    var policy = Policy
      .Handle<HttpRequestException>()
      .OrResult<HttpResponseMessage>(r =>
        r.StatusCode == HttpStatusCode.TooManyRequests || (int)r.StatusCode >= 500)
      .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
        onRetry: (response, delay, retryCount, _) =>
        {
          _logger.LogWarning("DeepL retry {RetryCount}, waiting {Delay}", retryCount, delay);
        });

    var data = new List<KeyValuePair<string, string>>();
    foreach (var item in items)
    {
      data.Add(new KeyValuePair<string, string>("text", item.Source));
    }

    data.Add(new KeyValuePair<string, string>("target_lang", MapTargetLang(options.TargetLang)));
    if (!string.Equals(options.SourceLang, "auto", StringComparison.OrdinalIgnoreCase))
    {
      data.Add(new KeyValuePair<string, string>("source_lang", MapSourceLang(options.SourceLang)));
    }

    var client = _httpClientFactory.CreateClient(Name);
    var response = await policy.ExecuteAsync(async token =>
    {
      using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
      request.Headers.Add("Authorization", $"DeepL-Auth-Key {options.ProviderApiKey}");
      request.Content = new FormUrlEncodedContent(data);
      return await client.SendAsync(request, token);
    }, ct);

    if (!response.IsSuccessStatusCode)
    {
      return FailAll(items, "provider_http_error", $"DeepL returned {(int)response.StatusCode}");
    }

    var content = await response.Content.ReadAsStringAsync(ct);
    using var json = JsonDocument.Parse(content);
    var translations = json.RootElement.GetProperty("translations");

    var results = new List<TranslatedItem>();
    for (var i = 0; i < items.Count; i++)
    {
      var text = i < translations.GetArrayLength()
        ? translations[i].GetProperty("text").GetString() ?? items[i].Source
        : items[i].Source;
      results.Add(new TranslatedItem { Id = items[i].Id, TranslatedText = text });
    }

    return new TranslateResult
    {
      Items = results,
      Errors = Array.Empty<ProviderError>()
    };
  }

  private static string MapTargetLang(string lang) =>
    string.Equals(lang, "zh-Hans", StringComparison.OrdinalIgnoreCase) ? "ZH" : lang.ToUpperInvariant();

  private static string MapSourceLang(string lang) =>
    string.Equals(lang, "zh-Hans", StringComparison.OrdinalIgnoreCase) ? "ZH" : lang.ToUpperInvariant();

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
