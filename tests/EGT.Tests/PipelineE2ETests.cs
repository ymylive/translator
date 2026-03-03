using EGT.Contracts.Models;
using EGT.Core.Pipeline;
using EGT.Profiles.GenericText;
using EGT.Profiles.RenPy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
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

  [Fact]
  public async Task RunAsync_ShouldSkipCsvMetadataAndIdentifierKeys()
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
    var translatedCsv = Directory
      .EnumerateFiles(result.OutputRoot, "ui.csv", SearchOption.AllDirectories)
      .Single();

    var csvContent = await File.ReadAllTextAsync(translatedCsv);
    csvContent.Should().Contain("\"id\",\"text\"");
    csvContent.Should().Contain("\"btn_start\",\"[ZH]Start Game\"");
    csvContent.Should().Contain("\"btn_exit\",\"[ZH]Exit\"");
    csvContent.Should().NotContain("[ZH]btn_start");
    csvContent.Should().NotContain("[ZH]btn_exit");
  }

  [Fact]
  public async Task RunAsync_ShouldReuseRelativeCacheAcrossDifferentWorkingDirectories()
  {
    var tempRoot = CreateTempDirectory();
    var gameRoot = CopyFixtureTo(tempRoot);
    var exePath = Path.Combine(gameRoot, "MyGame.exe");
    var pipeline = BuildPipeline();
    var originalCurrentDirectory = Environment.CurrentDirectory;

    var options = new PipelineOptions
    {
      ProfileName = "generic-text",
      ProviderName = "mock",
      OutputRoot = Path.Combine(tempRoot, "EGT_Output"),
      BackupRoot = Path.Combine(tempRoot, "EGT_Backup"),
      CacheFilePath = "EGT_Cache/resume.db",
      ApplyInPlace = false
    };

    try
    {
      Environment.CurrentDirectory = tempRoot;
      _ = await pipeline.RunAsync(exePath, options, progress: null, CancellationToken.None);

      Environment.CurrentDirectory = Path.GetPathRoot(tempRoot)!;
      var second = await pipeline.RunAsync(exePath, options, progress: null, CancellationToken.None);

      second.CacheFilePath.Should().NotBeNullOrWhiteSpace();
      second.CacheFilePath.Should().StartWith(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

      var reportPath = Path.Combine(second.OutputRoot, "report", "quality_report.json");
      File.Exists(reportPath).Should().BeTrue();
      using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
      var cacheHits = doc.RootElement.GetProperty("CacheHits").GetInt32();
      cacheHits.Should().BeGreaterThan(0);
    }
    finally
    {
      Environment.CurrentDirectory = originalCurrentDirectory;
    }
  }

  [Fact]
  public async Task RunAsync_WithRenPyProfile_ShouldTranslateDialogueOnly()
  {
    var tempRoot = CreateTempDirectory();
    var gameRoot = Path.Combine(tempRoot, "renpy_game");
    Directory.CreateDirectory(Path.Combine(gameRoot, "game"));
    await File.WriteAllTextAsync(Path.Combine(gameRoot, "MyGame.exe"), string.Empty);
    var scriptPath = Path.Combine(gameRoot, "game", "script.rpy");
    var script =
      """
      label start:
          scene bg room
          e "Hello traveler."
          "Narrator line."
          menu:
              "Go left":
                  jump left_path
          $ persistent.gallery_variables['dragotalk'] = True
          image eileen happy = "eileen_happy.webp"
      """;
    await File.WriteAllTextAsync(scriptPath, script);

    var pipeline = BuildPipeline();
    var options = new PipelineOptions
    {
      ProfileName = "renpy",
      ProviderName = "mock",
      OutputRoot = Path.Combine(tempRoot, "EGT_Output"),
      BackupRoot = Path.Combine(tempRoot, "EGT_Backup"),
      CacheFilePath = Path.Combine(tempRoot, "EGT_Cache", "cache.db"),
      ApplyInPlace = false
    };

    var result = await pipeline.RunAsync(Path.Combine(gameRoot, "MyGame.exe"), options, progress: null, CancellationToken.None);
    result.TotalItems.Should().BeGreaterThan(0);

    var translatedScript = Directory
      .EnumerateFiles(result.OutputRoot, "script.rpy", SearchOption.AllDirectories)
      .Single();
    var content = await File.ReadAllTextAsync(translatedScript);
    content.Should().Contain("[ZH]Hello traveler.");
    content.Should().Contain("[ZH]Narrator line.");
    content.Should().Contain("[ZH]Go left");
    content.Should().NotContain("[ZH]dragotalk");
    content.Should().NotContain("[ZH]eileen_happy.webp");
  }

  private static ITranslationPipeline BuildPipeline()
  {
    var services = new ServiceCollection();
    services.AddLogging(x => x.SetMinimumLevel(LogLevel.Warning));
    services.AddEgtCore();
    services.AddRenPyProfile();
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
