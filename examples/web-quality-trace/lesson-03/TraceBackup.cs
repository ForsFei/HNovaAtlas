using Microsoft.Data.Sqlite;

static class TraceBackup
{
    public static string Create(string source, string destination)
    {
        destination = Path.GetFullPath(destination);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("Backup destination already exists.");

        using var original = Open(source, SqliteOpenMode.ReadOnly);
        Execute(original, "SELECT 1 FROM Inspections LIMIT 0");
        string folder = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, $".trace-backup-{Guid.NewGuid():N}.pending");
        using (File.Open(temporary, FileMode.CreateNew, FileAccess.Write)) { }

        try
        {
            using (var copy = Open(temporary, SqliteOpenMode.ReadWrite))
            {
                original.BackupDatabase(copy);
                if (!string.Equals(Execute(copy, "PRAGMA journal_mode=DELETE") as string, "delete",
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Backup could not be closed as a standalone database.");
                if (!Equals(Execute(copy, "PRAGMA quick_check"), "ok"))
                    throw new InvalidDataException("Backup failed SQLite quick_check.");
            }
            File.Move(temporary, destination);
            return destination;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = mode, Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static object? Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
