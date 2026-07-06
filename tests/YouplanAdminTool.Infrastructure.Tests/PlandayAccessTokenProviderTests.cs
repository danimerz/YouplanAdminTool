using MsOptions = Microsoft.Extensions.Options.Options;
using YouplanAdminTool.Infrastructure.Auth;
using YouplanAdminTool.Infrastructure.Options;

namespace YouplanAdminTool.Infrastructure.Tests;

public class PlandayAccessTokenProviderTests
{
    private static PlandayOptions CreateOptions() => new()
    {
        ClientId = "test-client",
        RefreshToken = "test-refresh-token",
        AuthBaseUrl = "https://id.planday.test",
    };

    [Fact]
    public async Task FetchesAndCachesAccessToken()
    {
        var handler = StubHttpMessageHandler.ReturningJson("""{"access_token":"token-1","expires_in":3600}""");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://id.planday.test") };
        var sut = new PlandayAccessTokenProvider(httpClient, MsOptions.Create(CreateOptions()));

        var first = await sut.GetAccessTokenAsync();
        var second = await sut.GetAccessTokenAsync();

        Assert.Equal("token-1", first);
        Assert.Equal("token-1", second);
        Assert.Equal(1, handler.RequestCount); // zweiter Aufruf kommt aus dem Cache
    }

    [Fact]
    public async Task InvalidateForcesNewTokenFetch()
    {
        var responses = new Queue<string>(["""{"access_token":"token-1","expires_in":3600}""", """{"access_token":"token-2","expires_in":3600}"""]);
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(responses.Dequeue(), System.Text.Encoding.UTF8, "application/json"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://id.planday.test") };
        var sut = new PlandayAccessTokenProvider(httpClient, MsOptions.Create(CreateOptions()));

        var first = await sut.GetAccessTokenAsync();
        sut.Invalidate();
        var second = await sut.GetAccessTokenAsync();

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task RefetchesTokenAfterExpiry()
    {
        var responses = new Queue<string>([
            """{"access_token":"token-1","expires_in":0}""",
            """{"access_token":"token-2","expires_in":3600}"""
        ]);
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(responses.Dequeue(), System.Text.Encoding.UTF8, "application/json"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://id.planday.test") };
        var sut = new PlandayAccessTokenProvider(httpClient, MsOptions.Create(CreateOptions()));

        var first = await sut.GetAccessTokenAsync();
        var second = await sut.GetAccessTokenAsync();

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second); // expires_in=0 liegt sofort in der Vergangenheit (Puffer) -> neuer Fetch
        Assert.Equal(2, handler.RequestCount);
    }
}
