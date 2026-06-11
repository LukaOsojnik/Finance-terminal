using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Areas.Identity.Pages.Account;

// Overrides the default Identity UI ExternalLogin page (app pages win over the Identity.UI RCL).
// Difference from the default: on first Google sign-in we silently create the local account from
// the Google email and sign in — no "confirm your email / Register" step.
[AllowAnonymous]
public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<AppUser> _signInManager;
    private readonly UserManager<AppUser> _userManager;
    private readonly ILogger<ExternalLoginModel> _logger;

    public ExternalLoginModel(
        SignInManager<AppUser> signInManager,
        UserManager<AppUser> userManager,
        ILogger<ExternalLoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet() => RedirectToPage("./Login");

    // The Google button on the Login page posts here with the provider name.
    public IActionResult OnPost(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return new ChallengeResult(provider, properties);
    }

    // Google redirects back here after consent.
    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl ??= Url.Content("~/");

        if (remoteError != null)
        {
            ErrorMessage = $"Error from external provider: {remoteError}";
            return RedirectToPage("./Login", new { returnUrl });
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            ErrorMessage = "Error loading external login information.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        // Already linked → sign in straight away.
        var result = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
        if (result.Succeeded)
        {
            _logger.LogInformation("{Name} signed in with {Provider}.", info.Principal.Identity?.Name, info.LoginProvider);
            return LocalRedirect(returnUrl);
        }
        if (result.IsLockedOut)
            return RedirectToPage("./Lockout");

        // Not linked yet → auto-create the local account from the Google email (no confirmation step).
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email))
        {
            ErrorMessage = "The external provider did not return an email address.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        // Reuse an existing local account with this email if one exists, otherwise create it.
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                ErrorMessage = string.Join(" ", createResult.Errors.Select(e => e.Description));
                return RedirectToPage("./Login", new { returnUrl });
            }
        }

        var addLoginResult = await _userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded)
        {
            ErrorMessage = string.Join(" ", addLoginResult.Errors.Select(e => e.Description));
            return RedirectToPage("./Login", new { returnUrl });
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        _logger.LogInformation("Created local account for {Email} via {Provider}.", email, info.LoginProvider);
        return LocalRedirect(returnUrl);
    }
}
