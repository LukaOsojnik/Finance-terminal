using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;
using System.Text;

namespace simple_bloomberg_terminal.Controllers;

// Admin-only user management: list users, assign roles, delete users. Role gate on the whole
// controller, so every action requires the Admin role.
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly UserManager<AppUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly ILogger<AdminController> _logger;
    private readonly IWebHostEnvironment _env;

    public AdminController(UserManager<AppUser> users, RoleManager<IdentityRole> roles,
        ILogger<AdminController> logger, IWebHostEnvironment env)
    {
        _users = users;
        _roles = roles;
        _logger = logger;
        _env = env;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // Project HasPicture in the query (translates to IS NOT NULL) so we don't load every user's
        // image bytes just to render the list. Roles are still fetched per user as before.
        var users = _users.Users
            .Select(u => new { u.Id, u.Email, u.UserName, HasPicture = u.ProfilePictureData != null })
            .ToList();

        var rows = new List<AdminUserRow>();
        foreach (var u in users)
        {
            rows.Add(new AdminUserRow
            {
                Id = u.Id,
                Email = u.Email,
                UserName = u.UserName,
                HasPicture = u.HasPicture,
                Roles = await _users.GetRolesAsync(new AppUser { Id = u.Id })
            });
        }
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> EditRoles(string userId)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null) return NotFound();

        var assigned = await _users.GetRolesAsync(user);
        var vm = new EditRolesViewModel
        {
            UserId = user.Id,
            Email = user.Email,
            Roles = _roles.Roles
                .Select(r => r.Name!)
                .OrderBy(n => n)
                .Select(name => new RoleCheckbox { Name = name, Selected = assigned.Contains(name) })
                .ToList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditRoles(EditRolesViewModel model)
    {
        var user = await _users.FindByIdAsync(model.UserId);
        if (user is null) return NotFound();

        var selected = model.Roles.Where(r => r.Selected).Select(r => r.Name).ToList();
        var current = await _users.GetRolesAsync(user);

        // Diff current vs selected — only touch the roles that actually changed.
        var added = selected.Except(current).ToList();
        var removed = current.Except(selected).ToList();
        await _users.AddToRolesAsync(user, added);
        await _users.RemoveFromRolesAsync(user, removed);
        _logger.LogInformation("Admin {Actor} updated roles for {Target}: +[{Added}] -[{Removed}]",
            _users.GetUserId(User), model.UserId, string.Join(",", added), string.Join(",", removed));

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string userId)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null) return NotFound();

        // Don't let an admin delete their own account out from under the current session.
        if (user.Id == _users.GetUserId(User))
            return BadRequest("You cannot delete your own account.");

        _logger.LogWarning("Admin {Actor} deleted user {Target} ({Email})",
            _users.GetUserId(User), user.Id, user.Email);
        await _users.DeleteAsync(user);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Logs() => View();

    // SSE endpoint: sends the last 200 lines of today's log file, then tails live.
    [HttpGet]
    public async Task LogStream(CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var logDir = Path.Combine(_env.ContentRootPath, "logs");
        var logFile = Directory.Exists(logDir)
            ? Directory.GetFiles(logDir, "app-*.log").OrderByDescending(f => f).FirstOrDefault()
            : null;

        if (logFile is null)
        {
            await Response.WriteAsync("data: [no log file yet — trigger any action to start logging]\n\n", ct);
            await Response.Body.FlushAsync(ct);
            return;
        }

        using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Send last 200 lines as history before tailing.
        var history = TailLines(logFile, 200);
        foreach (var line in history)
        {
            await Response.WriteAsync($"data: {line}\n\n", ct);
        }
        await Response.Body.FlushAsync(ct);

        // Seek to end and tail live.
        stream.Seek(0, SeekOrigin.End);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is not null)
            {
                await Response.WriteAsync($"data: {line}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            else
            {
                await Task.Delay(300, ct);
            }
        }
    }

    private static List<string> TailLines(string path, int count)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null) lines.Add(line);
        return lines.Count > count ? lines.GetRange(lines.Count - count, count) : lines;
    }
}
