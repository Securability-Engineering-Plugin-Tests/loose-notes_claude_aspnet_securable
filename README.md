# LooseNotes Information Exchange Platform — Securable Reference Build

ASP.NET Core 8 MVC reference implementation of the LooseNotes PRD,
re-engineered to embody **FIASSE v1.0.4** Securable Software Engineering
Model qualities. Functional surface follows the PRD; security-relevant
technical requirements were replaced with FIASSE-aligned controls (see
"Securability Decisions" below).

> This codebase intentionally **does not** implement the deliberately insecure
> patterns the original PRD spelled out (Base64 "encryption" of passwords,
> SQL string concatenation, XXE, path traversal, anonymous admin, etc.).
> Generating those patterns would violate the active plugin's FIASSE/SSEM
> constraints. The PRD's *functional* intent is preserved.

---

## Setup and Run

### Prerequisites

- **.NET SDK 8.0** (LTS). Confirm with `dotnet --version`.
- A modern terminal (PowerShell, bash, or zsh).

### One-time configuration

Generate a 32-byte AES-256 key and supply it via user-secrets (development) or
an environment variable (production). The application **fails fast** at startup
if it is missing — by design (FIASSE: Confidentiality + Resilience).

```bash
# Generate a key (Linux / macOS / WSL)
key=$(openssl rand -base64 32)

# Or PowerShell
# $key = [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 } | ForEach-Object { [byte]$_ }))

# Store under user-secrets (development)
cd src/LooseNotes.Web
dotnet user-secrets init
dotnet user-secrets set "Encryption:KeyBase64" "$key"

# Or as an environment variable (production)
export Encryption__KeyBase64="$key"
```

Optional: pre-set the bootstrap admin password (otherwise one is generated on
first run and emitted to the operator log in Development only):

```bash
dotnet user-secrets set "AdminBootstrap:InitialPassword" "Some-Strong-Pass-12!"
```

### Run

```bash
# From the repository root
dotnet restore
dotnet run --project src/LooseNotes.Web
```

The app listens on <https://localhost:5001> by default. SQLite database
(`loosenotes.db`) and attachment storage (`App_Data/attachments/`) are
created relative to the project's content root.

### Run the tests

```bash
dotnet test
```

The xUnit suite covers the pure-logic securable controls
(SafePathResolver, TokenHasher, HtmlSanitizationService, EncryptionService,
XmlIngestService).

---

## Project Layout

```
LooseNotes.sln
├── src/LooseNotes.Web/
│   ├── Program.cs                      Pipeline + DI + Identity + rate limiting + headers
│   ├── Controllers/                    Thin HTTP boundary; binds to typed DTOs only
│   ├── Models/                         Input DTOs (Request Surface Minimization)
│   ├── Data/                           EF Core DbContext + entities
│   ├── Services/                       Centralized security-sensitive logic
│   ├── Options/                        Strongly-typed config sections
│   ├── Views/                          Razor (auto-encoding); Html.Raw only on sanitized content
│   └── wwwroot/                        Static assets
└── tests/LooseNotes.Tests/             xUnit unit tests for securable controls
```

---

## SSEM Attribute Coverage Summary (FIASSE v1.0.4 — 10 attributes)

> v1.0.4 introduces Observability as the 10th SSEM attribute. The brief
> mentioned nine; all ten are covered below for completeness.

### Maintainability

| Attribute | How it is addressed in this codebase |
|-----------|--------------------------------------|
| **Analyzability** (S3.2.1.1) | Methods are short and single-purpose. Names describe intent (`NoteAuthorizationService.LoadOwnedAsync`, `SafePathResolver.ResolveUnder`). No dead code, no commented-out blocks. Comments explain *why* (e.g., why we always emit a generic recovery response) — never *what*. |
| **Modifiability** (S3.2.1.2) | Every security-sensitive concern is centralized in exactly one service. Path canonicalization → `SafePathResolver`. HTML sanitization → `HtmlSanitizationService`. Ownership checks → `NoteAuthorizationService`. XML parser policy → `XmlIngestService`. Configuration is externalized to `appsettings.json` and bound via strongly-typed `*Options` records. Dependencies are injected through constructors; no static mutable state. |
| **Testability** (S3.2.1.3) | Every service is interface-fronted (`ISafePathResolver`, `IShareTokenService`, etc.) so it can be mocked. Controllers receive their collaborators through constructor DI. The xUnit suite exercises the pure logic without needing a running web host. |
| **Observability** (S3.2.1.4) | Structured `ILogger` calls at every trust-boundary outcome with consistent event names: `note.created`, `note.access.denied`, `attachment.saved`, `share_token.created`, `password_recovery.answer_failed`, `admin.note_reassigned`. Failure paths produce log signals — no silent `try/catch`. The diagnostics page exposes runtime headers (with sensitive ones redacted) for operators. |

