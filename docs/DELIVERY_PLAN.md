# EGT Delivery Plan (Team + Milestones)

## 1. Team Setup

### Tech Lead

1. Scope:
   `src/EGT.Contracts`, `docs/ARCHITECTURE.md`, cross-project dependency governance.
2. DoD:
   contracts stable, no circular references, architecture doc updated.
3. Verify:
   `dotnet build easy_game_translator.sln -c Debug`

### Desktop UI Engineer

1. Scope:
   `src/EGT.App`.
2. DoD:
   drag-drop to execute flow, progress/log view, advanced settings.
3. Verify:
   `dotnet run --project src/EGT.App`

### Core Pipeline Engineer

1. Scope:
   `src/EGT.Core`, `src/EGT.Profiles.GenericText`.
2. DoD:
   extraction/translation/apply/manifest/restore full chain passes tests.
3. Verify:
   `dotnet test tests/EGT.Tests --filter PipelineE2ETests`

### Translators Engineer

1. Scope:
   `src/EGT.Translators.*`.
2. DoD:
   DeepL + Microsoft + LLM providers support retry/error mapping and batch handling.
3. Verify:
   provider integration tests + manual CLI smoke.

### QA Engineer

1. Scope:
   `tests/EGT.Tests`, `tests/fixtures`.
2. DoD:
   e2e demo reproducible, edge cases covered (placeholder/rollback/cancel).
3. Verify:
   `dotnet test easy_game_translator.sln`

### DevOps Engineer

1. Scope:
   `.github/workflows`, release artifacts, format gates.
2. DoD:
   PR build/test/format passes; release workflow outputs app+cli artifacts.
3. Verify:
   GitHub Actions green on PR and tagged release.

## 2. Milestones

| Milestone | Goal | Output | Owner |
|---|---|---|---|
| M0 | Repo and solution bootstrap | multi-project solution, DI/config/logging, CLI help, CI baseline | Tech Lead + DevOps |
| M1 | Generic extraction/apply | generic profile, output+manifest, restore path | Core Engineer + QA |
| M2 | Providers and quality | DeepL+Microsoft+LLM, cache, glossary, retry/chunking | Translators Engineer |
| M3 | Desktop closed loop | drag exe -> run -> progress/log -> open output + advanced settings | UI Engineer + QA |
| M4 | Release and packaging | win artifact workflow, version/changelog policy | DevOps + Tech Lead |

## 3. Issue Board (Initial Backlog)

| ID | Milestone | Title | Type | DoD |
|---|---|---|---|---|
| EGT-001 | M0 | Initialize solution and project references | chore | build passes locally/CI |
| EGT-002 | M0 | Add core DI/config/logging bootstrap | feat | app+cli can resolve pipeline |
| EGT-003 | M0 | Add CI workflow (build/test/format) | chore | PR checks green |
| EGT-004 | M1 | Implement GenericText extraction rules | feat | fixture files extracted |
| EGT-005 | M1 | Implement apply + manifest writer | feat | output+manifest generated |
| EGT-006 | M1 | Implement restore service with hash check | feat | restore test green |
| EGT-007 | M2 | Implement DeepL provider | feat | provider contract test green |
| EGT-008 | M2 | Implement Microsoft provider | feat | provider contract test green |
| EGT-009 | M2 | Implement OpenAI-compatible LLM provider | feat | provider contract test green |
| EGT-010 | M2 | Add cache + glossary integration | feat | duplicate translation avoided |
| EGT-011 | M3 | Build Avalonia main workflow UI | feat | drag->start->progress works |
| EGT-012 | M3 | Add advanced settings panel and cancel flow | feat | cancellation works |
| EGT-013 | M3 | Export failed entries and improve error visualization | feat | failure export available |
| EGT-014 | M4 | Package Windows artifacts (app + cli) | chore | release workflow publishes zip |
| EGT-015 | M4 | Changelog + semver release guide | docs | release notes template ready |

## 4. Test Strategy

1. Unit tests:
   placeholder protection, cache key determinism, manifest hash logic.
2. Integration tests:
   profile extraction/apply with fixture files and encoding checks.
3. E2E tests:
   `sample_game` flow: extract -> translate(mock) -> output -> manifest -> restore.
4. Regression policy:
   every bugfix must include one reproducible fixture case.
5. Quality gate:
   CI must pass `build + test + format`.
6. Manual smoke (release candidate):
   UI drag/drop run + CLI run/restore on Windows machine.
