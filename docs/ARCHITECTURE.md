# EGT Architecture

## 1. Module Layout

```text
EGT.App                      # Avalonia desktop UI
EGT.Cli                      # Automation/debug CLI
EGT.Core                     # Shared translation pipeline
EGT.Contracts                # Interfaces + DTOs
EGT.Profiles.GenericText     # Generic extractor/apply profile
EGT.Translators.DeepL        # DeepL provider
EGT.Translators.Microsoft    # Microsoft provider
EGT.Translators.Llm          # OpenAI-compatible provider
EGT.Tests                    # Unit + integration/e2e tests
```

## 2. Data Flow

```text
Drag EXE / CLI path
  -> GameProjectResolver
  -> ProfileSelector (auto or specified)
  -> Profile.ExtractAsync
  -> PlaceholderProtector + Glossary + Cache lookup
  -> TranslationProvider.TranslateBatchAsync
  -> Profile.ApplyAsync
  -> ManifestWriter (output + optional apply + backup)
  -> manifest.json
  -> optional RestoreService
```

## 3. Plugin Mechanism

1. Drop plugin DLLs into `Plugins/`.
2. `PluginLoader` scans assemblies on startup.
3. Any exported type implementing:
   `IProfile` or `ITranslationProvider` is registered into DI.
4. Plugin contracts are isolated in `EGT.Contracts`.
5. Profile capability metadata (`supportedExtensions`, `engineHints`, `priority`) drives auto-selection.

## 4. Contracts

### Translation

1. `ITranslationProvider`
   - `Name`
   - `TranslateBatchAsync(IReadOnlyList<TranslateItem>, TranslateOptions, CancellationToken)`
2. `TranslateItem`
   - `Id`
   - `Source`
   - `Context`
   - `Placeholders`
3. `TranslateOptions`
   - `SourceLang`
   - `TargetLang`
   - `Glossary`
   - `MaxConcurrency`
   - `PreserveFormatting`
   - `ProviderApiKey/Endpoint/Model/Region`
4. `TranslateResult`
   - `Items`
   - `Errors`

### Profile

1. `IProfile`
   - `Supports(GameProject)`
   - `ExtractAsync(...)`
   - `ApplyAsync(...)`
2. `ProfileExtractionResult`
   - `Files`
   - `Entries`
3. `ProfileApplyResult`
   - `Files`

### Pipeline

1. `ITranslationPipeline`
   - `RunAsync(exePath, options, progress, ct)`
   - `RestoreAsync(manifestPath, ct)`

## 5. Manifest JSON Schema (Simplified)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "EGT Translation Manifest",
  "type": "object",
  "required": ["manifestVersion", "createdAtUtc", "project", "run", "statistics", "files", "restore"],
  "properties": {
    "manifestVersion": { "type": "string" },
    "createdAtUtc": { "type": "string", "format": "date-time" },
    "project": {
      "type": "object",
      "required": ["gameName", "exePath", "rootPath", "profile", "provider", "optionsHash"],
      "properties": {
        "gameName": { "type": "string" },
        "exePath": { "type": "string" },
        "rootPath": { "type": "string" },
        "profile": { "type": "string" },
        "provider": { "type": "string" },
        "optionsHash": { "type": "string" }
      }
    },
    "run": {
      "type": "object",
      "required": ["outputRoot", "backupRoot", "runId", "appliedInPlace"],
      "properties": {
        "outputRoot": { "type": "string" },
        "backupRoot": { "type": "string" },
        "runId": { "type": "string" },
        "appliedInPlace": { "type": "boolean" }
      }
    },
    "statistics": {
      "type": "object",
      "required": ["totalEntries", "succeeded", "failed", "skipped"],
      "properties": {
        "totalEntries": { "type": "integer" },
        "succeeded": { "type": "integer" },
        "failed": { "type": "integer" },
        "skipped": { "type": "integer" }
      }
    },
    "files": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["originalPath", "outputPath", "originalSha256", "outputSha256", "encoding"],
        "properties": {
          "originalPath": { "type": "string" },
          "outputPath": { "type": "string" },
          "appliedPath": { "type": ["string", "null"] },
          "backupPath": { "type": ["string", "null"] },
          "originalSha256": { "type": "string" },
          "outputSha256": { "type": "string" },
          "encoding": { "type": "string" }
        }
      }
    },
    "restore": {
      "type": "object",
      "required": ["canRestore", "items"],
      "properties": {
        "canRestore": { "type": "boolean" },
        "items": {
          "type": "array",
          "items": {
            "type": "object",
            "required": ["targetPath", "backupPath", "backupSha256"],
            "properties": {
              "targetPath": { "type": "string" },
              "backupPath": { "type": "string" },
              "backupSha256": { "type": "string" }
            }
          }
        }
      }
    }
  }
}
```

## 6. Manifest Example

```json
{
  "manifestVersion": "1.0.0",
  "createdAtUtc": "2026-02-23T12:00:01.1234567+00:00",
  "project": {
    "gameName": "MyGame",
    "exePath": "D:\\Games\\MyGame\\MyGame.exe",
    "rootPath": "D:\\Games\\MyGame",
    "profile": "generic-text",
    "provider": "mock",
    "optionsHash": "7f6d0a..."
  },
  "run": {
    "outputRoot": "D:\\workspace\\EGT_Output\\MyGame\\20260223_120001",
    "backupRoot": "D:\\workspace\\EGT_Backup\\20260223_120001",
    "runId": "20260223_120001",
    "appliedInPlace": true
  },
  "statistics": {
    "totalEntries": 42,
    "succeeded": 41,
    "failed": 1,
    "skipped": 0
  },
  "files": [
    {
      "originalPath": "D:\\Games\\MyGame\\Localization\\dialogue.json",
      "outputPath": "D:\\workspace\\EGT_Output\\MyGame\\20260223_120001\\Localization\\dialogue.json",
      "appliedPath": "D:\\Games\\MyGame\\Localization\\dialogue.json",
      "backupPath": "D:\\workspace\\EGT_Backup\\20260223_120001\\Localization\\dialogue.json",
      "originalSha256": "5ca8...",
      "outputSha256": "88dd...",
      "encoding": "utf-8"
    }
  ],
  "restore": {
    "canRestore": true,
    "items": [
      {
        "targetPath": "D:\\Games\\MyGame\\Localization\\dialogue.json",
        "backupPath": "D:\\workspace\\EGT_Backup\\20260223_120001\\Localization\\dialogue.json",
        "backupSha256": "5ca8..."
      }
    ]
  }
}
```

