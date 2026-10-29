using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Dubgen.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Dubgen;

public class GenerateDubtitlesTask : IScheduledTask
{
    private sealed record Candidate(BaseItem Item, MediaStream Track);

    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<GenerateDubtitlesTask> _logger;

    public GenerateDubtitlesTask(
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        IMediaEncoder mediaEncoder,
        IFileSystem fileSystem,
        ILogger<GenerateDubtitlesTask> logger)
    {
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
        _mediaEncoder = mediaEncoder;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public string Name => "Generate Dubtitles";

    public string Key => "GenerateDubtitles";

    public string Description =>
        "Transcribes the English dub of foreign-language media with Subgen and saves it as <title>.en.Dubtitles.srt.";

    public string Category => "Dubgen";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Daily at 02:00. Change or remove under Dashboard > Scheduled Tasks.
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(2).Ticks
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (string.IsNullOrWhiteSpace(cfg.SubgenUrl))
        {
            _logger.LogError("Dubgen: Subgen URL is not configured.");
            return;
        }

        var kinds = new List<BaseItemKind>();
        if (cfg.IncludeMovies)
        {
            kinds.Add(BaseItemKind.Movie);
        }

        if (cfg.IncludeEpisodes)
        {
            kinds.Add(BaseItemKind.Episode);
        }

        if (kinds.Count == 0)
        {
            return;
        }

        var items = GetItems(cfg, kinds.ToArray());

        // ---- Phase 1: scan (0-10%) ----
        var dirCache = new DirectoryCache();
        var seriesWithDubtitles = new HashSet<Guid>();
        var candidates = new List<Candidate>();

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[i];
            var path = item.Path;

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var hasDubtitle = DubtitleFiles.Exists(path, dirCache, cfg.SubtitleTag);
                if (hasDubtitle && item is Episode existing && existing.SeriesId != Guid.Empty)
                {
                    seriesWithDubtitles.Add(existing.SeriesId);
                }

                if (!hasDubtitle)
                {
                    var streams = _mediaSourceManager.GetMediaStreams(item.Id);
                    if (AudioSelector.TryGetEnglishDubTrack(streams, out var track))
                    {
                        candidates.Add(new Candidate(item, track));
                    }
                }
            }

