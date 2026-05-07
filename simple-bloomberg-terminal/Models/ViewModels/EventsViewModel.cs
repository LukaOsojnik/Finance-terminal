using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class EventsIndexViewModel
{
    public IEnumerable<EventRowViewModel> LiveEvents { get; set; } = [];
    public IEnumerable<EventRowViewModel> PastEvents { get; set; } = [];
}
