using System;
using Microsoft.Data.Sqlite;

namespace ArchiveManager.Infrastructure.Data;

/// <summary>
/// Owns the SQLite connection string and hands out short-lived connections.
/// Microsoft.Data.Sqlite connections are cheap to open/close (pooled internally),
/// so services open one per operation rather than sharing a single long-lived
/// connection across threads — simpler and safer for a WPF app with async calls.
/// </summary>
public sealed class SqliteConnectionFactory : IDisposable
{
    private readonly string _connectionString;

    public string DatabasePath { get; }

    public SqliteConnectionFactory(string databasePath)
    {
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            ForeignKeys = true
        }.ToString();
    }

    public SqliteConnection CreateOpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public void Dispose()
    {
        // Nothing to hold open — connections are created per-call. Kept for
        // symmetry / future pooled-connection use.
        SqliteConnection.ClearAllPools();
    }
}
