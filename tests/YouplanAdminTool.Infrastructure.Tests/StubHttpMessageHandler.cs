using System.Net;

namespace YouplanAdminTool.Infrastructure.Tests;

/// <summary>Testdouble für HttpClient: liefert vorbereitete Antworten anhand der Anfrage-URL
/// und zählt, wie oft jede URL angefragt wurde.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public int RequestCount { get; private set; }
    public List<string?> RequestedUrls { get; } = [];

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public static StubHttpMessageHandler ReturningJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        RequestedUrls.Add(request.RequestUri?.ToString());
        return Task.FromResult(_responder(request));
    }
}
