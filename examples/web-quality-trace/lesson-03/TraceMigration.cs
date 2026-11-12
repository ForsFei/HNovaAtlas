using Microsoft.Data.Sqlite;

static class TraceMigration
{
    public static bool Apply(string database)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        long version = (long)command.ExecuteScalar()!;
        if (version == 1) return false;
        if (version != 0)
            throw new InvalidDataException($"Unsupported trace database version: {version}.");

        command.CommandText = """
            CREATE INDEX IX_Inspections_Station_Result_ImportedTicks_Id
                ON Inspections (Station, Result, ImportedTicks, Id);
            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }
}
