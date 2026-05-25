using System.Net.Http;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Base for API integration tests. xUnit constructs a new test-class instance per test
/// method, so each test gets a brand-new factory with a fresh, seeded in-memory database
/// => tests are fully isolated even though some mutate (POST/PUT/DELETE).
/// </summary>
public abstract class ApiTestBase : IDisposable
{
    protected readonly CustomWebApplicationFactory Factory;
    protected readonly HttpClient Client;

    protected ApiTestBase()
    {
        Factory = new CustomWebApplicationFactory();
        Client = Factory.CreateClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }
}
