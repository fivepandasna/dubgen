using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Dubtitles;

internal static class AudioSelector
{
    /// <summary>
    /// Finds the English track when the item's primary audio is a known non-English language.
    /// Primary = default-flagged audio track, else the first audio track (commentary/description tracks ignored).
    /// </summary>
    public static bool TryGetEnglishDubTrack(IEnumerable<MediaStream> streams, out MediaStream englishTrack)
    {
        englishTrack = null!;

        var audio = streams
            .Where(s => s.Type == MediaStreamType.Audio && !s.IsExternal && !IsAuxiliary(s))
            .OrderBy(s => s.Index)
            .ToList();

        if (audio.Count < 2)
        {
            return false;
        }

        var primary = audio.FirstOrDefault(s => s.IsDefault) ?? audio[0];
        if (IsUnknown(primary.Language) || IsEnglish(primary.Language))
        {
            return false;
        }

        var english = audio.FirstOrDefault(s => IsEnglish(s.Language));
        if (english is null)
        {
            return false;
        }

        englishTrack = english;
        return true;
    }

    private static bool IsEnglish(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return false;
        }

        var l = lang.Trim();
        return l.Equals("en", StringComparison.OrdinalIgnoreCase)
               || l.Equals("eng", StringComparison.OrdinalIgnoreCase)
               || l.Equals("english", StringComparison.OrdinalIgnoreCase)
               || l.StartsWith("en-", StringComparison.OrdinalIgnoreCase)
               || l.StartsWith("en_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnknown(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return true;
        }

        var l = lang.Trim();
        return l.Equals("und", StringComparison.OrdinalIgnoreCase)
               || l.Equals("mis", StringComparison.OrdinalIgnoreCase)
               || l.Equals("zxx", StringComparison.OrdinalIgnoreCase)
               || l.Equals("mul", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuxiliary(MediaStream s)
    {
        var t = $"{s.Title} {s.DisplayTitle}";
        return t.Contains("commentary", StringComparison.OrdinalIgnoreCase)
               || t.Contains("descriptive", StringComparison.OrdinalIgnoreCase)
               || t.Contains("audio description", StringComparison.OrdinalIgnoreCase);
    }
}