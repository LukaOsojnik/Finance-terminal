# Task 3 Analysis: ASP.NET Core Identity Authentication (Autentikacija)

**Project:** simple-bloomberg-terminal  
**Date:** 2026-06-12  
**Scope:** Full audit of Identity setup, AppUser, roles, and per-controller authorization.

---

## 1. Identity Setup Analysis

### 1.1 Registration

**File:** `Areas/Identity/Pages/Account/Register.cshtml.cs`

- Standard Identity Razor Pages override (app views win over the Identity UI RCL).
- `InputModel` has three fields: `Email`, `Password`, `ConfirmPassword`.
- On POST: creates `new AppUser { UserName = Input.Email, Email = Input.Email }` and calls `_userManager.CreateAsync(user, Input.Password)`.
- On success: auto-signs in (`_signInManager.SignInAsync`) and redirects to return URL.
- Concurrency-safe: catches `DbUpdateException` as a backstop for duplicate-email races.
- **No extended fields are collected during registration.** The `AppUser` entity has five extra fields (all profile-picture metadata), but none are set in `Register.cshtml.cs` — they are populated later via the `/Account/Profile` page (Dropzone upload).

### 1.2 Login

**File:** `Areas/Identity/Pages/Account/Login.cshtml.cs`

- Standard `PasswordSignInAsync` with Email, Password, RememberMe.
- `lockoutOnFailure: false` — account lockout is effectively disabled.
- Google external login button present in the Login view.

### 1.3 External Login (Google)

**File:** `Areas/Identity/Pages/Account/ExternalLogin.cshtml.cs`

- Google is configured in `Program.cs` lines 42–52, gated behind presence of `Authentication:Google:ClientId` and `ClientSecret` in config. The app bootstraps without them (the Google button simply does not appear).
- Custom override of the Identity UI ExternalLogin page: on first Google sign-in, it **silently creates a local account** from the Google email and signs in — no intermediate "confirm email / Register" page.
- If a local account with that email already exists, it reuses it and links the external login.
- Concurrency-safe: catches `DbUpdateException` on insert (unique email index) and re-fetches.

### 1.4 Password Policy

From `Program.cs` lines 29–34:

```csharp
options.SignIn.RequireConfirmedAccount = false;
options.Password.RequireNonAlphanumeric = false;
options.Password.RequireUppercase = false;
options.Password.RequiredLength = 6;
```

Effective requirements (combining relaxed settings with Identity defaults):
| Constraint | Value |
|---|---|
| Minimum length | 6 characters |
| Require digit | Yes (default, not overridden) |
| Require lowercase | Yes (default, not overridden) |
| Require uppercase | No (explicitly disabled) |
| Require non-alphanumeric | No (explicitly disabled) |
| Email confirmation | No (explicitly disabled) |

This is a relaxed policy suitable for a lab/tool environment — no email verification, no complex passwords.

---

## 2. AppUser Extended Fields Analysis

**File:** `Models/Entities/AppUser.cs`

```csharp
public class AppUser : IdentityUser
{
    public string? ProfilePicturePath { get; set; }
    public string? OriginalFileName { get; set; }
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? UploadedAt { get; set; }
}
```

| Extended Field | Type | Purpose | Set During Registration? |
|---|---|---|---|
| `ProfilePicturePath` | `string?` | Web-relative path to stored image | No |
| `OriginalFileName` | `string?` | Original filename at upload time | No |
| `ContentType` | `string?` | MIME type (jpeg/png/gif/webp) | No |
| `SizeBytes` | `long?` | File size in bytes | No |
| `UploadedAt` | `DateTime?` | Upload timestamp | No |

**Key observations:**
- All five extended fields are profile-picture metadata only. They get populated via the `/Account/Profile` page (Dropzone upload), `AccountController.UploadProfilePicture` and `DeleteProfilePicture`.
- **Missing business fields:** There is no `FirstName`, `LastName`, `DisplayName`, or other business-relevant user field. `UserName` defaults to the email address.
- The `AppDbContext` inherits from `IdentityDbContext<AppUser>`, which generates all standard Identity tables (AspNetUsers, AspNetRoles, AspNetUserRoles, AspNetRoleClaims, AspNetUserClaims, AspNetUserLogins, AspNetUserTokens).

### Database Schema (from Migration)

**File:** `Migrations/20260611142929_AddIdentity.cs`

The `AspNetUsers` table includes all `IdentityUser` columns plus the five `AppUser` columns:

| Column | Type | Nullable |
|---|---|---|
| `ProfilePicturePath` | `longtext` | Yes |
| `OriginalFileName` | `longtext` | Yes |
| `ContentType` | `longtext` | Yes |
| `SizeBytes` | `bigint` | Yes |
| `UploadedAt` | `datetime(6)` | Yes |

---

## 3. Role System Analysis

### 3.1 Roles Created

Three roles are seeded at startup in `Program.cs` lines 156–165:

```csharp
foreach (var role in new[] { "Admin", "Manager", "User" })
{
    if (!await roleManager.RoleExistsAsync(role))
        await roleManager.CreateAsync(new IdentityRole(role));
}
```

