using System.IO;
using EArchive.Models;
using Microsoft.Data.Sqlite;

namespace EArchive.Data;

public sealed class ArchiveRepository
{
    private readonly string _connectionString;
    public ArchiveRepository(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        Initialize();
    }

    private SqliteConnection Open() => new(_connectionString);

    private void Initialize()
    {
        using var c = Open(); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
        PRAGMA journal_mode=WAL;
        PRAGMA foreign_keys=ON;
        CREATE TABLE IF NOT EXISTS ArchiveRecords(
          Id INTEGER PRIMARY KEY AUTOINCREMENT,
          Title TEXT NOT NULL, Source TEXT NOT NULL, Destination TEXT NOT NULL,
          Notes TEXT NOT NULL, DateFrom TEXT NULL, DateTo TEXT NULL,
          OriginalFileName TEXT NOT NULL, StoredRelativePath TEXT NOT NULL,
          Extension TEXT NOT NULL, FileSize INTEGER NOT NULL, CreatedAt TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_Archive_DateFrom ON ArchiveRecords(DateFrom);
        CREATE INDEX IF NOT EXISTS IX_Archive_DateTo ON ArchiveRecords(DateTo);
        CREATE INDEX IF NOT EXISTS IX_Archive_Title ON ArchiveRecords(Title);
        """;
        cmd.ExecuteNonQuery();
    }

    public async Task<long> AddAsync(ArchiveRecord r, CancellationToken ct = default)
    {
        await using var c = Open(); await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        INSERT INTO ArchiveRecords
        (Title,Source,Destination,Notes,DateFrom,DateTo,OriginalFileName,StoredRelativePath,Extension,FileSize,CreatedAt)
        VALUES ($title,$source,$destination,$notes,$from,$to,$name,$path,$ext,$size,$created);
        SELECT last_insert_rowid();
        """;
        Bind(cmd, r);
        return (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
    }


    public async Task UpdateAsync(ArchiveRecord r, CancellationToken ct = default)
    {
        await using var c = Open(); await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        UPDATE ArchiveRecords SET Title=$title, Source=$source, Destination=$destination,
          Notes=$notes, DateFrom=$from, DateTo=$to, OriginalFileName=$name,
          StoredRelativePath=$path, Extension=$ext, FileSize=$size
        WHERE Id=$id;
        """;
        Bind(cmd, r);
        cmd.Parameters.AddWithValue("$id", r.Id);
        if (await cmd.ExecuteNonQueryAsync(ct) == 0)
            throw new InvalidOperationException("السجل المطلوب تعديله غير موجود.");
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var c = Open(); await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM ArchiveRecords WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<ArchiveRecord>> SearchAsync(string? query, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var results = new List<ArchiveRecord>();
        await using var c = Open(); await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        SELECT * FROM ArchiveRecords
        WHERE ($q = '' OR Title LIKE $like OR Source LIKE $like OR Destination LIKE $like OR Notes LIKE $like OR OriginalFileName LIKE $like)
          AND ($from IS NULL OR (DateFrom IS NOT NULL AND DateFrom >= $from))
          AND ($to IS NULL OR (DateTo IS NOT NULL AND DateTo <= $to))
        ORDER BY CreatedAt DESC LIMIT 5000;
        """;
        var q = (query ?? "").Trim();
        cmd.Parameters.AddWithValue("$q", q);
        cmd.Parameters.AddWithValue("$like", $"%{q}%");
        cmd.Parameters.AddWithValue("$from", from?.ToString("yyyy-MM-dd") is string f && from.HasValue ? f : DBNull.Value);
        cmd.Parameters.AddWithValue("$to", to?.ToString("yyyy-MM-dd") is string t && to.HasValue ? t : DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ArchiveRecord {
                Id=reader.GetInt64(0), Title=reader.GetString(1), Source=reader.GetString(2),
                Destination=reader.GetString(3), Notes=reader.GetString(4),
                DateFrom=ParseDate(reader.IsDBNull(5)?null:reader.GetString(5)),
                DateTo=ParseDate(reader.IsDBNull(6)?null:reader.GetString(6)),
                OriginalFileName=reader.GetString(7), StoredRelativePath=reader.GetString(8),
                Extension=reader.GetString(9), FileSize=reader.GetInt64(10),
                CreatedAt=DateTime.Parse(reader.GetString(11))
            });
        }
        return results;
    }

    public async Task<List<ArchiveRecord>> GetAllAsync(CancellationToken ct = default) =>
        await SearchAsync("", null, null, ct);

    private static DateTime? ParseDate(string? s) =>
        DateTime.TryParse(s, out var d) ? d : null;

    private static void Bind(SqliteCommand cmd, ArchiveRecord r)
    {
        cmd.Parameters.AddWithValue("$title", r.Title);
        cmd.Parameters.AddWithValue("$source", r.Source);
        cmd.Parameters.AddWithValue("$destination", r.Destination);
        cmd.Parameters.AddWithValue("$notes", r.Notes);
        cmd.Parameters.AddWithValue("$from", r.DateFrom?.ToString("yyyy-MM-dd") is string f && r.DateFrom.HasValue ? f : DBNull.Value);
        cmd.Parameters.AddWithValue("$to", r.DateTo?.ToString("yyyy-MM-dd") is string t && r.DateTo.HasValue ? t : DBNull.Value);
        cmd.Parameters.AddWithValue("$name", r.OriginalFileName);
        cmd.Parameters.AddWithValue("$path", r.StoredRelativePath);
        cmd.Parameters.AddWithValue("$ext", r.Extension);
        cmd.Parameters.AddWithValue("$size", r.FileSize);
        cmd.Parameters.AddWithValue("$created", r.CreatedAt.ToString("O"));
    }
}