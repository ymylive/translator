# UX Blueprint (MVP)

## 1. Main Window Layout

1. Header:
   product name + legal hint.
2. Left panel:
   drag zone, base config, advanced config.
3. Right panel:
   progress, controls, logs.
4. Footer:
   output path shortcut.

## 2. Core User Journey

1. User drags `.exe` to drop zone.
2. EGT auto-detects root and profile.
3. User picks provider and target language.
4. User clicks `开始翻译`.
5. EGT shows progress and logs.
6. User opens output folder or applies in place.
7. If applied in place, user can restore by manifest.

## 3. Newbie / Advanced Mode

1. Newbie defaults:
   - provider: `mock` (local demo) or selected API provider
   - apply in place: off
   - output only
2. Advanced settings:
   - API endpoint/model/region
   - concurrency
   - max file size
   - glossary CSV
   - profile override

## 4. Accessibility & Interaction Checklist

1. Contrast ratio at least `4.5:1` for text.
2. Click target minimum `44x44`.
3. Clickable controls use pointer cursor.
4. Motion duration in `150-300ms` range where applied.
5. Keyboard focus visible.
6. No horizontal overflow on typical widths.

## 5. Error UX

1. Stage-level status (`extract/translate/apply/done`).
2. Inline warning lines in logs.
3. Partial failure does not crash full run.
4. Cancel button always available during long tasks.

