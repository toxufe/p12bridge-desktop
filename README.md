# P12Bridge Desktop

[中文说明](README.zh-CN.md)

P12Bridge is a Windows desktop tool for iOS certificate, provisioning profile, and IPA upload workflows.

The MVP is local-first: it helps Windows users generate private keys and CSRs, import Apple-issued certificates, export P12 files, validate provisioning profiles and signed IPA files, and upload IPA builds without collecting Apple ID main passwords or hosting signing assets in the cloud.

## Product Scope

- Windows desktop app.
- Recommended stack: C# / .NET 8 with WPF or WinUI 3.
- Local certificate and signing asset management.
- Semi-automated P12 generation:
  - generate private key and CSR locally,
  - user uploads CSR in Apple Developer,
  - user imports downloaded `.cer`,
  - app exports `.p12` locally.
- Provisioning profile import and validation.
- Signed IPA inspection and upload.
- No local IPA signing or resigning in MVP.
- No Apple ID main password collection.
- No cloud certificate custody or cloud upload relay.

## Documents

- [Product requirements](docs/prd.md)
- [Technical design](docs/design.md)
- [Implementation plan](docs/implement.md)
- [Manual verification checklist](docs/manual-verification.md)

## Repository Status

The Desktop MVP implementation is in place for local certificate workflows, profile import, IPA inspection, upload readiness, settings, asset management, and operation history.

Automated local validation covers the main non-Apple service chain. The remaining verification work is:

1. Run the manual end-to-end walkthrough with real Apple-issued certificate/profile assets.
2. Verify a real signed IPA upload attempt with valid credentials.
3. Capture only redacted evidence listed in the manual verification checklist.

## Security Principles

- Keep private keys local.
- Do not store Apple ID main passwords.
- Store optional credentials only with explicit user consent.
- Redact secrets from logs.
- Prefer Apple official APIs and documented upload mechanisms where possible.
