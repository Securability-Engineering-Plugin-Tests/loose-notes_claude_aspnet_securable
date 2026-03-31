# Loose Notes

A secure, multi-user note-taking web application built with ASP.NET Core 8 MVC, Entity Framework Core, and ASP.NET Core Identity.

## Features

- User registration, login, lockout, and password reset
- Create, edit, delete, and search notes (public/private visibility)
- File attachments (PDF, DOC, DOCX, TXT, PNG, JPG, JPEG — max 10 MB)
- Shareable links (CSPRNG-generated tokens, revokable)
- 1–5 star ratings with comments
- Top Rated page (public notes with ≥ 3 ratings)
- Admin dashboard: user management, activity log, note reassignment
- Structured audit logging for all security-sensitive actions

---

## Setup and Run

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Clone and run

```bash
git clone <repo>
cd LooseNotes
dotnet run
```

The application will:
1. Create the SQLite database (`loosenotes.db`) and run migrations automatically.
2. Seed an admin user (see credentials below).
3. Listen on `http://localhost:5000` (or `https://localhost:5001`).

### Default Admin Credentials

Set via environment variables before running (defaults shown):

| Variable         | Default                  |
|------------------|--------------------------|
| `ADMIN_EMAIL`    | `admin@loosenotes.local` |
| `ADMIN_USERNAME` | `admin`                  |
| `ADMIN_PASSWORD` | `Admin@123!`             |

**Change these before deploying to any non-local environment.**

```bash
ADMIN_EMAIL=you@example.com ADMIN_PASSWORD='Str0ng!Pass' dotnet run
```

### Configuration (`appsettings.json`)

| Key                              | Default       | Description                     |
|----------------------------------|---------------|---------------------------------|
| `ConnectionStrings:DefaultConnection` | `Data Source=loosenotes.db` | SQLite connection string |
| `FileStorage:UploadDirectory`    | `uploads/`    | Directory for uploaded files    |

To use a different database (e.g., SQL Server), change the connection string and swap `UseSqlite` for `UseSqlServer` in `Program.cs`, then add the appropriate EF Core provider package.

### Running Migrations Manually

```bash
dotnet ef database update
```

---

## SSEM Attribute Coverage Summary

The nine SSEM attributes (across Maintainability, Trustworthiness, and Reliability pillars) were applied as engineering constraints throughout the codebase.

### Maintainability

| Attribute | How it is addressed |
|-----------|---------------------|
| **Analyzability** | Controllers are split by concern (Account, Notes, Admin, Profile, Attachments, Share). Methods are kept under 30 LoC where possible. Cyclomatic complexity is minimised by extracting helpers (`ValidateAndStoreAttachmentAsync`, `IsOwnerOrAdmin`). Meaningful naming throughout — no abbreviations or magic values. |
| **Modifiability** | All cross-cutting concerns are behind interfaces: `IAuditService`, `IFileStorageService`, `IShareTokenService`, `IEmailService`. Dependency injection is used everywhere. The email service is a stub (`LoggingEmailService`) that can be swapped for an SMTP/SaaS implementation without touching callers. Security configuration (password policy, lockout, cookie settings) is centralised in `Program.cs`. |
| **Testability** | All services depend on injected interfaces, making them mockable. The `AuditService`, `LocalFileStorageService`, `ShareTokenService`, and `LoggingEmailService` are small, single-purpose classes that can be tested in isolation. ViewModels use data annotations so validation logic is testable without HTTP context. |

### Trustworthiness

| Attribute | How it is addressed |
|-----------|---------------------|
| **Confidentiality** | Passwords are hashed by ASP.NET Core Identity (PBKDF2 with HMAC-SHA256, 100k iterations). Password reset tokens are stored as SHA-256 hashes — the raw token is never persisted. Logs contain no passwords, tokens, or full PII. Minimum data is collected (no phone, no address). Security headers (`X-Content-Type-Options`, `X-Frame-Options`, CSP) are applied on every response. |
| **Accountability** | Every security-sensitive action (register, login, logout, login failure, lockout, password reset, note CRUD, attachment access, admin actions) is written to the `AuditLogs` table and to structured Serilog output. Audit records capture who, what, target, outcome, and client IP. The audit table uses `SetNull` on user deletion so historical records are preserved. |
| **Authenticity** | ASP.NET Core Identity handles credential verification with constant-time comparison. Session cookies are `HttpOnly`, `SameSite=Lax`, and scoped to 8-hour sliding expiration. Share tokens are generated with `RandomNumberGenerator.GetBytes(32)` (256-bit entropy). Anti-forgery tokens protect all state-changing forms and POST actions. Redirect-after-login validates the return URL with `Url.IsLocalUrl()` to prevent open redirect. |

### Reliability

| Attribute | How it is addressed |
|-----------|---------------------|
| **Availability** | Account lockout after 5 failed attempts (15-minute lockout) limits brute-force load. Multipart request body size is capped at 11 MB to prevent resource exhaustion from large uploads. Serilog rolling-file logging avoids log growth causing disk pressure. Audit save failures are caught and logged without breaking the primary request flow. |
| **Integrity** | Input validation follows the canonicalize → sanitize → validate pattern at every trust boundary: file extension is lowercased before allow-list check, usernames are trimmed, query terms are trimmed and lowercased for comparison. The `Derived Integrity Principle` is enforced: `OwnerId` and `RaterId` are always taken from the authenticated session, never from the request body. The `LocalFileStorageService` enforces a UUID-only regex on all file system operations, rejecting any path traversal attempt. EF Core parameterized queries prevent SQL injection. Razor views HTML-encode all output by default. |
| **Resilience** | Specific exception handling is used (no bare `catch (Exception)` swallowing). The `LocalFileStorageService.GetAbsolutePath` throws `ArgumentException` on invalid input rather than silently failing. The admin `ReassignNote` action validates that the target user exists before updating the database. Cascade delete rules are explicit in `OnModelCreating` so removal of a note atomically removes its children. |

---

## Dependency Selection Rationale

| Package | Version | Rationale |
|---------|---------|-----------|
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 8.0.11 | Latest stable for .NET 8; Microsoft-maintained with security patch cadence |
| `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.11 | Matches Identity version; SQLite is sufficient for the default single-server deployment |
| `Serilog.AspNetCore` | 8.0.3 | Latest stable; structured logging with zero known high/critical CVEs |
| `Serilog.Sinks.Console` / `Sinks.File` | 6.0.0 | Current stable sinks; well-maintained, low dependency footprint |

All versions are pinned in the `.csproj`. Use `dotnet list package --vulnerable` to audit for known CVEs after installing.
