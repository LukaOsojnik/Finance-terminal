using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class TradeBlocCreateModel
{
    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(16)]
    public string Code { get; set; } = string.Empty;

    public string? Description { get; set; }
    public DateOnly? FoundedDate { get; set; }

    public List<long> SelectedCountryIds { get; set; } = new();
}

public class TradeBlocEditModel : TradeBlocCreateModel
{
    public long Id { get; set; }
}
