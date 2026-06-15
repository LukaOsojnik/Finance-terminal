using Microsoft.AspNetCore.Mvc.Rendering;

namespace simple_bloomberg_terminal.Controllers;

/// <summary>
/// Builds dropdown items from an enum. Value is always the enum name; the visible label
/// defaults to the name but accepts a transform (e.g. underscores → spaces for EventType).
/// Replaces the per-controller <c>Enum.GetValues().Select(new SelectListItem(...))</c> copies.
/// </summary>
public static class EnumSelect
{
    public static List<SelectListItem> Of<TEnum>(Func<TEnum, string>? label = null)
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>()
            .Select(t => new SelectListItem(label?.Invoke(t) ?? t.ToString(), t.ToString()))
            .ToList();
}
