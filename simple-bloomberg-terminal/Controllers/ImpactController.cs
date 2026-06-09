using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Services;

namespace simple_bloomberg_terminal.Controllers;

/// <summary>
/// The event-impact simulator page. Follows the GraphController pattern: one MVC controller that
/// both renders the interactive view and serves its JSON data action (avoids the MVC/API
/// controller-name collision that would route links to an API verb).
/// </summary>
[Route("impact")]
public class ImpactController(EventImpactService impact) : Controller
{
    [HttpGet, Route("", Name = "ImpactIndex")]
    public IActionResult Index()
    {
        ViewBag.Profiles = impact.Profiles();
        return View();
    }

    /// <summary>Run one cascade and return the ranked sectors, per-sector companies, and trace.</summary>
    [HttpPost, Route("solve", Name = "ImpactSolve")]
    public IActionResult Solve([FromBody] ImpactRequest req)
    {
        if (!Enum.TryParse<Sector>(req.Sector, out var sector))
            return BadRequest($"Unknown sector '{req.Sector}'.");
        if (!Enum.TryParse<ImpactKind>(req.Kind, ignoreCase: true, out var kind))
            return BadRequest($"Unknown kind '{req.Kind}'.");

        return Json(impact.Solve(kind, sector, req.Magnitude));
    }

    public sealed record ImpactRequest(string Kind, string Sector, double Magnitude);
}
