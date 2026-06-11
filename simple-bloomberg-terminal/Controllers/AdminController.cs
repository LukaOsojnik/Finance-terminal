using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Controllers;

// Admin-only user management: list users, assign roles, delete users. Role gate on the whole
// controller, so every action requires the Admin role.
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly UserManager<AppUser> _users;
    private readonly RoleManager<IdentityRole> _roles;

    public AdminController(UserManager<AppUser> users, RoleManager<IdentityRole> roles)
    {
        _users = users;
        _roles = roles;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var rows = new List<AdminUserRow>();
        foreach (var u in _users.Users.ToList())
        {
            rows.Add(new AdminUserRow
            {
                Id = u.Id,
                Email = u.Email,
                UserName = u.UserName,
                ProfilePicturePath = u.ProfilePicturePath,
                Roles = await _users.GetRolesAsync(u)
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
        await _users.AddToRolesAsync(user, selected.Except(current));
        await _users.RemoveFromRolesAsync(user, current.Except(selected));

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

        await _users.DeleteAsync(user);
        return RedirectToAction(nameof(Index));
    }
}
