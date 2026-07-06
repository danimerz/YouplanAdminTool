using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using YouplanAdminTool.Core.Abstractions;
using YouplanAdminTool.Infrastructure.Options;

namespace YouplanAdminTool.Infrastructure.Http;

/// <summary>Fügt jedem Planday-API-Aufruf die Header X-ClientId und Authorization hinzu.
/// Bei 401 wird das Access-Token einmalig erneuert und der Aufruf wiederholt; bei 429 wird
/// entsprechend dem Retry-After-Header einmalig gewartet und wiederholt.</summary>
public sealed class PlandayAuthHeaderHandler : DelegatingHandler
{
    private readonly IAccessTokenProvider _tokenProvider;
    private readonly PlandayOptions _options;

    public PlandayAuthHeaderHandler(IAccessTokenProvider tokenProvider, IOptions<PlandayOptions> options)
    {
        _tokenProvider = tokenProvider;
        _options = options.Value;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await SendWithAuthAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _tokenProvider.Invalidate();
            response.Dispose();
            response = await SendWithAuthAsync(request, cancellationToken);
        }

        if (response.StatusCode == (HttpStatusCode)429)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
            response = await SendWithAuthAsync(request, cancellationToken);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendWithAuthAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Remove("X-ClientId");
        request.Headers.Add("X-ClientId", _options.ClientId);

        // Cloning is required because HttpRequestMessage cannot be sent twice.
        var clonedRequest = await CloneRequestAsync(request);
        return await base.SendAsync(clonedRequest, cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content is not null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(contentBytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}