### Trustworthiness

| Attribute | How it is addressed in this codebase |
|-----------|--------------------------------------|
| **Confidentiality** (S3.2.2.1) | Passwords are stored only as ASP.NET Core Identity hashes (PBKDF2/HMAC-SHA512). Security answers are hashed with the same hasher. Share tokens are stored hashed at rest. Auth cookies are HttpOnly + Secure + SameSite=Strict. Generic responses on login failure and password recovery start avoid information leaks. Admin diagnostics redact `Authorization`, `Cookie`, `Proxy-Authorization`, `X-Api-Key`, `X-Auth-Token`. AES-GCM encryption uses a config-supplied 32-byte key — no hardcoded passphrase, fail-fast on absence. |
| **Accountability** (S3.2.2.2) | Auth events (`account.login.success`, `account.login.failed`, `account.password_changed`, `account.registered`), authz outcomes (`note.access.denied` with reason), and admin actions (`admin.note_reassigned`) are logged with structured fields (actor id, target id, outcome). No request bodies or credentials are logged. |
| **Authenticity** (S3.2.2.3) | Authentication uses ASP.NET Core Identity with cookie sessions; lockout activates after 5 failed attempts. Anti-forgery tokens are required on every state-changing POST (`AutoValidateAntiforgeryTokenAttribute` + per-controller `[ValidateAntiForgeryToken]`). Share tokens come from `RandomNumberGenerator.GetBytes` (CSPRNG), not sequential integers. Password recovery uses an opaque, single-use, rate-limited ticket — never returns the original password. |

### Reliability

| Attribute | How it is addressed in this codebase |
|-----------|--------------------------------------|
| **Availability** (S3.2.3.1) | Rate limiters guard `login`, `recovery`, and `autocomplete`. Form upload size capped at 10 MB; ZIP import capped at 60 MB raw and 200 MB decompressed; archive entry count capped at 2,000. EF Core queries use `.Take(N)` projections. External (filesystem) operations are bounded. |
| **Integrity** (S3.2.3.2) | Every input crosses a typed DTO before reaching a service. EF Core LINQ queries are parameterized; the search and autocomplete paths escape `LIKE` wildcards. `SafePathResolver` enforces base-directory containment for every filesystem read/write. ZIP imports validate manifest and reject zip-slip. AES-GCM provides authenticated encryption. The XML loader prohibits DTDs (`DtdProcessing.Prohibit`, `XmlResolver = null`). |
| **Resilience** (S3.2.3.3) | Specific exception types (`InvalidAttachmentException`, `PathTraversalException`, `InvalidImportException`, `CryptographicException`) — no bare `catch`. Caller surfaces use generic operator-friendly messages while logs carry the detailed reason. `IDisposable`/`using` patterns for streams and cryptographic primitives. Failures redirect with a TempData notice rather than crashing the request. |

---

## Securability Decisions (PRD section → re-engineered behavior)

Every PRD requirement that demanded an unsafe pattern was substituted with the
FIASSE-aligned alternative below. The functional intent of the PRD is
preserved; the technical mechanism is replaced.

