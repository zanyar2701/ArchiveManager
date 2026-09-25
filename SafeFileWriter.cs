using System;
using System.IO;

namespace ArchiveManager.Infrastructure.FileSystem;

public enum FileTransferMode { Copy, Move }

/// <summary>
/// The only code path in the app allowed to write a file into the archive.
/// Pattern: write to a temp name in the destination folder, verify it, then
/// atomically rename to the final name. Never overwrites blindly (spec §7/§20).
/// </summary>
public sealed class SafeFileWriter
{
    public sealed class ConflictException : Exception
    {
        public ConflictException(string path) : base($"مقصد از قبل وجود دارد: {path}") { }
    }

    /// <summary>
    /// Transfers <paramref name="sourcePath"/> into <paramref name="destinationFolder"/>
    /// under <paramref name="finalFileName"/>. Throws ConflictException if the final
    /// name already exists — callers must resolve conflicts (versioning / explicit
    /// overwrite) BEFORE calling this, per spec §7; this method itself never overwrites.
    /// </summary>
    public string TransferInto(string sourcePath, string destinationFolder, string finalFileName, FileTransferMode mode)
    {
        Directory.CreateDirectory(destinationFolder);
        var finalPath = Path.Combine(destinationFolder, finalFileName);

        if (File.Exists(finalPath))
        {
            throw new ConflictException(finalPath);
        }

        var tempPath = Path.Combine(destinationFolder, $".{Guid.NewGuid():N}.tmp");

        try
        {
            File.Copy(sourcePath, tempPath, overwrite: false);

            // Verify: same length as source. (A full hash compare is possible but
            // unnecessary overhead for typical office documents — ASSUMPTION.)
            var sourceLength = new FileInfo(sourcePath).Length;
            var tempLength = new FileInfo(tempPath).Length;
            if (sourceLength != tempLength)
            {
                SafeDelete(tempPath);
                throw new IOException("تایید فایل کپی‌شده ناموفق بود؛ اندازه فایل مطابقت ندارد.");
            }

            File.Move(tempPath, finalPath); // atomic on the same volume

            if (mode == FileTransferMode.Move)
            {
                File.Delete(sourcePath);
            }

            return finalPath;
        }
        catch
        {
            SafeDelete(tempPath);
            throw;
        }
    }

    /// <summary>Renames an already-registered file in place using the same safe pattern.</summary>
    public string RenameInPlace(string currentPath, string newFileName)
    {
        var folder = Path.GetDirectoryName(currentPath)
            ?? throw new IOException("مسیر پوشه نامعتبر است.");
        var newPath = Path.Combine(folder, newFileName);

        if (string.Equals(currentPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            return currentPath; // no-op
        }

        if (File.Exists(newPath))
        {
            throw new ConflictException(newPath);
        }

        File.Move(currentPath, newPath);
        return newPath;
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
