using EGT.Core.Translation;
using FluentAssertions;
using Xunit;

namespace EGT.Tests;

public sealed class PlaceholderProtectorTests
{
  [Fact]
  public void ProtectAndRestore_ShouldKeepPlaceholdersUntouched()
  {
    var protector = new PlaceholderProtector();
    const string source = "HP {0} and %s <color=#fff>\\n";

    var (protectedText, map) = protector.Protect(source);
    protectedText.Should().Contain("__PH_0__");
    protectedText.Should().Contain("__PH_1__");

    var translated = "[ZH]" + protectedText;
    var restored = protector.Restore(translated, map);
    restored.Should().Be("[ZH]HP {0} and %s <color=#fff>\\n");
  }
}
