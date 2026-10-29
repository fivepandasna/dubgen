using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Dubgen.Configuration;

public class WebhookSettings
{
    public bool Enabled { get; set; }

    public string Url { get; set; } = string.Empty;

    /// <summary>HTTP method: POST, GET, PUT...</summary>
    public string Method { get; set; } = "POST";

    /// <summary>Sent as "Authorization: Bearer &lt;token&gt;" when non-empty.</summary>
    public string BearerToken { get; set; } = string.Empty;

    /// <summary>Optional request body, sent as application/json when non-empty.</summary>
    public string Body { get; set; } = string.Empty;
}

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Base URL of the Subgen instance (no trailing /asr).</summary>
    public string SubgenUrl { get; set; } = "http://localhost:9000";

    /// <summary>Token placed after ".en." in the output name: Title.en.Dubtitles.srt.</summary>
    public string SubtitleTag { get; set; } = "Dubtitles";

    /// <summary>
    /// Library (virtual folder) ids to process. Empty = all libraries.
    /// If ids are set but none exist any more, nothing is processed.
    /// </summary>
    public string[] EnabledLibraryIds { get; set; } = System.Array.Empty<string>();

    public bool IncludeMovies { get; set; } = true;

    public bool IncludeEpisodes { get; set; } = true;

    /// <summary>
    /// false: skip only episodes/movies that already have a dubtitle file (a half-finished series resumes).
    /// true: skip a whole series as soon as any one of its episodes has a dubtitle file.
    /// </summary>
    public bool SkipSeriesWithAnyDubtitles { get; set; }

    /// <summary>Per-item timeout for the Subgen request.</summary>
    public int RequestTimeoutMinutes { get; set; } = 120;

    /// <summary>Cap on items processed per task run. 0 = no limit.</summary>
    public int MaxItemsPerRun { get; set; }

    /// <summary>Refresh the item after writing so the subtitle appears immediately.</summary>
    public bool RefreshAfterWrite { get; set; } = true;

    /// <summary>Sent once, only when there is work to do, before anything is sent to Subgen.</summary>
    public WebhookSettings PreWebhook { get; set; } = new();

    /// <summary>Extra fixed wait after the pre-webhook returns (seconds).</summary>
    public int PreWebhookDelaySeconds { get; set; }

    /// <summary>After the pre-webhook, poll Subgen until it answers before starting.</summary>
    public bool WaitForSubgenReady { get; set; } = true;

    public int SubgenReadyTimeoutMinutes { get; set; } = 10;

    /// <summary>Sent when processing ends (finished, failed or cancelled), only if there was work to do.</summary>
    public WebhookSettings PostWebhook { get; set; } = new();
}
