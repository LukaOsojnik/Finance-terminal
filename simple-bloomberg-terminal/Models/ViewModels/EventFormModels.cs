using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels.Validation;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class EventCreateModel
{
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public EventType? Type { get; set; }

    [Required]
    public DateTime? Date { get; set; }

    [DateGreaterThanOrEqual(nameof(Date), ErrorMessage = "End date must be on or after start date.")]
    public DateTime? EndDate { get; set; }

    public string? Description { get; set; }

    [Range(-10, 10)]
    public double? ImpactScore { get; set; }

    public List<long> SelectedCountryIds { get; set; } = new();
    public List<long> SelectedCompanyIds { get; set; } = new();
    public List<long> SelectedTradeBlocIds { get; set; } = new();
}

public class EventEditModel : EventCreateModel
{
    public long Id { get; set; }
}
