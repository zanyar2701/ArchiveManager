using System;
using ArchiveManager.Infrastructure.Data;

namespace ArchiveManager.Application.Services;

/// <summary>Writes audit-trail rows (spec §20/§23). Never throws into the caller's
/// happy path — a logging failure should not block the underlying operation, but
/// it is itself logged to Logs/ via the caller's normal exception handling.</summary>
public sealed class ActivityLogService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public ActivityLogService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public void Log(string action, int? documentId, string? detailsJson, int? operatorId)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO ActivityLog (Timestamp, OperatorId, Action, DocumentId, DetailsJson)
                             VALUES ($ts, $op, $action, $docId, $details);";
        cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$op", (object?)operatorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$docId", (object?)documentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$details", (object?)detailsJson ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
