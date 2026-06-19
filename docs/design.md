# P12Bridge Technical Design

## 1. Purpose

This document translates the PRD into an implementation-oriented design for the first build of P12Bridge Desktop.

The design keeps the MVP local-first and deliberately avoids features that would require cloud custody, Apple ID password collection, Mac device spoofing, or Windows-based IPA resigning.

## 2. Architecture

Recommended solution layout:

```text
p12bridge-desktop/
  src/
    P12Bridge.Desktop/        # WPF or WinUI shell
    P12Bridge.Core/           # certificate, profile, IPA, upload domain logic
    P12Bridge.Infrastructure/ # filesystem, credential storage, external tools
  tests/
    P12Bridge.Core.Tests/
    P12Bridge.Infrastructure.Tests/
  docs/
    prd.md
    design.md
    implement.md
```

Layer responsibilities:

- `Desktop`: views, view models, navigation, progress display, user-facing validation messages.
- `Core`: pure domain models and validation rules for certificate assets, provisioning profiles, IPA metadata, upload readiness.
- `Infrastructure`: Windows filesystem access, credential storage, process execution, Apple API/upload adapters, local project storage.

## 3. Core Workflows

### 3.1 Certificate Project

A certificate project is a local folder plus metadata file representing one signing asset set.

Suggested files:

```text
project-folder/
  p12bridge.project.json
  private.key
  request.csr
  certificate.cer
  certificate.pem
  export.p12
  profile.mobileprovision
  logs/
```

The metadata file stores non-secret fields only:

- project name,
- certificate purpose,
- created time,
- file paths,
- certificate subject,
- team id if available,
- expiration time,
- last validation result.

Passwords and app-specific upload credentials must not be stored in this metadata file.

### 3.2 P12 Generation

Flow:

1. Desktop collects certificate purpose and subject fields.
2. Core builds a certificate request command model.
3. Infrastructure generates RSA key and CSR locally.
4. User uploads CSR manually in Apple Developer.
5. User imports `.cer`.
6. Infrastructure converts `.cer` and private key into `.p12`.
7. Core validates that required files exist and records asset state.

Implementation options:

- Prefer .NET cryptography APIs when they can produce Apple-compatible output.
- Use OpenSSL-compatible process execution only if .NET APIs cannot reliably cover a required step.
- Keep external process calls behind an interface so they can be replaced or tested.

### 3.3 Provisioning Profile Parsing

The `.mobileprovision` file is a CMS/PKCS#7-wrapped plist payload.

The parser should extract:

- UUID,
- name,
- team identifier,
- application identifier / Bundle ID,
- creation date,
- expiration date,
- profile type,
- provisioned devices count,
- developer certificate fingerprints.

Validation rules:

- expired profile blocks use,
- App Store upload requires App Store distribution-compatible profile,
- Bundle ID mismatch blocks upload readiness,
- missing certificate relationship creates a warning or block depending on available evidence.

### 3.4 IPA Inspection

An IPA is a zip archive containing `Payload/<App>.app`.

The inspector should extract:

- app bundle path,
- `Info.plist`,
- Bundle ID,
- version,
- build number,
- embedded provisioning profile if present,
- basic signature-related file presence.

MVP must not modify the IPA.

### 3.5 Upload Adapter

Upload remains the highest-risk technical proof.

The MVP should isolate upload behind an adapter:

```text
IUploadService
  ValidateEnvironment()
  UploadAsync(UploadRequest, IProgress<UploadProgress>)
```

Candidate paths:

- official Apple Transporter / iTMSTransporter-compatible execution,
- App Store Connect API supported upload flow,
- open-source uploader only if official paths are insufficient and the risk is documented.

The desktop UI should not depend directly on any specific upload tool. It should consume normalized progress events and normalized failure codes.

## 4. Data and Security

Sensitive data classes:

- private key,
- P12 export password,
- Apple app-specific password,
- App Store Connect API private key,
- upload logs that may contain identifiers.

Rules:

- Private keys stay on disk only in user-selected local project folders.
- P12 password is entered at export time and not stored by default.
- Optional credential persistence must require explicit consent.
- Use Windows Credential Manager or DPAPI-backed secure storage for optional secrets.
- Logs must redact passwords, private key content, JWTs, app-specific passwords, and full paths if user chooses privacy mode.

## 5. UI Structure

Primary navigation:

- Dashboard
- Certificate
- Profiles
- IPA Upload
- Asset Library
- Settings

The first implementation can use a simple WPF shell with MVVM. Visual polish is secondary to safe workflow, clear states, and actionable errors.

## 6. Error Model

Core errors should be structured, not plain strings:

- code,
- severity,
- user message,
- technical detail,
- suggested action,
- related file path if safe to show.

Examples:

- `PRIVATE_KEY_MISSING`
- `PROFILE_EXPIRED`
- `BUNDLE_ID_MISMATCH`
- `IPA_INFO_PLIST_MISSING`
- `UPLOAD_CREDENTIAL_INVALID`
- `TRANSPORTER_NOT_FOUND`

## 7. Technical Risks

- Apple upload mechanics on Windows need proof before UI commitments.
- Mobile provisioning profile parsing needs real samples.
- IPA signature inspection on Windows will be limited without Apple codesign.
- OpenSSL packaging and path management can create install complexity.
- App Store Connect API onboarding may be difficult for non-technical users.

## 8. Design Decisions

- Keep upload adapter replaceable.
- Keep cryptographic operations separate from UI.
- Keep parsed metadata separate from original files.
- Do not add cloud services in MVP.
- Do not build IPA resigning until certificate/profile/upload workflows are stable.
