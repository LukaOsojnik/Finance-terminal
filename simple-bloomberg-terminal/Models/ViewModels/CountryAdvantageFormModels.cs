using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class CountryAdvantageCreateModel
{
    [Required]
    public long CountryId { get; set; }

    [Required, StringLength(500)]
    public string Text { get; set; } = string.Empty;
}

public class CountryAdvantageEditModel : CountryAdvantageCreateModel
{
    public long Id { get; set; }
}
