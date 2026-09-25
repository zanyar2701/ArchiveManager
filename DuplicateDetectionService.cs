using System.IO;
using System.Text.RegularExpressions;

namespace ArchiveManager.Application.Services;

public enum DuplicateResolution
{
    None,
    NewVersion,
    ViewExisting,
    Cancel,
    OverwriteConfirmed // Administrator-only, requires typed confirmation in the UI
}

/// <summary>
/// Detects filename collisions in the destination folder and computes the next
/// free version number. Never decides to overwrite by itself (spec §7) — that
/// decision always comes from an explicit UI choice.
/// </summary>
public sealed class DuplicateDetectionService
{
    private static readonly Regex VersionSuffix = new(@" نسخه (\d{2})$", RegexOptions.Compiled);

    public bool Exists(string destinationFolder, string fileName)
    {
        return File.Exists(Path.Combine(destinationFolder, fileName));
    }

    /// <summary>
    /// Given a base name (no version, no extension) and an extension, scans the
    /// destination folder for existing "<base> نسخه NN.ext" files and returns the
    /// next free version number (starting at 2, since an unversioned file is
    /// implicitly version 1 — spec §15: "don't force a version when only one exists").
    /// </summary>
    public int NextFreeVersion(string destinationFolder, string baseNameNoVersion, string extension)
    {
        if (!Directory.Exists(destinationFolder))
        {
            return 2;
        }

        var maxVersion = 1;
        var ext = "." + extension.TrimStart('.').ToLowerInvariant();

        foreach (var file in Directory.EnumerateFiles(destinationFolder))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var fileExt = Path.GetExtension(file);
            if (!string.Equals(fileExt, ext, System.StringComparison.OrdinalIgnoreCase))
                continue;

            var match = VersionSuffix.Match(name);
            if (match.Success)
            {
                var withoutVersion = name[..match.Index];
                if (string.Equals(withoutVersion, baseNameNoVersion, System.StringComparison.Ordinal)
                    && int.TryParse(match.Groups[1].Value, out var v))
                {
                    if (v > maxVersion) maxVersion = v;
                }
            }
            else if (string.Equals(name, baseNameNoVersion, System.StringComparison.Ordinal))
            {
                // the un-versioned file counts as version 1
            }
        }

        return maxVersion + 1;
    }
}
