# EGT MVP SPEC

## 1. Scope

MVP goal: usable and stable translation flow for common text resources in Windows games.

### In Scope

1. Input:
   Drag-and-drop `.exe` or manual selection.
2. Project detection:
   EXE directory as default root, user can override.
3. Generic extraction:
   `.json .csv .tsv .ini .xml .yaml .yml .txt .strings`
4. Translation providers:
   DeepL, Microsoft Translator, OpenAI-compatible LLM, mock provider.
5. Quality baseline:
   placeholder protection, glossary CSV, cache, batch splitting, retry.
6. Output:
   `EGT_Output/<game>/<timestamp>/`
7. In-place apply:
   Optional, only after backup creation.
8. Recovery:
   `manifest.json` + restore command.
9. UX:
   Progress, current stage, speed, logs, error visibility, cancel support.

### Out of Scope (Non-MVP)

1. Runtime hook / injection / subtitle overlay.
2. Unity/Unreal proprietary package parsing.
3. OCR / texture translation.
4. Automatic cloud upload of game files.

## 1.1 Engineering Scaffold Baseline

Repository scaffold is fixed to this shape:

1. `src/EGT.App` desktop shell (Avalonia + MVVM).
2. `src/EGT.Cli` command entry for automation/debug.
3. `src/EGT.Core` reusable pipeline.
4. `src/EGT.Contracts` interfaces and DTO contracts.
5. `src/EGT.Translators.*` provider adapters.
6. `src/EGT.Profiles.*` game/file profiles.
7. `tests/EGT.Tests` automated test suite.
8. `tests/fixtures` deterministic sample input.
9. `docs/` governance and architecture docs.

Bootstrap commands:

```bash
dotnet restore easy_game_translator.sln
dotnet build easy_game_translator.sln
dotnet run --project src/EGT.Cli -- --help
dotnet run --project src/EGT.App
```

## 2. Supported File Handling Rules

1. Skip non-text large files above `50MB` by default (`MaxFileSizeMb` configurable).
2. Skip by extension default denylist:
   `*.pak *.bundle *.dll *.exe *.bin *.dat *.mp4 *.mp3 *.ogg *.wav`.
3. Encoding:
   read UTF-8, UTF-8 BOM, UTF-16 LE/BE; write back preserving detected encoding.
4. JSON:
   current MVP does token/segment replacement and avoids translating JSON keys.
5. CSV/TSV:
   preserve delimiter and quoted field escaping.

## 3. Output and Rollback

1. Output-only mode:
   write translated files to output folder without touching original files.
2. Apply mode:
   create backup first, then replace original files.
3. Manifest:
   record project info, options hash, per-file path mapping, checksums, and restore items.
4. Restore:
   verify backup checksum then copy backup back to original path.

## 4. Error Handling Strategy

1. Provider-level partial failure is allowed.
2. Failed entries fallback to source text and are recorded in warnings.
3. Retry policy on `429` / `5xx` with exponential backoff.
4. Cancellation token propagates through full pipeline.
5. Sensitive data is masked in logs.

## 5. Reproducibility

Same `input + options hash + cache` should produce deterministic output except non-deterministic provider behavior.
