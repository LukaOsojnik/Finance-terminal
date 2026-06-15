using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class UserApiKeyRepository(AppDbContext db) : IUserApiKeyRepository
{
    public Task<UserApiKey?> GetAsync(string userId, CancellationToken ct = default) =>
        db.UserApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == userId, ct);

    public async Task UpsertAsync(UserApiKey row, CancellationToken ct = default)
    {
        var exists = await db.UserApiKeys.AnyAsync(k => k.UserId == row.UserId, ct);
        if (exists) db.UserApiKeys.Update(row);
        else db.UserApiKeys.Add(row);
        await db.SaveChangesAsync(ct);
    }
}
