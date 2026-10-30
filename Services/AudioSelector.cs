using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Dubgen;

internal static class AudioSelector
{
    /// <summary>
    /// Finds the English track to transcribe.
    /// Primary = default-flagged audio track, else the first audio track (commentary/description tracks ignored).
    /// <para>
    /// Foreign primary: returns the English track (if there is one).<br/>
    /// English primary: only when <paramref name="includeEnglishPrimary"/> is true, and only if the item also has
    /// a known non-English audio track; returns the primary (English) track.
    /// </para>
    /// Items whose primary audio has no/unknown language tag are always skipped.
    /// </summary>
    public static bool TryGetEnglishDubTrack(IEnumerable<MediaStream> streams, bool includeEnglishPrimary, out MediaStream englishTrack)
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
        if (IsUnknown(primary.Language))
        {
            return false;
        }

        if (IsEnglish(primary.Language))
        {
            if (!includeEnglishPrimary)
            {
                return false;
            }

            // English primary: require at least one other track in a known, non-English language.
            var hasForeign = audio.Any(s => !ReferenceEquals(s, primary) && !IsUnknown(s.Language) && !IsEnglish(s.Language));
            if (!hasForeign)
            {
                return false;
            }

            englishTrack = primary;
            return true;
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
