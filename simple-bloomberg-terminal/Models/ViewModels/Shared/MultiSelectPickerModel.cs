namespace simple_bloomberg_terminal.Models.ViewModels.Shared;

public class MultiSelectPickerModel
{
    // Form key for every selected id. Repeated keys bind to a List<long>.
    public string FieldName { get; set; } = "";
    public string Legend { get; set; } = "";
    public string Placeholder { get; set; } = "Type to filter...";
    public List<MultiSelectOption> Options { get; set; } = new();
    public List<long> SelectedIds { get; set; } = new();

    // Optional cascade: only show options whose ParentId is selected in the
    // picker bound to ParentField. Used so companies are gated by chosen countries.
    public string? ParentField { get; set; }
    public string ParentEmptyPlaceholder { get; set; } = "Select a parent first...";
}

public class MultiSelectOption
{
    public long Id { get; set; }
    public string Label { get; set; } = "";

    // When the picker has a ParentField, this is the id of the owning parent option.
    public long? ParentId { get; set; }
}
