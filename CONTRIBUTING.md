# Contributing to EGT

## 1. Branching Strategy

1. `main`:
   releasable branch, protected.
2. Feature branch naming:
   `feat/<scope>-<short-name>`
3. Bugfix branch naming:
   `fix/<scope>-<short-name>`
4. One PR per logical unit.

## 2. Commit Convention

Use Conventional Commits:

1. `feat:`
2. `fix:`
3. `docs:`
4. `refactor:`
5. `test:`
6. `chore:`

Examples:

1. `feat(core): add manifest writer and restore flow`
2. `fix(profile): preserve csv quoted escaping`
3. `docs(spec): update mvp non-goals`

## 3. Versioning

SemVer:

1. MAJOR: breaking API/behavior.
2. MINOR: backward-compatible features.
3. PATCH: fixes and internal improvements.

## 4. Code Style & Quality Gate

1. `.editorconfig` is authoritative.
2. CI enforces `dotnet format --verify-no-changes`.
3. Any core behavior change must include tests.
4. Keep diff minimal and focused.
5. Never log raw API keys.

## 5. Review Rules

1. PR must include:
   - scope
   - risk
   - test evidence
2. At least one reviewer from relevant module owner.
3. Blocking issues:
   security regressions, restore safety breaks, missing tests.

## 6. Issue / PR Templates

1. Bug report:
   reproduction steps, expected vs actual, logs, manifest snippet.
2. Feature request:
   user value, acceptance criteria, out-of-scope notes.
3. PR template:
   summary, changes, test commands, rollback considerations.

## 7. Add a New Translation Provider

1. Create project `src/EGT.Translators.<Name>/`.
2. Implement `ITranslationProvider`.
3. Add retry/limit/error mapping.
4. Add DI extension `Add<Name>Provider`.
5. Wire into `EGT.Cli` and `EGT.App` startup.
6. Add unit/integration tests for:
   - happy path
   - partial failure
   - rate-limit retry

## 8. Add a New Profile

1. Create project `src/EGT.Profiles.<Name>/`.
2. Implement `IProfile`:
   - `Supports`
   - `ExtractAsync`
   - `ApplyAsync`
3. Define capability metadata:
   extensions, hints, priority.
4. Add DI extension and register in app/cli.
5. Add fixture + e2e tests:
   extract -> apply -> manifest -> restore.

