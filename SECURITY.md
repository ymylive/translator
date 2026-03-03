# Security Policy

## 1. API Key Storage

1. Windows uses DPAPI (`ProtectedData`, CurrentUser scope).
2. Keys are encrypted before persistence.
3. Plaintext key files are prohibited.

## 2. Log Redaction

1. Never print raw API keys in UI or file logs.
2. Mask sensitive fields (`apiKey`, bearer tokens, secret headers).
3. Keep request bodies minimal in error logs.

## 3. Data Upload Boundary

1. Default behavior:
   only extracted text segments are sent to providers.
2. Game files are never uploaded unless explicitly supported and enabled by user action in future versions.

## 4. Crash Reporting

1. Default: off.
2. Any future crash-report feature must be opt-in and redact sensitive config.

## 5. Restore Safety

1. Backup is required before in-place apply.
2. Restore validates backup hash before overwrite.
3. Manifest must be immutable record for audit/recovery.

## 6. Responsible Disclosure

If you discover a security issue, report privately to project maintainers before public disclosure.

