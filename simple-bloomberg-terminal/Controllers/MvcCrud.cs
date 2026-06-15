using Microsoft.AspNetCore.Mvc;

namespace simple_bloomberg_terminal.Controllers;

/// <summary>
/// Shared helpers for the repeated MVC CRUD shapes. Keeps controllers thin without a
/// leaky base class (see docs/improve-code.md 3.2).
/// </summary>
public static class MvcCrud
{
    /// <summary>
    /// Runs a soft-delete, surfacing a cascade-guard <see cref="InvalidOperationException"/> via
    /// <c>TempData["Error"]</c>, then redirects (defaults to <c>Index</c>). The caller passes the
    /// repo call as a closure so it keeps its own repository and route-param name.
    /// </summary>
    public static IActionResult SoftDeleteRedirect(this Controller controller, Action softDelete, string action = "Index")
    {
        try { softDelete(); }
        catch (InvalidOperationException ex) { controller.TempData["Error"] = ex.Message; }
        return controller.RedirectToAction(action);
    }
}
