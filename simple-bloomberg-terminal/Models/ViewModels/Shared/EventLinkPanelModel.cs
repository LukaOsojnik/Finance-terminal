namespace simple_bloomberg_terminal.Models.ViewModels.Shared;

// Right-side flyout panel for linking countries + companies to an event.
// Countries list on the left of the panel; clicking one reveals its companies.
public class EventLinkPanelModel
{
    public List<EventLinkCountry> Countries { get; set; } = new();
    public List<long> SelectedCountryIds { get; set; } = new();
    public List<long> SelectedCompanyIds { get; set; } = new();
}

public class EventLinkCountry
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public List<MultiSelectOption> Companies { get; set; } = new();
}
