# easy_game_translator (EGT)

Windows-first desktop translator for game text assets.

## Product Positioning

EGT is a desktop tool for players who want local game translation with minimal setup:

1. Drag a game `.exe` into EGT.
2. Select translation engine (`DeepL`, `Microsoft`, `LLM`, or `mock`).
3. Extract text, translate, and generate reversible output.
4. Optionally apply to original game folder with mandatory backup.

For advanced users, EGT also supports logs, glossary import, cache, CLI automation, and plugin extension.

## MVP Features

1. EXE drag-and-drop or manual path input.
2. Profiles: `renpy` (Ren'Py script focused) + `generic-text` (`.json .csv .tsv .ini .xml .yaml .yml .txt .strings`), auto-selected by default.
3. Translation providers: DeepL / Microsoft Translator / OpenAI-compatible LLM.
4. GUI AI priority routing: configurable priority-1/priority-2 presets (`openai-responses`, `openrouter-responses`, `modelscope-chat`, etc.).
5. Placeholder protection (`{0}`, `%s`, `<...>`, `\n`) and restoration.
6. Translation cache (SQLite) and glossary CSV.
7. Output to `./EGT_Output/<game>/<timestamp>/` by default.
8. Optional in-place apply with mandatory backup to `./EGT_Backup/<timestamp>/`.
9. Manifest generation for rollback and reproducibility.
10. CLI + Desktop UI based on the same core pipeline.
11. Quality report export (`quality_report.json`, `translation_preview.csv`, `failed_items.csv` when failures exist).

## Screenshots

- `[TODO] docs/assets/screenshot-main-window.png`
- `[TODO] docs/assets/screenshot-progress-log.png`

## Quick Start

### Prerequisites

1. .NET 8 SDK
2. Windows 10/11 (MVP target)

### Build

```bash
dotnet restore easy_game_translator.sln
dotnet build easy_game_translator.sln -c Debug
```

### Run CLI

```bash
dotnet run --project src/EGT.Cli -- --help
```

### Run Desktop App

```bash
dotnet run --project src/EGT.App
```

## CLI Examples

```bash
# Basic run with mock provider
dotnet run --project src/EGT.Cli -- run --exe "D:\Games\MyGame\MyGame.exe" --provider mock

# DeepL run (output only)
dotnet run --project src/EGT.Cli -- run --exe "D:\Games\MyGame\MyGame.exe" --provider deepl --api-key "<DEEPL_KEY>"

# Microsoft run with region
dotnet run --project src/EGT.Cli -- run --exe "D:\Games\MyGame\MyGame.exe" --provider microsoft --api-key "<AZURE_KEY>" --region "eastasia"

# OpenAI-compatible LLM run
dotnet run --project src/EGT.Cli -- run --exe "D:\Games\MyGame\MyGame.exe" --provider llm --api-key "<KEY>" --base-url "https://api.openai.com" --model "gpt-4o-mini"

# Apply directly to game (backup required)
dotnet run --project src/EGT.Cli -- run --exe "D:\Games\MyGame\MyGame.exe" --provider mock --apply

# Restore from manifest
dotnet run --project src/EGT.Cli -- restore --manifest "EGT_Output\MyGame\20260223_120001\manifest.json"
```

## End-to-End Demo (Fixture)

Run the automated fixture scenario (extract -> translate(mock) -> output -> manifest -> restore):

```bash
dotnet test tests/EGT.Tests/EGT.Tests.csproj --filter PipelineE2ETests
```

## Output Folders

1. `EGT_Output/<game>/<timestamp>/` translated files + `manifest.json`.
2. `EGT_Backup/<timestamp>/` backups used by restore (only when `--apply`).
3. `EGT_Output/<game>/<timestamp>/report/` quality report and translation preview.
4. `%LocalAppData%/easy_game_translator/EGT_Cache/translation_cache.db` translation cache (default relative-path resolution target).
5. `logs/` app and CLI logs.

## Engineering Docs

1. `docs/SPEC.md`
2. `docs/ARCHITECTURE.md`
3. `docs/UX.md`
4. `docs/DESIGN_SYSTEM.md`
5. `docs/DELIVERY_PLAN.md`
6. `CONTRIBUTING.md`
7. `SECURITY.md`

## Legal and Ethical Boundary

1. EGT is only for legally owned games and authorized localization use cases.
2. EGT does not distribute pirated content or any built-in game assets.
3. By default, EGT uploads only extracted text fragments to translation services, not raw game files.
4. Users are responsible for compliance with third-party API terms and local laws.
