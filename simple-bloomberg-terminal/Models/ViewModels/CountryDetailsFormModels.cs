using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class CountryDetailsCreateModel
{
    [Required]
    [Remote(action: "ValidateCountry", controller: "CountryDetails",
            ErrorMessage = "This country already has details.")]
    public long CountryId { get; set; }

    [Required, StringLength(500)]
    public string MarketPosition { get; set; } = string.Empty;
}

public class CountryDetailsEditModel
{
    public long CountryId { get; set; }

    [Required, StringLength(500)]
    public string MarketPosition { get; set; } = string.Empty;
}
