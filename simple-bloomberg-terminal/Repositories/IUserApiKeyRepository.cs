using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

/// <summary>
/// Persistence for the 1:1 <see cref="UserApiKey"/> row (encrypted columns). The only place that
/// queries the table; <c>UserApiKeyProvider</c> owns the HttpContext/cache/crypto and delegates the
/// database I/O here so it stays EF-free.
/// </summary>
public interface IUserApiKeyRepository
{
    // Untracked read for the per-request resolve path (read-only).
    Task<UserApiKey?> GetAsync(string userId, CancellationToken ct = default);

    // Insert the row if absent, otherwise update it. Caller has already set the (encrypted) columns.
    Task UpsertAsync(UserApiKey row, CancellationToken ct = default);
}
