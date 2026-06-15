using simple_bloomberg_terminal.Services;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Stand-in <see cref="IUserApiKeyProvider"/> that returns fixed keys, so the keyed clients see a
/// non-empty Bearer key (the scripted handler ignores its value). An <see cref="UserApiKeys.Empty"/>
/// instance reproduces the "user hasn't added a key" path that throws <see cref="MissingApiKeyException"/>.
/// </summary>
public sealed class FakeApiKeyProvider : IUserApiKeyProvider
{
    private UserApiKeys _keys;
    public FakeApiKeyProvider(UserApiKeys keys) => _keys = keys;

    public Task<UserApiKeys> GetAsync(CancellationToken ct = default) => Task.FromResult(_keys);
    public void Set(UserApiKeys keys) => _keys = keys;
    public Task SaveAsync(ApiKeyEdit edit, CancellationToken ct = default) => Task.CompletedTask;
}
