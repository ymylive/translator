using EGT.Contracts.Models;
using EGT.Core.Pipeline;
using EGT.Profiles.GenericText;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EGT.Tests;

public sealed class PipelineE2ETests
{
  [Fact]
  public async Task RunAsync_ShouldGenerateOutputAndManifest()
  {
    var tempRoot = CreateTempDirectory();
    var gameRoot = CopyFixtureTo(tempRoot);
    var exePath = Path.Combine(gameRoot, "MyGame.exe");

    var pipeline = BuildPipeline();
    var options = new PipelineOptions
    {
      ProfileName = "generic-text",
      ProviderName = "mock",
      OutputRoot = Path.Combine(tempRoot, "EGT_Output"),
      BackupRoot = Path.Combine(tempRoot, "EGT_Backup"),
      CacheFilePath = Path.Combine(tempRoot, "EGT_Cache", "cache.db"),
      ApplyInPlace = false
    };

    var result = await pipeline.RunAsync(exePath, options, progress: null, CancellationToken.None);

    result.TotalItems.Should().BeGreaterThan(0);
    result.FailedItems.Should().Be(0);
    File.Exists(result.ManifestPath).Should().BeTrue();

    var translatedJson = Directory
      .EnumerateFiles(result.OutputRoot, "dialogue.json", SearchOption.AllDirectories)
      .Single();
    var translatedContent = await File.ReadAllTextAsync(translatedJson);
    translatedContent.Should().Contain("[ZH]Welcome, hero!");
    translatedContent.Should().Contain("[ZH]HP {0} / {1}");
  }

  [Fact]
  public async Task RestoreAsync_ShouldRecoverOriginalFiles_WhenApplyInPlaceEnabled()
  {
    var tempRoot = CreateTempDirectory();
    var gameRoot = CopyFixtureTo(tempRoot);
    var exePath = Path.Combine(gameRoot, "MyGame.exe");
    var targetFile = Path.Combine(gameRoot, "Localization", "dialogue.json");
    var original = await File.ReadAllTextAsync(targetFile);

    var pipeline = BuildPipeline();
    var options = new PipelineOptions
    {
      ProfileName = "generic-text",
      ProviderName = "mock",
      OutputRoot = Path.Combine(tempRoot, "EGT_Output"),
      BackupRoot = Path.Combine(tempRoot, "EGT_Backup"),
      CacheFilePath = Path.Combine(tempRoot, "EGT_Cache", "cache.db"),
      ApplyInPlace = true
    };

    var result = await pipeline.RunAsync(exePath, options, progress: null, CancellationToken.None);
    var changed = await File.ReadAllTextAsync(targetFile);
    changed.Should().Contain("[ZH]Welcome, hero!");

    await pipeline.RestoreAsync(result.ManifestPath, CancellationToken.None);
    var restored = await File.ReadAllTextAsync(targetFile);
    restored.Should().Be(original);
  }

  private static ITranslationPipeline BuildPipeline()
  {
    var services = new ServiceCollection();
    services.AddLogging(x => x.SetMinimumLevel(LogLevel.Warning));
    services.AddEgtCore();
    services.AddGenericTextProfile();
    var provider = services.BuildServiceProvider();
    return provider.GetRequiredService<ITranslationPipeline>();
  }

  private static string CopyFixtureTo(string tempRoot)
  {
    var fixtureRoot = ResolveFixtureRoot();
    var targetRoot = Path.Combine(tempRoot, "sample_game");
    CopyDirectory(fixtureRoot, targetRoot);
    return targetRoot;
  }

  private static string ResolveFixtureRoot()
  {
    var repoRoot = FindRepoRoot();
    var candidates = new[]
    {
      Path.Combine(repoRoot, "tests", "fixtures", "sample_game"),
      Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_game"),
      Path.Combine(repoRoot, "tests", "EGT.Tests", "Fixtures", "sample_game")
    };

    var found = candidates.FirstOrDefault(Directory.Exists);
    if (found is null)
    {
      throw new DirectoryNotFoundException(
        $"sample_game fixture not found. Checked:\n- {string.Join("\n- ", candidates)}");
    }

    return found;
  }

  private static string FindRepoRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "easy_game_translator.sln")))
      {
        return current.FullName;
      }

      current = current.Parent;
    }

    throw new DirectoryNotFoundException("Repository root containing easy_game_translator.sln was not found.");
  }

  private static string CreateTempDirectory()
  {
    var path = Path.Combine(Path.GetTempPath(), "egt-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
  }

  private static void CopyDirectory(string sourceDir, string destDir)
  {
    Directory.CreateDirectory(destDir);
    foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
    {
      var relative = Path.GetRelativePath(sourceDir, file);
      var dest = Path.Combine(destDir, relative);
      Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
      File.Copy(file, dest, overwrite: true);
    }
  }
}
