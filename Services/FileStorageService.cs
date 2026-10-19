using System.IO;
using EArchive.Models;

namespace EArchive.Services;

public sealed class FileStorageService
{
    private readonly string _root;
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp" };

    public FileStorageService(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task<(string relativePath, long size, string extension)> StoreAsync(
        string sourcePath, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(sourcePath);
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException($"نوع الملف غير مسموح: {ext}");

        var now = DateTime.Now;
        var folder = Path.Combine(_root, now.ToString("yyyy"), now.ToString("MM"));
        Directory.CreateDirectory(folder);

        var safeName = Path.GetFileName(sourcePath);
        var storedName = $"{Guid.NewGuid():N}_{safeName}";
        var destination = Path.Combine(folder, storedName);

        // Streaming copy: لا يحمل الملف بالكامل إلى الذاكرة.
        await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await input.CopyToAsync(output, 1024 * 128, ct);
        }

        var relative = Path.GetRelativePath(_root, destination);
        return (relative, new FileInfo(destination).Length, ext.ToLowerInvariant());
    }

    public string GetFullPath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("مسار الملف خارج مجلد الأرشيف.");
        return full;
    }
}