namespace simple_bloomberg_terminal.Models.ViewModels;

// One row in the admin user list.
public class AdminUserRow
{
    public string Id { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? UserName { get; set; }
    public string? ProfilePicturePath { get; set; }
    public IList<string> Roles { get; set; } = new List<string>();
}

// Role-assignment form: the user plus every role with a "selected" flag bound to a checkbox.
public class EditRolesViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public List<RoleCheckbox> Roles { get; set; } = new();
}

public class RoleCheckbox
{
    public string Name { get; set; } = string.Empty;
    public bool Selected { get; set; }
}
