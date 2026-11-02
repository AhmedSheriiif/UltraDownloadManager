using System.Globalization;
using Microsoft.Data.Sqlite;

namespace UltraDownloadManager.Core.History;

/// <summary>
/// Local SQLite-backed download history. Synchronous by design: it's small, local file I/O
/// (matching how FDM itself persists history) and call sites are infrequent UI-thread actions,
/// so async machinery here would just add ceremony without a real benefit.
/// </summary>
public sealed class HistoryService
{
    private readonly string _connectionString;

    public HistoryService(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS DownloadHistory (
                Id TEXT PRIMARY KEY,
                Url TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FilePath TEXT,
                Category TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                Source TEXT NOT NULL,
                Status TEXT NOT NULL,
                AddedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_DownloadHistory_Url ON DownloadHistory(Url);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public void Upsert(HistoryEntry entry)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DownloadHistory (Id, Url, FileName, FilePath, Category, SizeBytes, Source, Status, AddedAtUtc, CompletedAtUtc)
            VALUES ($id, $url, $fileName, $filePath, $category, $size, $source, $status, $added, $completed)
            ON CONFLICT(Id) DO UPDATE SET
                FileName = excluded.FileName,
                FilePath = excluded.FilePath,
                Category = excluded.Category,
                SizeBytes = excluded.SizeBytes,
                Status = excluded.Status,
                CompletedAtUtc = excluded.CompletedAtUtc;
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$url", entry.Url);
        command.Parameters.AddWithValue("$fileName", entry.FileName);
        command.Parameters.AddWithValue("$filePath", (object?)entry.FilePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$category", entry.Category);
        command.Parameters.AddWithValue("$size", entry.SizeBytes);
        command.Parameters.AddWithValue("$source", entry.Source);
        command.Parameters.AddWithValue("$status", entry.Status);
        command.Parameters.AddWithValue("$added", entry.AddedAtUtc.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$completed",
            (object?)entry.CompletedAtUtc?.ToString("o", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    /// <summary>Most recent successful download of this exact URL, if any - used for duplicate warnings.</summary>
    public HistoryEntry? FindCompletedByUrl(string url)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT * FROM DownloadHistory WHERE Url = $url AND Status = 'complete' ORDER BY CompletedAtUtc DESC LIMIT 1";
        command.Parameters.AddWithValue("$url", url);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public List<HistoryEntry> GetAll(int limit = 2000)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM DownloadHistory ORDER BY AddedAtUtc DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();

        var results = new List<HistoryEntry>();
        while (reader.Read()) results.Add(Read(reader));
        return results;
    }

    public void Remove(string id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DownloadHistory WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void Clear()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DownloadHistory";
        command.ExecuteNonQuery();
    }

    private static HistoryEntry Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("Id")),
        Url = reader.GetString(reader.GetOrdinal("Url")),
        FileName = reader.GetString(reader.GetOrdinal("FileName")),
        FilePath = reader.IsDBNull(reader.GetOrdinal("FilePath")) ? null : reader.GetString(reader.GetOrdinal("FilePath")),
        Category = reader.GetString(reader.GetOrdinal("Category")),
        SizeBytes = reader.GetInt64(reader.GetOrdinal("SizeBytes")),
        Source = reader.GetString(reader.GetOrdinal("Source")),
        Status = reader.GetString(reader.GetOrdinal("Status")),
        AddedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("AddedAtUtc")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        CompletedAtUtc = reader.IsDBNull(reader.GetOrdinal("CompletedAtUtc"))
            ? null
            : DateTime.Parse(reader.GetString(reader.GetOrdinal("CompletedAtUtc")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };
}
