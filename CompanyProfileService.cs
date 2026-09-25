using System;
using ArchiveManager.Domain.Models;
using ArchiveManager.Infrastructure.Data;

namespace ArchiveManager.Application.Services;

/// <summary>
/// Reads/writes the optional CompanyProfile row (شناسه ملی, شماره ثبت, ...).
/// Only ever relevant once an entity IS a Company — never required for a
/// Person in pre-growth (addendum "COMPANY REGISTRATION INFORMATION").
/// </summary>
public sealed class CompanyProfileService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public CompanyProfileService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public CompanyProfile? Load(int entityId)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT EntityId, NationalId, RegistrationNumber, RegistrationDate, CompanyType, Ceo, ContactInformation, Website
                             FROM CompanyProfiles WHERE EntityId = $id;";
        cmd.Parameters.AddWithValue("$id", entityId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new CompanyProfile
        {
            EntityId = reader.GetInt32(0),
            NationalId = reader.IsDBNull(1) ? null : reader.GetString(1),
            RegistrationNumber = reader.IsDBNull(2) ? null : reader.GetString(2),
            RegistrationDate = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)),
            CompanyType = reader.IsDBNull(4) ? null : reader.GetString(4),
            Ceo = reader.IsDBNull(5) ? null : reader.GetString(5),
            ContactInformation = reader.IsDBNull(6) ? null : reader.GetString(6),
            Website = reader.IsDBNull(7) ? null : reader.GetString(7)
        };
    }

    /// <summary>Upsert — called after an entity transitions to Company, or
    /// whenever the operator edits its registration info directly.</summary>
    public void Save(CompanyProfile profile)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO CompanyProfiles (EntityId, NationalId, RegistrationNumber, RegistrationDate, CompanyType, Ceo, ContactInformation, Website)
VALUES ($id, $national, $regNum, $regDate, $type, $ceo, $contact, $website)
ON CONFLICT(EntityId) DO UPDATE SET
    NationalId = excluded.NationalId,
    RegistrationNumber = excluded.RegistrationNumber,
    RegistrationDate = excluded.RegistrationDate,
    CompanyType = excluded.CompanyType,
    Ceo = excluded.Ceo,
    ContactInformation = excluded.ContactInformation,
    Website = excluded.Website;";
        cmd.Parameters.AddWithValue("$id", profile.EntityId);
        cmd.Parameters.AddWithValue("$national", (object?)profile.NationalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$regNum", (object?)profile.RegistrationNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$regDate", (object?)profile.RegistrationDate?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$type", (object?)profile.CompanyType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ceo", (object?)profile.Ceo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$contact", (object?)profile.ContactInformation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$website", (object?)profile.Website ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