| Role | Purpose | Used in `[Authorize]`? |
|---|---|---|
| **Admin** | System administration, user management, full CRUD | Yes — AdminController, all data controllers |
| **Manager** | Data entry, extraction, content management | Yes — all data controllers, ExtractionController |
| **User** | Generic authenticated user | **No** — never referenced in any `[Authorize(Roles = "...")]` attribute |

### 3.2 Role Seeding

- Runs at application startup inside a `using (var scope = app.Services.CreateScope())` block.
- Idempotent: checks `roleManager.RoleExistsAsync(role)` before creating.
- Seeding happens **after** `app.UseAuthentication()` and `app.UseAuthorization()` middleware registration, but that is fine — middleware registration is declarative; the check runs before any request arrives.

### 3.3 Role Management UI

**File:** `Controllers/AdminController.cs`

The Admin area (`[Authorize(Roles = "Admin")]`):
- **Index** — lists all users with their assigned roles
- **EditRoles** — GET shows checkboxes for all roles; POST diffs current vs. selected and adds/removes accordingly
- **Delete** — deletes a user (prevents self-deletion)

---

## 4. Authorization Audit — Per-Controller Breakdown

### 4.1 Totals

| Category | Controllers |
|---|---|
| `[AllowAnonymous]` (fully public) | 4 |
| `[Authorize]` (any authenticated user) | 1 |
| `[Authorize(Roles = "Admin")]` | 1 |
| `[Authorize(Roles = "Admin,Manager")]` with per-action `[AllowAnonymous]` on reads | 10 |
| `[Authorize(Roles = "Admin,Manager")]` without any anonymous overrides | 1 |

### 4.2 Controllers with `[AllowAnonymous]` (Fully Public)

| Controller | Actions | Notes |
|---|---|---|
| **HomeController** | Index, Privacy, Error | Landing page, privacy policy, error page |
| **TickerController** (API) | Feed (`/api/ticker/feed`) | JSON feed for the scrolling ticker bar |
| **GraphController** | Index, CompanyGraph | Company graph visualization |
| **ImpactController** | Index, Solve | Event-impact simulator |

### 4.3 Controller with `[Authorize]` (Authenticated Only, No Role Check)

| Controller | Actions | Notes |
|---|---|---|
| **AccountController** | Profile, CurrentPicture, UploadProfilePicture, DeleteProfilePicture | Any signed-in user can manage their own profile picture |

### 4.4 Controller with `[Authorize(Roles = "Admin")]`

| Controller | Actions | Notes |
|---|---|---|
| **AdminController** | Index, EditRoles (GET/POST), Delete | Only Admins can manage users and roles |

### 4.5 Controllers with `[Authorize(Roles = "Admin,Manager")]` + `[AllowAnonymous]` on Read Actions

All ten follow the same pattern: class-level `[Authorize(Roles = "Admin,Manager")]` with per-action `[AllowAnonymous]` on read-only endpoints. Create/Edit/Delete require Admin or Manager.

| Controller | Anonymous Actions | Role-Gated Actions |
|---|---|---|
| **TradeBlocsController** | Index, Search, Details | Create (GET/POST), Edit (GET/POST), Delete |
| **EventsController** | Index, Search, Details | Create (GET/POST), Edit (GET/POST), Delete |
| **CountriesController** | Index, Search, Lookup, Details | Create (GET/POST), Edit (GET/POST), Delete |
| **CompaniesController** | Index, Search, Lookup, Details, LinkedSources | Create, Fetch, DiscoverPrivate, Rediscover, Backfill, Edit (GET/POST), Delete |
| **RevenueSourcesController** | Index, Search, Details | Create, Edit (GET/POST), Delete, DetachReview, SetReviewFiling |
| **CostSourcesController** | Index, Search, Details | Create, Edit (GET/POST), Delete |
| **CountryAdvantagesController** | Index, Search | Create, Edit (GET/POST), Delete |
| **CountryChallengesController** | Index, Search | Create, Edit (GET/POST), Delete |
| **CountryDetailsController** | Index, Search | Create, Edit (GET/POST), Delete, ValidateCountry |
| **GdpSnapshotsController** | Index, Search | Create, Edit (GET/POST), Delete |

### 4.6 Controller with `[Authorize(Roles = "Admin,Manager")]` Without Anonymous Overrides

| Controller | All Actions | Notes |
|---|---|---|
| **ExtractionController** | Index, References, Reference, Save, SaveBatch, Review, AutoExtract, ScanAuto, ScanAutoAsync, ScanJobs, DismissScanJob, ScanJobReply, ScanJobReplyState, Chat, DiscoverRelated, LinkCounterparty | Every action requires Admin or Manager role; no anonymous reads |

### 4.7 Authorization Summary Matrix

