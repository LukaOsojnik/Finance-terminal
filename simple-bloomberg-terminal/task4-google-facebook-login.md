# Task 4 Analysis: Google / Facebook Login

## 1. What External Auth Is Configured

**Google only.** No Facebook authentication is configured anywhere in the project.

- Google is wired via `AddGoogle()` in `Program.cs` (lines 42-52), conditionally registered only when both `ClientId` and `ClientSecret` are present in configuration.
- The `Microsoft.AspNetCore.Authentication.Google` NuGet package (v9.0.0) is present in the csproj.
- The `Microsoft.AspNetCore.Authentication.Facebook` NuGet package is **absent** from the csproj.
- No Facebook-related configuration keys exist in any config source (appsettings.json, user-secrets, environment variables).
- No Facebook-specific code exists anywhere in the project.

---

## 2. Configuration Analysis

### 2.1 appsettings.json
Contains no `Authentication` section at all. Google keys are deliberately excluded from source control.

### 2.2 User Secrets (dotnet user-secrets)
Both required Google credentials are present, verified via `dotnet user-secrets list`:

```
Authentication:Google:ClientId    = 241645727342-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com
Authentication:Google:ClientSecret = GOCSPX-xxxxxxxxxxxxxxxxxxxx
```

These are **real Google OAuth credentials** (redacted above). The project holds valid credentials for a Google Cloud project with numeric ID `241645727342`.

### 2.3 Program.cs Guard Logic (lines 42-52)
```csharp
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
        });
}
```

Smart guard -- if the credentials aren't present (e.g., user-secrets not initialized on a fresh clone), the app still boots but the Google button simply won't render.

### 2.4 Integration Tests
`docs/tests.md` shows that integration tests provide mock Google config via in-memory configuration:
```csharp
["Authentication:Google:ClientId"] = "test-client-id",
["Authentication:Google:ClientSecret"] = "test-client-secret"
```

### 2.5 Missing Facebook Config Keys
Neither `Authentication:Facebook:AppId` nor `Authentication:Facebook:AppSecret` exist in any config layer.

---

## 3. Login Page External Button Analysis

### 3.1 Login page (`Areas/Identity/Pages/Account/Login.cshtml`)
- Renders external login buttons dynamically when `Model.ExternalLogins.Count > 0` (line 38).
- Uses a `foreach` loop (lines 42-54) over registered providers, rendering one button per provider.
- Each button posts to `./ExternalLogin` with the provider name and return URL.
- The button has the CSS class `google-btn` and a hardcoded Google SVG logo -- **this is a cosmetic issue** because if Facebook (or any other provider) were added, it would still render with the Google logo.
- The button displays `@provider.DisplayName` text alongside the Google SVG icon.

### 3.2 Register page (`Areas/Identity/Pages/Account/Register.cshtml`)
- Same pattern: renders external login buttons when `Model.ExternalLogins.Count > 0` (line 37).
- Same `google-btn` class and Google SVG logo issue.

### 3.3 Login code-behind (`Areas/Identity/Pages/Account/Login.cshtml.cs`)
- `ExternalLogins` is populated via `_signInManager.GetExternalAuthenticationSchemesAsync()` (line 49).
- This returns only schemes registered in `Program.cs`, so only Google appears.

### 3.4 Register code-behind (`Areas/Identity/Pages/Account/Register.cshtml.cs`)
- Same population mechanism (line 51).

---

## 4. ExternalLogin Callback Flow Analysis

### 4.1 Custom Override
The project does **not** use the default Identity UI ExternalLogin page. It provides a custom override at:
`Areas/Identity/Pages/Account/ExternalLogin.cshtml.cs`

### 4.2 Flow

1. **OnPost** (line 36-40): User clicks Google button on Login/Register page.
   - Calls `_signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl)`.
   - Returns a `ChallengeResult` that redirects the browser to Google's OAuth consent screen.
   - The `redirectUrl` points back to `./ExternalLogin?handler=Callback`.

2. **OnGetCallbackAsync** (line 44-117): Google redirects back after user consent.
   - Checks for `remoteError` (user denied consent) -- redirects back to Login with error.
   - Calls `_signInManager.GetExternalLoginInfoAsync()` to extract the Google identity.
   - **Already-linked user**: calls `ExternalLoginSignInAsync` -- signs in immediately.
   - **First-time user**: extracts `ClaimTypes.Email` from Google's response.
     - If email missing, returns error (rare but handled).
     - Looks up existing local account by email, or creates a new `AppUser`.
     - Calls `_userManager.AddLoginAsync(user, info)` to link Google identity to local account.
     - Signs in the user.
   - Has a `DbUpdateException` catch for concurrent-registration race conditions.

### 4.3 Key Design Decision: Silent Account Creation
On first Google sign-in, the app **auto-creates a local account** from the Google email without any "confirm your email / associate with existing account" step. This is a deliberate simplification noted in the file comment (lines 11-12):
> "on first Google sign-in we silently create the local account from the Google email and sign in -- no confirm your email / Register step."

### 4.4 ExternalLogin.cshtml (Razor View)
Minimal -- only renders an error message if a redirect is missed. On the happy path, the browser is always redirected before the page renders.

