using System.Globalization;

namespace simple_bloomberg_terminal.Models.ViewModels.Shared;

public class DateTimePickerModel
{
    public string FieldName { get; set; } = "";
    public string IsoValue { get; set; } = "";
    public string FormattedValue { get; set; } = "";
    public string Format { get; set; } = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;
    public string Locale { get; set; } = CultureInfo.CurrentCulture.Name;
    public bool Required { get; set; }
    public string ErrorMessage { get; set; } = "Please select a date.";
    public bool ShowTime { get; set; }
    public string Placeholder { get; set; } = "";

    public Dictionary<string, string> CustomValidationAttributes { get; set; } = new();
}
