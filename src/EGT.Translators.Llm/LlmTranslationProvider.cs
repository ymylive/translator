using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EGT.Contracts.Translation;
using Microsoft.Extensions.Logging;
using Polly;

namespace EGT.Translators.Llm;

public sealed class LlmTranslationProvider : ITranslationProvider
{
  private readonly IHttpClientFactory _httpClientFactory;
  private readonly ILogger<LlmTranslationProvider> _logger;

  public LlmTranslationProvider(IHttpClientFactory httpClientFactory, ILogger<LlmTranslationProvider> logger)
  {
    _httpClientFactory = httpClientFactory;
    _logger = logger;
  }

  public string Name => "llm";

  public async Task<TranslateResult> TranslateBatchAsync(
    IReadOnlyList<TranslateItem> items,
    TranslateOptions options,
    CancellationToken ct)
  {
    if (items.Count == 0)
    {
      return Empty();
    }

    var endpoint = NormalizeEndpoint(options.ProviderEndpoint);
    var requireKey = !endpoint.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) &&
                     !endpoint.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase);

    if (requireKey && string.IsNullOrWhiteSpace(options.ProviderApiKey))
    {
      return FailAll(items, "missing_api_key", "LLM API key is missing.");
    }

    var model = string.IsNullOrWhiteSpace(options.ProviderModel) ? "gpt-4o-mini" : options.ProviderModel;
    var chunkSize = Math.Max(1, options.AiBatchSize);
    var maxConcurrency = Math.Max(1, options.MaxConcurrency);
    var chunks = items.Chunk(chunkSize).ToList();

    var semaphore = new SemaphoreSlim(maxConcurrency);
    var translated = new List<TranslatedItem>();
    var errors = new List<ProviderError>();
    var tasks = chunks.Select(async chunk =>
    {
      await semaphore.WaitAsync(ct);
      try
      {
        var chunkItems = chunk.ToList();
        var chunkResult = await TranslateChunkAsync(chunkItems, options, endpoint, model, ct);

        lock (translated)
        {
          translated.AddRange(chunkResult.Items);
        }

        lock (errors)
        {
          errors.AddRange(chunkResult.Errors);
        }
      }
      finally
      {
        semaphore.Release();
      }
    });

    await Task.WhenAll(tasks);
    return new TranslateResult
    {
      Items = translated,
      Errors = errors
    };
  }

  private async Task<TranslateResult> TranslateChunkAsync(
    IReadOnlyList<TranslateItem> chunk,
    TranslateOptions options,
    string endpoint,
    string model,
    CancellationToken ct)
  {
    var useResponsesProtocol = IsResponsesEndpoint(endpoint);
    var policy = Policy
      .Handle<HttpRequestException>()
      .OrResult<HttpResponseMessage>(r =>
        r.StatusCode == HttpStatusCode.TooManyRequests || (int)r.StatusCode >= 500)
      .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(500 * Math.Pow(2, retryAttempt)),
        onRetry: (_, delay, retryCount, _) =>
        {
          _logger.LogWarning("LLM retry {RetryCount}, waiting {Delay}", retryCount, delay);
        });

    var payload = BuildBatchPayload(chunk, options, model, useResponsesProtocol, endpoint);
    var response = await SendAsync(policy, endpoint, options.ProviderApiKey, payload, ct);
    if (!response.IsSuccessStatusCode)
    {
      return await FallbackToSingleAsync(
        chunk,
        options,
        endpoint,
        model,
        useResponsesProtocol,
        $"LLM returned {(int)response.StatusCode}",
        ct);
    }

    var body = await response.Content.ReadAsStringAsync(ct);
    var content = ExtractChatContent(body);
    if (string.IsNullOrWhiteSpace(content))
    {
      return await FallbackToSingleAsync(
        chunk,
        options,
        endpoint,
        model,
        useResponsesProtocol,
        "LLM response content is empty.",
        ct);
    }

    if (!TryParseBatchResponse(content, out var translatedById))
    {
      return await FallbackToSingleAsync(
        chunk,
        options,
        endpoint,
        model,
        useResponsesProtocol,
        "LLM batch parse failed.",
        ct);
    }

    var translated = new List<TranslatedItem>();
    var errors = new List<ProviderError>();
    var unresolved = new List<TranslateItem>();
    foreach (var item in chunk)
    {
      if (translatedById.TryGetValue(item.Id, out var text) && !string.IsNullOrWhiteSpace(text))
      {
        translated.Add(new TranslatedItem
        {
          Id = item.Id,
          TranslatedText = text
        });
      }
      else
      {
        unresolved.Add(item);
      }
    }

    if (unresolved.Count > 0)
    {
      var singleResult = await FallbackToSingleAsync(
        unresolved,
        options,
        endpoint,
        model,
        useResponsesProtocol,
        "missing_items_in_batch",
        ct);

      translated.AddRange(singleResult.Items);
      var singleErrorsById = singleResult.Errors.ToDictionary(x => x.Id, StringComparer.Ordinal);
      var recoveredIds = singleResult.Items.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

      foreach (var item in unresolved)
      {
        if (recoveredIds.Contains(item.Id))
        {
          continue;
        }

        if (singleErrorsById.TryGetValue(item.Id, out var singleError))
        {
          errors.Add(singleError);
          continue;
        }

        errors.Add(new ProviderError
        {
          Id = item.Id,
          Code = "missing_item",
          Message = "Missing translated item in LLM batch response.",
          IsTransient = true
        });
      }
    }

    return new TranslateResult
    {
      Items = translated,
      Errors = errors
    };
  }

  private async Task<TranslateResult> FallbackToSingleAsync(
    IReadOnlyList<TranslateItem> chunk,
    TranslateOptions options,
    string endpoint,
    string model,
    bool useResponsesProtocol,
    string reason,
    CancellationToken ct)
  {
    _logger.LogWarning("LLM batch fallback to single-item mode: {Reason}", reason);
    var translated = new List<TranslatedItem>();
    var errors = new List<ProviderError>();

    foreach (var item in chunk)
    {
      var singlePayload = BuildSinglePayload(item, options, model, useResponsesProtocol, endpoint);
      var policy = Policy
        .Handle<HttpRequestException>()
        .OrResult<HttpResponseMessage>(r =>
          r.StatusCode == HttpStatusCode.TooManyRequests || (int)r.StatusCode >= 500)
        .WaitAndRetryAsync(2, retry => TimeSpan.FromMilliseconds(300 * Math.Pow(2, retry)));

      var response = await SendAsync(policy, endpoint, options.ProviderApiKey, singlePayload, ct);
      if (!response.IsSuccessStatusCode)
      {
        errors.Add(new ProviderError
        {
          Id = item.Id,
          Code = "provider_http_error",
          Message = $"LLM returned {(int)response.StatusCode}",
          IsTransient = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests
        });
        continue;
      }

      var body = await response.Content.ReadAsStringAsync(ct);
      var content = ExtractChatContent(body);
      if (string.IsNullOrWhiteSpace(content))
      {
        errors.Add(new ProviderError
        {
          Id = item.Id,
          Code = "empty_content",
          Message = "LLM returned empty content.",
          IsTransient = true
        });
        continue;
      }

      translated.Add(new TranslatedItem
      {
        Id = item.Id,
        TranslatedText = content
      });
    }

    return new TranslateResult
    {
      Items = translated,
      Errors = errors
    };
  }

  private async Task<HttpResponseMessage> SendAsync(
    IAsyncPolicy<HttpResponseMessage> policy,
    string endpoint,
    string? apiKey,
    string payload,
    CancellationToken ct)
  {
    var client = _httpClientFactory.CreateClient(Name);
    return await policy.ExecuteAsync(async token =>
    {
      using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
      if (!string.IsNullOrWhiteSpace(apiKey))
      {
        req.Headers.Add("Authorization", $"Bearer {apiKey}");
      }

      req.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(payload));
      req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
      return await client.SendAsync(req, token);
    }, ct);
  }

  private static string BuildBatchPayload(
    IReadOnlyList<TranslateItem> chunk,
    TranslateOptions options,
    string model,
    bool useResponsesProtocol,
    string endpoint)
  {
    var input = string.Join(
      "\n",
      chunk.Select(x => $"ID={x.Id}\nTEXT={x.Source}\n---"));

    var systemPrompt =
      "You are a game localization translator. Translate each item into target language while preserving placeholders like __PH_n__, {0}, %s, <...>, \\n. Return only minified JSON object: {\"items\":[{\"id\":\"...\",\"text\":\"...\"}]}. No markdown.";
    var userPrompt = $"Source language: {options.SourceLang}\nTarget language: {options.TargetLang}\nItems:\n{input}";

    if (useResponsesProtocol)
    {
      var payload = new Dictionary<string, object?>
      {
        ["model"] = model,
        ["input"] = BuildResponsesInput(systemPrompt, userPrompt)
      };

      if (!ShouldOmitResponsesTemperature(endpoint))
      {
        payload["temperature"] = 0;
      }

      return JsonSerializer.Serialize(payload);
    }

    return JsonSerializer.Serialize(new
    {
      model,
      temperature = 0,
      messages = new object[]
      {
        new
        {
          role = "system",
          content = systemPrompt
        },
        new
        {
          role = "user",
          content = userPrompt
        }
      }
    });
  }

  private static string BuildSinglePayload(
    TranslateItem item,
    TranslateOptions options,
    string model,
    bool useResponsesProtocol,
    string endpoint)
  {
    var systemPrompt =
      "You are a game localization translator. Keep placeholders like __PH_0__, {0}, %s, <...>, \\n unchanged. Return only the translated text.";
    var userPrompt = $"Source language: {options.SourceLang}\nTarget language: {options.TargetLang}\nText:\n{item.Source}";

    if (useResponsesProtocol)
    {
      var payload = new Dictionary<string, object?>
      {
        ["model"] = model,
        ["input"] = BuildResponsesInput(systemPrompt, userPrompt)
      };

      if (!ShouldOmitResponsesTemperature(endpoint))
      {
        payload["temperature"] = 0;
      }

      return JsonSerializer.Serialize(payload);
    }

    return JsonSerializer.Serialize(new
    {
      model,
      temperature = 0,
      messages = new object[]
      {
        new
        {
          role = "system",
          content = systemPrompt
        },
        new
        {
          role = "user",
          content = userPrompt
        }
      }
    });
  }

  private static bool TryParseBatchResponse(string content, out Dictionary<string, string> translatedById)
  {
    translatedById = new Dictionary<string, string>(StringComparer.Ordinal);
    try
    {
      var cleaned = ExtractJsonObject(content);
      if (string.IsNullOrWhiteSpace(cleaned))
      {
        return false;
      }

      using var doc = JsonDocument.Parse(cleaned);
      if (doc.RootElement.TryGetProperty("items", out var itemsNode))
      {
        foreach (var node in itemsNode.EnumerateArray())
        {
          if (!node.TryGetProperty("id", out var idNode))
          {
            continue;
          }

          var textNode = node.TryGetProperty("text", out var directText)
            ? directText
            : (node.TryGetProperty("translation", out var translationText) ? translationText : default);

          var id = idNode.GetString();
          var text = textNode.ValueKind == JsonValueKind.String ? textNode.GetString() : null;
          if (!string.IsNullOrWhiteSpace(id) && text is not null)
          {
            translatedById[id] = text;
          }
        }

        return translatedById.Count > 0;
      }

      if (doc.RootElement.ValueKind == JsonValueKind.Object)
      {
        foreach (var property in doc.RootElement.EnumerateObject())
        {
          if (property.Value.ValueKind == JsonValueKind.String)
          {
            translatedById[property.Name] = property.Value.GetString() ?? string.Empty;
          }
        }

        return translatedById.Count > 0;
      }
    }
    catch
    {
      return false;
    }

    return false;
  }

  private static string NormalizeEndpoint(string? endpoint)
  {
    var value = string.IsNullOrWhiteSpace(endpoint) ? "https://api.openai.com" : endpoint.Trim();
    if (value.EndsWith("/v1/responses", StringComparison.OrdinalIgnoreCase))
    {
      return value;
    }

    if (value.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
    {
      return value;
    }

    if (value.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
    {
      return value;
    }

    if (value.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
    {
      return $"{value}/chat/completions";
    }

    return $"{value.TrimEnd('/')}/v1/chat/completions";
  }

  private static bool IsResponsesEndpoint(string endpoint)
  {
    if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
    {
      return uri.AbsolutePath.EndsWith("/responses", StringComparison.OrdinalIgnoreCase);
    }

    return endpoint.Contains("/responses", StringComparison.OrdinalIgnoreCase);
  }

  private static IReadOnlyList<ResponsesInputItem> BuildResponsesInput(string systemPrompt, string userPrompt)
  {
    var items = new List<ResponsesInputItem>();

    if (!string.IsNullOrWhiteSpace(systemPrompt))
    {
      items.Add(new ResponsesInputItem
      {
        Role = "system",
        Content = new[]
        {
          new ResponsesInputContent
          {
            Type = "input_text",
            Text = systemPrompt
          }
        }
      });
    }

    items.Add(new ResponsesInputItem
    {
      Role = "user",
      Content = new[]
      {
        new ResponsesInputContent
        {
          Type = "input_text",
          Text = userPrompt
        }
      }
    });

    return items;
  }

  private static bool ShouldOmitResponsesTemperature(string endpoint)
  {
    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
    {
      return false;
    }

    return string.Equals(uri.Host, "gmn.chuangzuoli.com", StringComparison.OrdinalIgnoreCase);
  }

  private static string? ExtractChatContent(string json)
  {
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    if (root.TryGetProperty("output_text", out var outputText) &&
        outputText.ValueKind == JsonValueKind.String)
    {
      return outputText.GetString()?.Trim();
    }

    if (root.TryGetProperty("output_text", out outputText) &&
        outputText.ValueKind == JsonValueKind.Array)
    {
      var merged = string.Concat(outputText.EnumerateArray()
        .Where(x => x.ValueKind == JsonValueKind.String)
        .Select(x => x.GetString()));
      if (!string.IsNullOrWhiteSpace(merged))
      {
        return merged.Trim();
      }
    }

    if (root.TryGetProperty("output", out var outputNode) && outputNode.ValueKind == JsonValueKind.Array)
    {
      var responseContent = ExtractFromResponsesOutput(outputNode);
      if (!string.IsNullOrWhiteSpace(responseContent))
      {
        return responseContent;
      }
    }

    if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
    {
      return null;
    }

    if (choices[0].TryGetProperty("message", out var messageNode))
    {
      if (messageNode.TryGetProperty("content", out var contentNode))
      {
        if (contentNode.ValueKind == JsonValueKind.String)
        {
          return contentNode.GetString()?.Trim();
        }

        if (contentNode.ValueKind == JsonValueKind.Array)
        {
          var text = ExtractFromContentArray(contentNode);
          if (!string.IsNullOrWhiteSpace(text))
          {
            return text;
          }
        }
      }

      if (messageNode.TryGetProperty("reasoning_content", out var reasoningNode) &&
          reasoningNode.ValueKind == JsonValueKind.String)
      {
        return reasoningNode.GetString()?.Trim();
      }
    }

    if (choices[0].TryGetProperty("delta", out var deltaNode))
    {
      if (deltaNode.TryGetProperty("content", out var deltaContentNode) &&
          deltaContentNode.ValueKind == JsonValueKind.String)
      {
        return deltaContentNode.GetString()?.Trim();
      }

      if (deltaNode.TryGetProperty("content", out deltaContentNode) &&
          deltaContentNode.ValueKind == JsonValueKind.Array)
      {
        var text = ExtractFromContentArray(deltaContentNode);
        if (!string.IsNullOrWhiteSpace(text))
        {
          return text;
        }
      }
    }

    return null;
  }

  private static string? ExtractFromResponsesOutput(JsonElement outputArray)
  {
    foreach (var entry in outputArray.EnumerateArray())
    {
      if (!entry.TryGetProperty("content", out var contentArray) || contentArray.ValueKind != JsonValueKind.Array)
      {
        continue;
      }

      var text = ExtractFromContentArray(contentArray);
      if (!string.IsNullOrWhiteSpace(text))
      {
        return text;
      }
    }

    return null;
  }

  private static string? ExtractFromContentArray(JsonElement contentArray)
  {
    var builder = new StringBuilder();
    foreach (var part in contentArray.EnumerateArray())
    {
      if (part.TryGetProperty("text", out var textNode) && textNode.ValueKind == JsonValueKind.String)
      {
        builder.Append(textNode.GetString());
      }
      else if (part.TryGetProperty("content", out var contentNode) && contentNode.ValueKind == JsonValueKind.String)
      {
        builder.Append(contentNode.GetString());
      }
    }

    var result = builder.ToString().Trim();
    return string.IsNullOrWhiteSpace(result) ? null : result;
  }

  private static string? ExtractJsonObject(string text)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return null;
    }

    var trimmed = text.Trim();
    if (trimmed.StartsWith("```", StringComparison.Ordinal))
    {
      var lines = trimmed.Split('\n').ToList();
      if (lines.Count >= 3)
      {
        lines.RemoveAt(0);
        if (lines[^1].Trim().StartsWith("```", StringComparison.Ordinal))
        {
          lines.RemoveAt(lines.Count - 1);
        }

        trimmed = string.Join('\n', lines).Trim();
      }
    }

    var start = trimmed.IndexOf('{');
    var end = trimmed.LastIndexOf('}');
    if (start >= 0 && end > start)
    {
      return trimmed.Substring(start, end - start + 1);
    }

    return trimmed;
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

  private sealed class ResponsesInputItem
  {
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required IReadOnlyList<ResponsesInputContent> Content { get; init; }
  }

  private sealed class ResponsesInputContent
  {
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }
  }
}
