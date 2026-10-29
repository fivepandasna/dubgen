using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Dubtitles.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Dubtitles;

/// <summary>Sends the optional pre-run / post-run webhooks (wake up / shut down a remote Subgen machine).</summary>
internal static class WebhookClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>
    /// Sends one webhook. Returns true on a 2xx response. Never throws for network or HTTP errors
    /// (they are logged and false is returned); only rethrows cancellation of <paramref name="ct"/>.
    /// The URL is never logged beyond its host, because webhook URLs often carry secrets in the path or query.
    /// </summary>
    public static async Task<bool> SendAsync(WebhookSettings settings, string eventName, ILogger logger, CancellationToken ct)
    {
        if (!settings.Enabled)
        {
            return false;
        }

        if (!Uri.TryCreate(settings.Url?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogWarning("Dubtitles: {Event} webhook is enabled but its URL is missing or not a valid http(s) URL", eventName);
            return false;
        }

        var methodName = string.IsNullOrWhiteSpace(settings.Method) ? "POST" : settings.Method.Trim().ToUpperInvariant();
        var method = new HttpMethod(methodName);

        try
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.TryAddWithoutValidation("X-Dubtitles-Event", eventName);

            if (!string.IsNullOrWhiteSpace(settings.BearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.BearerToken.Trim());
            }

            if (!string.IsNullOrWhiteSpace(settings.Body) && method != HttpMethod.Get && method != HttpMethod.Head)
            {
                request.Content = new StringContent(settings.Body, Encoding.UTF8, "application/json");
            }

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Dubtitles: {Event} webhook sent to {Host} ({Status})", eventName, uri.Host, (int)response.StatusCode);
                return true;
            }

            logger.LogWarning("Dubtitles: {Event} webhook to {Host} returned {Status}", eventName, uri.Host, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // HttpRequestException = unreachable / refused; OperationCanceledException without ct = the 60s timeout.
            logger.LogWarning("Dubtitles: {Event} webhook to {Host} failed: {Message}", eventName, uri.Host, ex.Message);
            return false;
        }
    }
}