```
                         Anonymous  Authenticated  Admin+Manager  Admin Only
                         ─────────  ─────────────  ─────────────  ─────────
Home                       ALL
Ticker (API)               ALL
Graph                      ALL
Impact                     ALL
Account                                ALL
Admin                                                               ALL
Extraction                                         ALL
TradeBlocs                 READ                       CREATE/EDIT/DELETE
Events                     READ                       CREATE/EDIT/DELETE
Countries                  READ                       CREATE/EDIT/DELETE
Companies                  READ                       CREATE/EDIT/DELETE
RevenueSources             READ                       CREATE/EDIT/DELETE
CostSources                READ                       CREATE/EDIT/DELETE
CountryAdvantages          READ                       CREATE/EDIT/DELETE
CountryChallenges          READ                       CREATE/EDIT/DELETE
CountryDetails             READ                       CREATE/EDIT/DELETE
GdpSnapshots               READ                       CREATE/EDIT/DELETE
```

---

## 5. Gaps and Recommendations

### 5.1 "User" Role is Dead

The `"User"` role is seeded at startup but **never referenced** in any `[Authorize(Roles = "User")]` attribute. It has no functional effect. Every data-controller role gate uses `"Admin,Manager"`, and there is no action that a plain "User" can perform that an anonymous user cannot.

**Recommendation:** Either:
- Add `[Authorize(Roles = "Admin,Manager,User")]` on write actions so that any logged-in user (with the "User" role) can create/edit/delete, while anonymous users can only read; OR
- Remove the "User" role from seeding if it is not intended to be used.

### 5.2 No Extended Fields on Registration

The registration form collects only Email and Password. `AppUser` has five extended fields but none appear in the register flow. This is not necessarily a gap — profile-picture fields are inherently post-registration — but if user metadata (name, phone, department) is needed, it must be added to both `Register.cshtml.cs` (create flow) and optionally to the `Profile` page.

**Recommendation:** If the project requires Name or other user metadata, add fields to `AppUser`, the Register `InputModel`, and the Register POST handler.

### 5.3 Account Lockout Disabled

`lockoutOnFailure: false` in `Login.cshtml.cs` line 60 means brute-force password guessing is not mitigated by Identity's built-in lockout mechanism.

**Recommendation:** Enable lockout for production scenarios (`lockoutOnFailure: true`) and configure `options.Lockout` settings in `Program.cs`.

### 5.4 Lockout and Email Confirmation Pages Exist but Unreachable

The Login view links to `ForgotPassword`, and the code references `Lockout` and `EmailConfirmation` pages (via `RedirectToPage("./Lockout")` and `EmailConfirmed = true` in ExternalLogin). These pages are part of the Identity UI RCL and work if navigated to, but:
- Email confirmation is disabled (`RequireConfirmedAccount = false`), so the confirmation page is never triggered naturally.
- Lockout is unreachable because `lockoutOnFailure: false`.

### 5.5 AdminController Delete Returns 400 Instead of Forbidden

`AdminController.Delete` (line 87) returns `BadRequest("You cannot delete your own account.")` when an admin tries to self-delete. `BadRequest` semantically means "malformed request" — a `403 Forbidden` or `409 Conflict` would be more appropriate.

### 5.6 Consistent Pattern Across Data Controllers

The authorization pattern is strong and coherent: class-level `[Authorize(Roles = "Admin,Manager")]` with per-action `[AllowAnonymous]` on read actions. This cleanly separates "viewing" (public) from "creating/editing/deleting" (privileged). All ten data controllers follow this pattern consistently.

### 5.7 Google External Login — Credentials in User Secrets

Google OAuth credentials come from configuration (`Authentication:Google:ClientId`, `Authentication:Google:ClientSecret`). The app works without them. This is the correct pattern for development — credentials should be stored in .NET User Secrets, environment variables, or a production secret store, not in `appsettings.json`.

### 5.8 ExtractionController is Fully Role-Gated

The `ExtractionController` (the AI-powered filing extraction and review system) requires `Admin` or `Manager` for every action, including GET requests. This is stricter than the other data controllers which allow anonymous reads. This seems intentional — the extraction pipeline involves AI operations (DeepSeek API calls) and data modifications, making it appropriate to gate entirely.

---

## 6. Summary

The ASP.NET Core Identity implementation in this project is **functional and well-structured**:

| Requirement | Status | Notes |
|---|---|---|
| Local registration with email/password | Done | Standard Identity UI, relaxed password policy |
| Google external login | Done | Conditionally configured, auto-creates local account |
| `AppUser` with extended fields | Done | 5 profile-picture fields, set via Profile page |
| `IdentityDbContext<AppUser>` | Done | Full Identity schema with custom columns |
| Role seeding | Done | 3 roles seeded (Admin, Manager, User) |
| Role-based authorization | Done | Admin + Manager gates on all data operations |
| Admin role implementation | Done | AdminController + checked in `_LoginPartial` |
| Read actions public | Done | All data controllers override with `[AllowAnonymous]` on reads |
| Write actions privileged | Done | Create/Edit/Delete require Admin or Manager |

**Main gap:** The "User" role exists but is never used. Any "User"-role holder has the same permissions as an anonymous visitor.
