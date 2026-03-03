using System.Net;
using System.Text;
using System.Text.Json;
using EGT.Contracts.Translation;
using EGT.Translators.Llm;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EGT.Tests;

public sealed class LlmTranslationProviderTests
{
  [Fact]
  public async Task TranslateBatchAsync_ShouldUseResponsesPayload_WhenEndpointIsResponses()
  {
    var handler = new SequenceHttpMessageHandler(
      CreateJsonResponse("""{"output_text":"{\"items\":[{\"id\":\"1\",\"text\":\"你好\"}]}"}"""));
    var sut = CreateSut(handler);
    var options = CreateOptions("https://api.openai.com/v1/responses");

    var result = await sut.TranslateBatchAsync(
      new[]
      {
        new TranslateItem
        {
          Id = "1",
          Source = "hello"
        }
      },
      options,
      CancellationToken.None);

    result.Errors.Should().BeEmpty();
    result.Items.Should().ContainSingle(x => x.Id == "1" && x.TranslatedText == "你好");
    handler.Requests.Should().ContainSingle();

    var request = handler.Requests.Single();
    request.Url.Should().EndWith("/v1/responses");
    using var requestJson = JsonDocument.Parse(request.Body);
    requestJson.RootElement.TryGetProperty("instructions", out _).Should().BeTrue();
    requestJson.RootElement.TryGetProperty("input", out _).Should().BeTrue();
    requestJson.RootElement.TryGetProperty("messages", out _).Should().BeFalse();
  }

  [Fact]
  public async Task TranslateBatchAsync_ShouldParseResponsesOutputContentArray()
  {
    var handler = new SequenceHttpMessageHandler(
      CreateJsonResponse(
        """{"output":[{"type":"message","content":[{"type":"output_text","text":"{\"items\":[{\"id\":\"A\",\"text\":\"世界\"}]}"}]}]}"""));
    var sut = CreateSut(handler);
    var options = CreateOptions("https://api.openai.com/v1/responses");

    var result = await sut.TranslateBatchAsync(
      new[]
      {
        new TranslateItem
        {
          Id = "A",
          Source = "world"
        }
      },
      options,
      CancellationToken.None);

    result.Errors.Should().BeEmpty();
    result.Items.Should().ContainSingle(x => x.Id == "A" && x.TranslatedText == "世界");
  }

  [Fact]
  public async Task TranslateBatchAsync_ShouldFallbackToSingle_WhenBatchContentIsNotJson()
  {
    var handler = new SequenceHttpMessageHandler(
      CreateJsonResponse("""{"output_text":"not-json"}"""),
      CreateJsonResponse("""{"output_text":"单条译文"}"""));
    var sut = CreateSut(handler);
    var options = CreateOptions("https://api.openai.com/v1/responses");

    var result = await sut.TranslateBatchAsync(
      new[]
      {
        new TranslateItem
        {
          Id = "X",
          Source = "single line"
        }
      },
      options,
      CancellationToken.None);

    result.Errors.Should().BeEmpty();
    result.Items.Should().ContainSingle(x => x.Id == "X" && x.TranslatedText == "单条译文");
    handler.Requests.Should().HaveCount(2);
    handler.Requests.All(x => x.Body.Contains("\"input\"", StringComparison.Ordinal)).Should().BeTrue();
  }

  private static TranslateOptions CreateOptions(string endpoint) => new()
  {
    ProviderApiKey = "test-key",
    ProviderEndpoint = endpoint,
    ProviderModel = "gpt-test",
    MaxConcurrency = 1,
    AiBatchSize = 8
  };

  private static HttpResponseMessage CreateJsonResponse(string json) => new(HttpStatusCode.OK)
  {
    Content = new StringContent(json, Encoding.UTF8, "application/json")
  };

  private static LlmTranslationProvider CreateSut(SequenceHttpMessageHandler handler)
  {
    var client = new HttpClient(handler);
    var factory = new FixedHttpClientFactory(client);
    return new LlmTranslationProvider(factory, NullLogger<LlmTranslationProvider>.Instance);
  }

  private sealed class FixedHttpClientFactory : IHttpClientFactory
  {
    private readonly HttpClient _client;

    public FixedHttpClientFactory(HttpClient client)
    {
      _client = client;
    }

    public HttpClient CreateClient(string name) => _client;
  }

  private sealed class SequenceHttpMessageHandler : HttpMessageHandler
  {
    private readonly Queue<HttpResponseMessage> _responses = new();

    public SequenceHttpMessageHandler(params HttpResponseMessage[] responses)
    {
      foreach (var response in responses)
      {
        _responses.Enqueue(response);
      }
    }

    public List<RequestSnapshot> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var body = request.Content is null
        ? string.Empty
        : await request.Content.ReadAsStringAsync(cancellationToken);

      Requests.Add(new RequestSnapshot(
        request.RequestUri?.ToString() ?? string.Empty,
        body));

      if (_responses.Count == 0)
      {
        throw new InvalidOperationException("No queued HTTP response.");
      }

      return _responses.Dequeue();
    }
  }

  private sealed record RequestSnapshot(string Url, string Body);
}