### 4.5 Google OAuth Redirect URI
The redirect URI that must be registered in the Google Cloud Console is:
```
https://localhost:{PORT}/Identity/Account/ExternalLogin?handler=Callback
```
The port number depends on the launch profile (typically 5001 for HTTPS or a random port via `dotnet run`).

---

## 5. Package References Analysis

| Package | Version | Purpose | Present? |
|---------|---------|---------|----------|
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 9.0.0 | EF Core stores for Identity | Yes |
| `Microsoft.AspNetCore.Identity.UI` | 9.0.0 | Default Identity Razor UI (RCL) | Yes |
| `Microsoft.AspNetCore.Authentication.Google` | 9.0.0 | Google OAuth handler | **Yes** |
| `Microsoft.AspNetCore.Authentication.Facebook` | N/A | Facebook OAuth handler | **No** |
| `Microsoft.AspNetCore.Authentication.MicrosoftAccount` | N/A | Microsoft Account OAuth | No |

No other external authentication packages are referenced. Only Google authentication is supported.

---

## 6. Testing / Verification Notes

### 6.1 Prerequisites for Manual Testing
- The app must be running with HTTPS (required by Google OAuth).
- The Google Cloud Console OAuth 2.0 client must have the correct redirect URI authorized:
  - `https://localhost:5001/Identity/Account/ExternalLogin?handler=Callback` (if using default HTTPS port).
  - If a different port is used, the URI must match exactly.
- The user running the test must be added as a test user in the Google Cloud Console (if the OAuth consent screen is in "Testing" mode).

### 6.2 Manual Test Steps
1. Run the app: `dotnet run`
2. Navigate to `/Identity/Account/Login`
3. The "Sign in with Google" button should appear below the "or" divider.
4. Click the button -- should redirect to Google's consent screen.
5. After consent, should redirect back and be signed in.

### 6.3 What Could Fail
- If the Google Cloud project has the wrong redirect URI configured, Google will show a `redirect_uri_mismatch` error.
- If the user-secrets are not loaded (e.g., on a fresh machine), the Google button won't render at all because the guard in Program.cs prevents registration.
- If the Google OAuth consent screen is in "Testing" mode and the user is not a test user, authentication will be rejected.

### 6.4 Integration Tests
The project's test infrastructure provides mock Google config values, but there is no existing test for the external login flow specifically. The `docs/tests.md` file mentions that external integrations should be mocked.

### 6.5 User-Secrets Verification
Run this to verify the secrets are present locally:
```
dotnet user-secrets list
```
Expected keys: `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret`.

---

## 7. Gaps and Recommendations

### 7.1 Google Login: Ready to Use
Google login is **fully implemented and configured**. The code is complete, the credentials are in user-secrets, and the callback flow handles both first-time and returning users. **No additional work is needed for Google login to work**, assuming the Google Cloud Console OAuth client has the correct redirect URI registered.

### 7.2 Facebook Login: Not Implemented
Facebook login is **not implemented at all**. If Facebook login is required:
1. Add the NuGet package:
   ```
   dotnet add package Microsoft.AspNetCore.Authentication.Facebook --version 9.0.0
   ```
2. Add Facebook registration in `Program.cs`, similar to Google's pattern:
   ```csharp
   var fbAppId = builder.Configuration["Authentication:Facebook:AppId"];
   var fbAppSecret = builder.Configuration["Authentication:Facebook:AppSecret"];
   if (!string.IsNullOrWhiteSpace(fbAppId) && !string.IsNullOrWhiteSpace(fbAppSecret))
   {
       builder.Services.AddAuthentication().AddFacebook(options =>
       {
           options.AppId = fbAppId;
           options.AppSecret = fbAppSecret;
       });
   }
   ```
3. Register the Facebook app and store credentials in user-secrets:
   ```
   dotnet user-secrets set "Authentication:Facebook:AppId" "<your-fb-app-id>"
   dotnet user-secrets set "Authentication:Facebook:AppSecret" "<your-fb-app-secret>"
   ```
4. Add the Facebook redirect URI in the Facebook App dashboard.

### 7.3 Cosmetic Issue: Hardcoded Google SVG Icon
The external login button on both `Login.cshtml` and `Register.cshtml` uses a hardcoded Google SVG logo and the CSS class `google-btn`. If Facebook is added, the button would show the Google icon with "Sign in with Facebook" text. **To fix**: Generate provider-specific icons dynamically (e.g., by provider name via a switch/if-else on `provider.Name`), or use a neutral/universal icon.

### 7.4 Recommended: Verify Google Credentials Work End-to-End
Since the task requires login to "raditi" (work), the single most important verification step is:
1. Check what port the app runs on in the launch profile (`Properties/launchSettings.json`).
2. Verify that URI is registered in the Google Cloud Console under `APIs & Services > Credentials > OAuth 2.0 Client IDs > Authorized redirect URIs`.
3. Run the app and test the full flow manually.

### 7.5 Summary

| Requirement | Status |
|-------------|--------|
| Google login configured in Program.cs | Done |
| Google credentials in user-secrets | Done |
| Google login button on Login page | Done |
| Google login button on Register page | Done |
| Custom ExternalLogin callback with silent account creation | Done |
| Facebook NuGet package | **Not present** |
| Facebook configuration in Program.cs | **Not present** |
| Facebook credentials in user-secrets | **Not present** |
| Provider-agnostic external login button icons | Needs improvement (hardcoded Google SVG) |