| PRD § | PRD said | This build does | FIASSE attribute |
|-------|----------|-----------------|------------------|
| §1 | Pre-seed accounts in configuration | First-run admin only, with a generated single-use password (or one supplied via user-secrets) and the same Identity password policy as everyone else. | Confidentiality |
| §2 | Base64-decode-then-string-equality password check; non-HttpOnly persistent cookie | ASP.NET Core Identity (`PasswordSignInAsync`) with lockout-on-failure; HttpOnly + Secure + SameSite=Strict cookie, sliding 60-minute expiry. | Authenticity, Confidentiality |
| §3 / §16 | Plaintext security answer; password stored Base64 | Answer hashed with Identity's hasher; profile lookup uses the authenticated principal — never a client cookie. | Confidentiality |
| §4 | Cookie-transported answer; reveal plaintext password | Server-side single-use ticket (CSPRNG, hashed at rest, rate-limited, max 5 attempts). On verification the user picks a *new* password — the original is never revealed. | Authenticity, Resilience |
| §6 / §13 / §14 | `Html.Raw` of unsanitized DB content, comments and titles | Note bodies are sanitized on **write** by encoding every character with `HtmlEncoder.Default` and re-introducing only `<br/>` between line breaks (no third-party HTML parser). The encoded payload is then `Html.Raw`'d safely. Titles and comments are rendered through Razor's auto-encoding. **Trade-off**: rich-text formatting (bold, lists, hyperlinks) is not rendered — see "Trade-offs" below. | Integrity |
| §7 / §23 | Client-supplied filename written under wwwroot; no validation | GUID-based stored filename, extension allowlist, base-directory containment via `SafePathResolver`, storage outside wwwroot, server-side ownership check on download. | Integrity, Resilience |
| §8 / §9 | No ownership check; no anti-forgery | `INoteAuthorizationService.LoadOwnedAsync` enforces ownership server-side; `[ValidateAntiForgeryToken]` plus the global filter. | Integrity |
| §10 | Sequential integer share tokens | 256-bit URL-safe tokens from `RandomNumberGenerator`; only the SHA-256 hash is persisted. | Authenticity |
| §11 / §12 / §17 | LIKE-clause string concatenation | EF Core LINQ with parameter binding and escaped wildcards; visibility filter uses `IsPublic OR OwnerId == viewerId`; topic-tag filter is allowlisted. | Integrity |
| §15 | Anonymous email autocomplete; full string concatenation | `[Authorize]` + per-user/IP rate limit; minimum 3-char prefix; capped result count. | Confidentiality, Availability |
| §18 | Verb-specific authz with no DELETE rule, shell command exec, anonymous DB reinit, raw payload logging | `[Authorize(Policy="AdminOnly")]` on every action regardless of verb. No shell-execution endpoint exists. DB reinitialization is removed from the web UI (operators use EF/SQLite tooling). Logs carry IDs and outcomes only. | Accountability, Authenticity |
| §19 | Reassignment with no admin role check | Admin role required; both old and new owner ids logged. | Accountability |
| §20 / §21 | Path concatenation with no traversal validation | `SafePathResolver.ResolveUnder` enforces containment; ZIP import validates manifest, rejects entries outside `attachments/`, caps decompressed size and entry count, refuses to overwrite. | Integrity, Availability |
| §22 | Default XML parser (DTDs allowed) | `XmlIngestService` sets `DtdProcessing.Prohibit`, `XmlResolver = null`, character/entity caps. | Integrity |
| §24 | Hardcoded fallback passphrase, constant PBKDF2 salt | AES-256-GCM with key from configuration (fail-fast if missing); fresh nonce per encryption; key derivation is the operator's responsibility outside the binary. | Confidentiality, Integrity |
| §25 | Reflect headers without encoding | View renders header pairs through Razor's auto-encoding; `Authorization`, `Cookie`, etc. are redacted server-side. | Integrity, Confidentiality |

---

## Dependency Hygiene (FIASSE S4.5 / S4.6)

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 8.0.10 | Identity stores |
| Microsoft.AspNetCore.Identity.UI | 8.0.10 | Default token providers |
| Microsoft.EntityFrameworkCore.Sqlite | 8.0.10 | Persistence (SQLite for the reference build) |
| Microsoft.EntityFrameworkCore.Tools | 8.0.10 | Design-time migrations |

All security-relevant primitives (HTML encoding, rate limiting, cryptography,
XML parser hardening, anti-forgery, cookie policy) are taken from the .NET 8
BCL/ASP.NET Core. We deliberately removed the third-party HTML sanitizer that
the first cut of this project pulled in: an allowlist parser is overkill for
a note-taking app where `HtmlEncoder.Default` plus `<br/>` substitution covers
the requirement at zero additional dependency surface (FIASSE S4.6
Stewardship — minimize the relationship list).

### Trade-offs

- **Rich-text rendering**: the PRD calls note content "rich-text"; the
  securable build renders it as plain text with line breaks preserved. If
  rich formatting is required, reintroduce a maintained allowlist sanitizer
  (e.g. `Ganss.Xss.HtmlSanitizer` ≥ a CVE-clean version) and confine the
  decision to `HtmlSanitizationService` — no other call site needs to change.
- **Email recovery channel**: this build uses an in-browser ticket cookie
  (HttpOnly, Secure, SameSite=Strict) for the recovery flow. A production
  deployment should additionally email a one-time link before answering the
  security question, so device-takeover doesn't grant recovery.
- **SQLite**: chosen for one-command demo; switch to a server-side database
  (PostgreSQL, SQL Server) for any multi-instance deployment. Connection
  strings should come from a secret store, not `appsettings.json`.

---

## License

CC-BY-4.0 (matching the FIASSE framework reference under which this code was
generated).