            progress.Report(10.0 * (i + 1) / items.Count);
        }

        if (cfg.SkipSeriesWithAnyDubtitles)
        {
            var removed = candidates.RemoveAll(c => c.Item is Episode e && seriesWithDubtitles.Contains(e.SeriesId));
            _logger.LogInformation(
                "Dubgen: skipped {Count} episodes in {Series} series that already have dubtitles",
                removed,
                seriesWithDubtitles.Count);
        }

        if (cfg.MaxItemsPerRun > 0 && candidates.Count > cfg.MaxItemsPerRun)
        {
            candidates = candidates.Take(cfg.MaxItemsPerRun).ToList();
        }

        _logger.LogInformation("Dubgen: {Count} item(s) to process", candidates.Count);

        if (candidates.Count == 0)
        {
            // Nothing to do: do not wake the remote machine.
            progress.Report(100);
            return;
        }

        // ---- Phase 2: generate (10-100%) ----
        using var client = new SubgenClient(cfg.SubgenUrl, TimeSpan.FromMinutes(Math.Max(1, cfg.RequestTimeoutMinutes)));

        try
        {
            if (cfg.PreWebhook.Enabled)
            {
                await WebhookClient.SendAsync(cfg.PreWebhook, "pre-run", _logger, cancellationToken).ConfigureAwait(false);

                if (cfg.PreWebhookDelaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(cfg.PreWebhookDelaySeconds), cancellationToken).ConfigureAwait(false);
                }

                if (cfg.WaitForSubgenReady)
                {
                    var timeout = TimeSpan.FromMinutes(Math.Max(1, cfg.SubgenReadyTimeoutMinutes));
                    _logger.LogInformation("Dubgen: waiting up to {Minutes} min for Subgen at {Url}", timeout.TotalMinutes, cfg.SubgenUrl);
                    if (!await client.WaitUntilReachableAsync(timeout, _logger, cancellationToken).ConfigureAwait(false))
                    {
                        _logger.LogError("Dubgen: Subgen did not become reachable at {Url}, aborting run", cfg.SubgenUrl);
                        return;
                    }
                }
            }

            var consecutiveFailures = 0;

            for (var i = 0; i < candidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (item, track) = candidates[i];

                try
                {
                    _logger.LogInformation("Dubgen: [{N}/{T}] {Path} (audio stream #{Index}, {Lang})", i + 1, candidates.Count, item.Path, track.Index, track.Language);
                    await GenerateAsync(item, track, cfg, client, cancellationToken).ConfigureAwait(false);
                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    _logger.LogWarning(ex, "Dubgen: failed for {Path}", item.Path);
                    if (consecutiveFailures >= 3)
                    {
                        _logger.LogError("Dubgen: 3 consecutive failures, aborting run. Is Subgen reachable at {Url}?", cfg.SubgenUrl);
                        break;
                    }
                }

                progress.Report(10.0 + (90.0 * (i + 1) / candidates.Count));
            }

            progress.Report(100);
        }
        finally
        {
            // Fires on success, failure and cancellation so a machine that was woken up always gets shut down.
            if (cfg.PostWebhook.Enabled)
            {
                await WebhookClient.SendAsync(cfg.PostWebhook, "post-run", _logger, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private List<BaseItem> GetItems(PluginConfiguration cfg, BaseItemKind[] kinds)
    {
        var selected = cfg.EnabledLibraryIds ?? Array.Empty<string>();

        if (selected.Length == 0)
        {
            return _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = kinds,
                IsVirtualItem = false,
                Recursive = true
            }).ToList();
        }

        var result = new List<BaseItem>();
        var seen = new HashSet<Guid>();
        var resolved = 0;

        foreach (var id in selected)
        {
            if (!Guid.TryParse(id, out var libraryId))
            {
                continue;
            }

            if (_libraryManager.GetItemById(libraryId) is null)
            {
                _logger.LogWarning("Dubgen: selected library {Id} no longer exists, ignoring", id);
                continue;
            }

            resolved++;
            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = kinds,
                IsVirtualItem = false,
                Recursive = true,
                ParentId = libraryId
            });

            foreach (var item in items)
            {
                if (seen.Add(item.Id))
                {
                    result.Add(item);
                }
            }
        }

        if (resolved == 0)
        {
            // Deliberately do not fall back to "all libraries".
            _logger.LogWarning("Dubgen: none of the selected libraries exist; nothing to do. Re-select libraries in the plugin settings.");
        }

        return result;
    }

    private async Task GenerateAsync(BaseItem item, MediaStream track, PluginConfiguration cfg, SubgenClient client, CancellationToken ct)
    {
        var outPath = DubtitleFiles.BuildOutputPath(item.Path, cfg.SubtitleTag);
        var tmpAudio = Path.Combine(Path.GetTempPath(), $"dubgen-{Guid.NewGuid():N}.flac");
        var tmpSrt = outPath + ".tmp";

        try
        {
            await ExtractAudioAsync(item.Path, track.Index, tmpAudio, ct).ConfigureAwait(false);
            var srt = await client.TranscribeEnglishAsync(tmpAudio, ct).ConfigureAwait(false);

            await File.WriteAllTextAsync(tmpSrt, srt, new UTF8Encoding(false), ct).ConfigureAwait(false);
            File.Move(tmpSrt, outPath, overwrite: true);
            _logger.LogInformation("Dubgen: wrote {Out}", outPath);
        }
        finally
        {
            TryDelete(tmpAudio);
            TryDelete(tmpSrt);
        }

        if (cfg.RefreshAfterWrite)
        {
            try
            {
                var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                {
                    MetadataRefreshMode = MetadataRefreshMode.ValidationOnly,
                    ImageRefreshMode = MetadataRefreshMode.ValidationOnly
                };
                await item.RefreshMetadata(options, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Dubgen: metadata refresh failed for {Path}; it will appear on the next library scan", item.Path);
            }
        }
    }

    private async Task ExtractAudioAsync(string mediaPath, int streamIndex, string outPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _mediaEncoder.EncoderPath,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in new[]
        {
            "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
            "-i", mediaPath,
            "-map", $"0:{streamIndex}",
            "-vn", "-sn", "-dn",
            "-ac", "1", "-ar", "16000",
            "-c:a", "flac", "-f", "flac",
            outPath
        })
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        using var reg = ct.Register(() =>
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(true);
                }
            }
            catch
            {
                // process already gone
            }
        });

        var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg exited with {proc.ExitCode}: {stderr}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
