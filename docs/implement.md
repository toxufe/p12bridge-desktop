# P12Bridge Implementation Plan

## 1. Current Phase

Planning and repository bootstrap.

Do not start full feature implementation until the solution skeleton and technical proofs are accepted.

## 2. Milestone 0: Repository Bootstrap

- [x] Clone GitHub repository.
- [x] Add PRD to `docs/prd.md`.
- [x] Add README.
- [x] Add technical design.
- [x] Add implementation plan.
- [x] Commit initial documentation locally.
- [ ] Push initial documentation.

Validation:

```powershell
git status --short
```

## 3. Milestone 1: .NET Solution Skeleton

Create the baseline solution:

- [x] `src/P12Bridge.Desktop`
- [x] `src/P12Bridge.Core`
- [x] `src/P12Bridge.Infrastructure`
- [x] `tests/P12Bridge.Core.Tests`
- [x] `tests/P12Bridge.Infrastructure.Tests`

Recommended commands:

```powershell
dotnet new sln -n P12Bridge
dotnet new classlib -n P12Bridge.Core -o src/P12Bridge.Core
dotnet new classlib -n P12Bridge.Infrastructure -o src/P12Bridge.Infrastructure
dotnet new wpf -n P12Bridge.Desktop -o src/P12Bridge.Desktop
dotnet new xunit -n P12Bridge.Core.Tests -o tests/P12Bridge.Core.Tests
dotnet new xunit -n P12Bridge.Infrastructure.Tests -o tests/P12Bridge.Infrastructure.Tests
dotnet sln add src/P12Bridge.Core/P12Bridge.Core.csproj
dotnet sln add src/P12Bridge.Infrastructure/P12Bridge.Infrastructure.csproj
dotnet sln add src/P12Bridge.Desktop/P12Bridge.Desktop.csproj
dotnet sln add tests/P12Bridge.Core.Tests/P12Bridge.Core.Tests.csproj
dotnet sln add tests/P12Bridge.Infrastructure.Tests/P12Bridge.Infrastructure.Tests.csproj
```

Validation:

```powershell
dotnet build
dotnet test
```

Current blocker:

- Resolved: .NET 8 SDK 8.0.422 is installed under `C:\Users\Fly\.dotnet`.
- Use `C:\Users\Fly\.dotnet\dotnet.exe` if the user-local SDK is not on PATH.

## 4. Milestone 2: Certificate Proof

Goal: prove local key/CSR/P12 generation on Windows.

Tasks:

- [x] Define certificate generation domain contracts.
- [x] Generate RSA private key.
- [x] Generate CSR.
- [x] Export `.p12` from certificate bytes and private key bytes.
- [x] Add tests for successful key/CSR/P12 generation.
- [x] Add tests for invalid certificate input, missing private key, small key size, and empty P12 password.

Validation:

- [x] Generate test key and CSR locally.
- [x] Export test P12 from generated in-memory test certificate material.
- [x] Confirm errors are structured and user-actionable.
- [x] `dotnet build P12Bridge.sln` passes.
- [x] `dotnet test P12Bridge.sln --no-build` passes.

## 5. Milestone 3: Profile and IPA Parsing Proof

Goal: parse real signing assets before building full UI.

Tasks:

- [x] Parse `.mobileprovision` plist payload from synthetic CMS-like bytes.
- [x] Extract Bundle ID, Team ID, expiration, profile type, device count, and certificate fingerprints.
- [x] Parse IPA zip structure and XML `Info.plist`.
- [x] Extract Bundle ID, version, build number, embedded profile if present.
- [x] Implement validation rules for malformed, incomplete, expired, and unknown profile data.

Validation:

- [x] Add fixture-based tests for valid and expired profiles.
- [x] Add fixture-based tests for IPA metadata extraction.
- [x] Confirm profile parser failures produce stable error codes.

## 6. Milestone 4: Upload Proof

Goal: validate the upload path before promising product behavior.

Prerequisite authentication proof:

- [x] Define App Store Connect API Key / JWT domain contracts.
- [x] Generate ES256 JWTs locally from `.p8` key material.
- [x] Add a low-risk App Store Connect connection-check adapter.
- [x] Normalize invalid credential, unauthorized, forbidden, Apple API, and network failures.
- [x] Keep Apple ID + app-specific password handling deferred to the upload proof.

Prerequisite upload-readiness proof:

- [x] Define App Store / TestFlight upload readiness contracts.
- [x] Evaluate parsed IPA metadata and provisioning profile metadata together.
- [x] Block common upload-preflight failures: missing metadata, missing signature marker, missing or expired embedded profile, non-App-Store profile, and Bundle ID / Team ID mismatches.
- [x] Return structured ready / ready-with-warnings / blocked results with actionable validation issues.

Tasks:

- Create `IUploadService`.
- Implement a first upload adapter with the safest available mechanism.
- Normalize progress and errors.
- Test invalid credentials and missing tool cases.

Validation:

- Run an upload verification against a real signed IPA when credentials are available.
- Confirm upload logs can be copied with secrets redacted.

## 7. Milestone 5: Desktop MVP

Tasks:

- Build WPF shell and navigation.
- Add certificate project creation UI.
- Add profile import and validation UI.
- Add IPA inspection and upload UI.
- Add settings for paths and optional credential storage.
- Add operation history and log viewer.

Validation:

- Manual end-to-end walkthrough:
  1. create project,
  2. generate CSR,
  3. import CER,
  4. export P12,
  5. import profile,
  6. inspect IPA,
  7. run upload attempt.

## 8. Review Gates

Before moving beyond proofs:

- Upload path is verified or explicitly marked experimental.
- Secret storage behavior is reviewed.
- Logs are redacted.
- User-facing copy does not ask for Apple ID main password.
- The app does not claim to sign or resign IPA files.

## 9. Rollback Points

- If .NET cryptography cannot generate compatible artifacts, isolate and replace with OpenSSL adapter.
- If official upload path is too constrained, keep upload behind adapter and continue with validation-only MVP until a safe path is proven.
- If WPF UI complexity slows proof work, delay UI polish and keep a minimal desktop shell.
