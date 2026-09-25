using System.Collections.Generic;
using System.IO;

namespace ArchiveManager.Infrastructure.FileSystem;

/// <summary>Maps a file extension to a friendly label + icon key (spec §8).</summary>
public sealed class FileTypeMapperService
{
    private static readonly Dictionary<string, (string Label, string IconKey)> Map = new()
    {
        [".pdf"] = ("PDF", "pdf"),
        [".doc"] = ("Word", "word"),
        [".docx"] = ("Word", "word"),
        [".xls"] = ("Excel", "excel"),
        [".xlsx"] = ("Excel", "excel"),
        [".csv"] = ("Excel", "excel"),
        [".ppt"] = ("PowerPoint", "ppt"),
        [".pptx"] = ("PowerPoint", "ppt"),
        [".mdb"] = ("Access", "access"),
        [".accdb"] = ("Access", "access"),
        [".zip"] = ("ZIP", "zip"),
        [".rar"] = ("ZIP", "zip"),
        [".7z"] = ("ZIP", "zip"),
        [".jpg"] = ("Image", "image"),
        [".jpeg"] = ("Image", "image"),
        [".png"] = ("Image", "image"),
        [".bmp"] = ("Image", "image"),
        [".tif"] = ("Image", "image"),
        [".mp4"] = ("Video", "video"),
        [".mov"] = ("Video", "video"),
        [".avi"] = ("Video", "video"),
        [".txt"] = ("Text", "text"),
    };

    public (string Label, string IconKey) Resolve(string fileNameOrExtension)
    {
        var ext = fileNameOrExtension.Contains('.')
            ? Path.GetExtension(fileNameOrExtension).ToLowerInvariant()
            : "." + fileNameOrExtension.TrimStart('.').ToLowerInvariant();

        return Map.TryGetValue(ext, out var result) ? result : ("سایر", "other");
    }

    public bool IsKnownExtension(string extension) =>
        Map.ContainsKey(extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant());
}
