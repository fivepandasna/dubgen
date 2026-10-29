using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.Dubtitles;

/// <summary>Naming and detection of "&lt;title&gt;.en.Dubtitles.srt" sidecar files.</summary>
internal static class DubtitleFiles
{
    private static readonly string[] SubtitleExtensions = { ".srt", ".ass", ".ssa", ".vtt", ".sub" };
    private static readonly string[] FlagTokens = { "default", "forced", "sdh", "cc" };

    public static string BuildOutputPath(string mediaPath, string tag)
    {
        var dir = Path.GetDirectoryName(mediaPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(mediaPath);
        return Path.Combine(dir, $"{name}.en.{tag}.srt");
    }

    /// <summary>
    /// True if a sidecar subtitle next to the media file has a "dubtitle*" token
    /// (matches both ".Dubtitle." and ".Dubtitles.", case-insensitive).
    /// </summary>
    public static bool Exists(string mediaPath, DirectoryCache cache)
    {
        var dir = Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrEmpty(dir))
        {
            return false;
        }

        var baseName = Path.GetFileNameWithoutExtension(mediaPath);
        var prefix = baseName + ".";

        foreach (var file in cache.GetFiles(dir))
        {
            var fileName = Path.GetFileName(file);
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ext = Path.GetExtension(fileName);
            if (!SubtitleExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var middle = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - ext.Length);
            var tokens = middle.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var idx = Array.FindIndex(tokens, t => t.StartsWith("dubtitle", StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
            {
                continue;
            }

            // Everything before the dubtitle token must look like a language code or flag, otherwise this
            // belongs to a sibling file such as "Movie.Extended.en.Dubtitles.srt" vs "Movie.mkv".
            var leading = tokens.Take(idx);
            if (leading.All(t => t.Length <= 3 || FlagTokens.Contains(t, StringComparer.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class DirectoryCache
{
    private readonly Dictionary<string, string[]> _cache = new(StringComparer.Ordinal);

    public string[] GetFiles(string dir)
    {
        if (_cache.TryGetValue(dir, out var files))
        {
            return files;
        }

        try
        {
            files = Directory.GetFiles(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = Array.Empty<string>();
        }

        _cache[dir] = files;
        return files;
    }
}