namespace EArchive.Models;

public sealed class ArchiveRecord
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string StoredRelativePath { get; set; } = "";
    public string Extension { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}