namespace simple_bloomberg_terminal.Models.ViewModels.Shared;

public class AutocompletePickerModel
{
    public string FieldName { get; set; } = "";
    public long? SelectedId { get; set; }
    public string SelectedLabel { get; set; } = "";
    public string? LookupUrl { get; set; }
    public string Placeholder { get; set; } = "Type to search...";
    public bool Required { get; set; }
    public string ErrorMessage { get; set; } = "Please select a value.";

    // Optional [Remote]-style uniqueness AJAX check. When set, picker emits data-val-remote-* attrs.
    public string? RemoteUrl { get; set; }
    public string RemoteErrorMessage { get; set; } = "Value already in use.";
}
