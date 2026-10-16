using Microsoft.Data.Sqlite;

using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open(); // :memory: 的内容只在这条连接打开期间存在

using (var schema = connection.CreateCommand())
{
    schema.CommandText = """
        CREATE TABLE Readings (
            Id INTEGER PRIMARY KEY,
            DeviceId TEXT NOT NULL,
            Value REAL NOT NULL CHECK (Value >= 0)
        );
        CREATE TABLE Audit (
            Id INTEGER PRIMARY KEY,
            Message TEXT NOT NULL
        );
        """;
    schema.ExecuteNonQuery();
}

using (var transaction = connection.BeginTransaction())
{
    InsertReading(connection, transaction, "pump-A", 12.5);
    InsertAudit(connection, transaction, "added pump-A");
    transaction.Commit();
}

Require(CountFor(connection, "pump-A") == 1, "事务提交后读数可见");
Require(CountAudit(connection) == 1, "同一事务中的审计记录可见");

// 这里的引号和 SQL 片段只是参数值，不会成为 WHERE 条件。
Require(CountFor(connection, "pump-A' OR 1=1 --") == 0, "参数化查询不执行输入中的 SQL");

bool constraintFailed = false;
using (var transaction = connection.BeginTransaction())
{
    try
    {
        InsertReading(connection, transaction, "pump-B", 9);
        InsertReading(connection, transaction, "invalid", -1); // CHECK 约束拒绝
        transaction.Commit();
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
    {
        constraintFailed = true;
        transaction.Rollback();
    }
}

Require(constraintFailed, "约束错误确实发生");
Require(CountFor(connection, "pump-B") == 0, "失败前已执行的写入也被回滚");

using (var command = connection.CreateCommand())
{
    command.CommandText = "SELECT DeviceId, Value FROM Readings ORDER BY Id";
    using var reader = command.ExecuteReader();
    Require(reader.Read(), "Reader 找到已提交行");
    Require(reader.GetString(0) == "pump-A" && reader.GetDouble(1) == 12.5, "读取列值");
    Require(!reader.Read(), "Reader 只看到一行");
}

Console.WriteLine("SQLite 集成示例通过：参数、Reader、提交与回滚。");

static void InsertReading(SqliteConnection connection, SqliteTransaction transaction, string deviceId, double value)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "INSERT INTO Readings (DeviceId, Value) VALUES ($deviceId, $value)";
    command.Parameters.AddWithValue("$deviceId", deviceId);
    command.Parameters.AddWithValue("$value", value);
    Require(command.ExecuteNonQuery() == 1, "插入一条读数");
}

static void InsertAudit(SqliteConnection connection, SqliteTransaction transaction, string message)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "INSERT INTO Audit (Message) VALUES ($message)";
    command.Parameters.AddWithValue("$message", message);
    Require(command.ExecuteNonQuery() == 1, "插入一条审计记录");
}

static long CountFor(SqliteConnection connection, string deviceId)
{
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM Readings WHERE DeviceId = $deviceId";
    command.Parameters.AddWithValue("$deviceId", deviceId);
    return (long)command.ExecuteScalar()!;
}

static long CountAudit(SqliteConnection connection)
{
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM Audit";
    return (long)command.ExecuteScalar()!;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException($"验证失败：{message}");
}
