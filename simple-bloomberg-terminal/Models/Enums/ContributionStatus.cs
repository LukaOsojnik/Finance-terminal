namespace simple_bloomberg_terminal.Models.Enums;

// Review state for a user-contributed row (web-searched / LLM-parsed revenue, cost, or risk).
// Approved is 0 so every existing row — and every admin/API write that doesn't set it — is live by
// default; only user contributions are flipped to Pending and held back until a Manager rules on them.
public enum ContributionStatus
{
    Approved = 0,
    Pending,
    Rejected
}
