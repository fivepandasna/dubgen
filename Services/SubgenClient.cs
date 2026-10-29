using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Dubgen;

/// <summary>Talks to Subgen's Bazarr/whisper-asr-compatible POST /asr endpoint.</summary>
internal sealed class SubgenClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly string _baseUrl;
    private readonly TimeSpan _timeout;

    public SubgenClient(string baseUrl, TimeSpan timeout)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _timeout = timeout;
    }

    /// <summary>
    /// Polls Subgen until it answers (any non-5xx response counts) or the timeout passes.
    /// Used after a "wake up" webhook while a remote machine boots.
    /// </summary>
    public async Task<bool> WaitUntilReachableAsync(TimeSpan timeout, ILogger logger, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                using var resp = await _http.GetAsync(_baseUrl + "/status", HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                if ((int)resp.StatusCode < 500)
                {
                    return true;
                }
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is OperationCanceledException) && !ct.IsCancellationRequested)
            {
                // not up yet
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            logger.LogDebug("Dubgen: Subgen not reachable yet, retrying in 10s");
            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        }
    }

    public async Task<string> TranscribeEnglishAsync(string audioPath, CancellationToken ct)
    {
        var url = $"{_baseUrl}/asr?task=transcribe&language=en&output=srt&encode=true";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        await using var fs = File.OpenRead(audioPath);
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(fs);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/flac");
        form.Add(file, "audio_file", "audio.flac");

        using var resp = await _http.PostAsync(url, form, cts.Token).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var snippet = body.Length > 300 ? body[..300] : body;
            throw new HttpRequestException($"Subgen returned {(int)resp.StatusCode}: {snippet}");
        }

        if (!body.Contains("-->", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Subgen response contained no SRT cues.");
        }

        return body;
    }

    public void Dispose() => _http.Dispose();
}
