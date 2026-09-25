using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ArchiveManager.Infrastructure.Data;

/// <summary>
/// Creates the schema on first run and applies additive migrations on
/// existing databases. Uses PRAGMA user_version as a lightweight migration
/// marker instead of a full migrations framework — appropriate at this scale
/// (spec §27, dev notes). Migrations are additive-only (ALTER TABLE ADD
/// COLUMN / CREATE TABLE IF NOT EXISTS) so upgrading NEVER drops or rewrites
/// existing archive data (entity-identity addendum: "do not destroy the
/// existing database... do not perform destructive migration").
///
/// IMPORTANT Microsoft.Data.Sqlite detail: once a transaction is active on a
/// connection, EVERY command executed on that connection must have its
/// .Transaction property explicitly set, or it throws at runtime — unlike
/// some other ADO.NET providers, it is never implicit. Every command below
/// is built through the small NewCommand() helper for exactly this reason.
/// </summary>
public sealed class DbInitializer
{
    // v1: original schema (Companies/Documents/... as shipped in the first build).
    // v2: entity identity management — EntityType/ArchiveCode/UpdatedAt on
    //     Companies, plus EntityNameHistory / EntityTransitions / CompanyProfiles.
    private const int CurrentSchemaVersion = 2;
    private readonly SqliteConnectionFactory _connectionFactory;

    public DbInitializer(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public void InitializeAndSeed()
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        var version = GetUserVersion(connection);

        if (version < 1)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    CreateSchemaV1(connection, transaction);
                    SeedReferenceDataV1(connection, transaction);
                    SetUserVersion(connection, transaction, 1);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            version = 1;
        }

        if (version < 2)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    MigrateToV2(connection, transaction);
                    SetUserVersion(connection, transaction, 2);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            version = 2;
        }
    }

    private static SqliteCommand NewCommand(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        if (transaction is not null) cmd.Transaction = transaction;
        return cmd;
    }

    private static int GetUserVersion(SqliteConnection connection)
    {
        using var cmd = NewCommand(connection, null, "PRAGMA user_version;");
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void SetUserVersion(SqliteConnection connection, SqliteTransaction transaction, int version)
    {
        // PRAGMA statements cannot take bound parameters, but `version` is an
        // internal int constant we control (never user input), so string
        // interpolation here is safe.
        using var cmd = NewCommand(connection, transaction, $"PRAGMA user_version = {version};");
        cmd.ExecuteNonQuery();
    }

    // ============================= V1 =============================

    private static void CreateSchemaV1(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var cmd = NewCommand(connection, transaction, @"
CREATE TABLE IF NOT EXISTS ArchiveLevels (
    ArchiveLevelId INTEGER PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL UNIQUE,
    Name TEXT NOT NULL,
    SortOrder INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS Companies (
    CompanyId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    ArchiveLevelId INTEGER NOT NULL REFERENCES ArchiveLevels(ArchiveLevelId),
    FolderPath TEXT NOT NULL,
    PreviousLevelId INTEGER NULL REFERENCES ArchiveLevels(ArchiveLevelId),
    CreatedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Companies_ArchiveLevelId ON Companies(ArchiveLevelId);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Companies_Name_Level ON Companies(Name, ArchiveLevelId);

CREATE TABLE IF NOT EXISTS DocumentTypes (
    DocumentTypeId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE,
    IsActive INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS DocumentSubjects (
    SubjectId INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE,
    IsActive INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS FileFormats (
    FileFormatId INTEGER PRIMARY KEY AUTOINCREMENT,
    Extension TEXT NOT NULL UNIQUE,
    Label TEXT NOT NULL,
    IconKey TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Operators (
    OperatorId INTEGER PRIMARY KEY AUTOINCREMENT,
    DisplayName TEXT NOT NULL,
    Role TEXT NOT NULL DEFAULT 'Operator'
);

CREATE TABLE IF NOT EXISTS Settings (
    Key TEXT PRIMARY KEY,
    Value TEXT
);

CREATE TABLE IF NOT EXISTS Documents (
    DocumentId INTEGER PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NOT NULL REFERENCES Companies(CompanyId),
    DocumentTypeId INTEGER NOT NULL REFERENCES DocumentTypes(DocumentTypeId),
    SubjectId INTEGER NULL REFERENCES DocumentSubjects(SubjectId),
    Year INTEGER NOT NULL,
    Version INTEGER NULL,
    FileFormatId INTEGER NOT NULL REFERENCES FileFormats(FileFormatId),
    FileName TEXT NOT NULL,
    RelativePath TEXT NOT NULL,
    SizeBytes INTEGER NOT NULL DEFAULT 0,
    RegisteredAt TEXT NOT NULL,
    RegisteredByOperatorId INTEGER NULL REFERENCES Operators(OperatorId),
    IsMissing INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS IX_Documents_CompanyId ON Documents(CompanyId);
CREATE INDEX IF NOT EXISTS IX_Documents_Year ON Documents(Year);
CREATE INDEX IF NOT EXISTS IX_Documents_DocumentTypeId ON Documents(DocumentTypeId);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Documents_RelativePath ON Documents(RelativePath);

CREATE TABLE IF NOT EXISTS ActivityLog (
    LogId INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp TEXT NOT NULL,
    OperatorId INTEGER NULL REFERENCES Operators(OperatorId),
    Action TEXT NOT NULL,
    DocumentId INTEGER NULL REFERENCES Documents(DocumentId),
    DetailsJson TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_ActivityLog_Timestamp ON ActivityLog(Timestamp);

-- Full-text index mirroring searchable document text (spec §10/§18).
CREATE VIRTUAL TABLE IF NOT EXISTS DocumentsFTS USING fts5(
    FileName, CompanyName, DocumentTypeName, SubjectName, Year,
    content=''
);
");
        cmd.ExecuteNonQuery();
    }

    private static void SeedReferenceDataV1(SqliteConnection connection, SqliteTransaction transaction)
    {
        void Exec(string sql)
        {
            using var cmd = NewCommand(connection, transaction, sql);
            cmd.ExecuteNonQuery();
        }

        // Archive levels — fixed per the org's structure, spec §4/§25.
        Exec(@"INSERT INTO ArchiveLevels (Code, Name, SortOrder) VALUES
            ('01', 'رشد مقدماتی', 1),
            ('02', 'رشد', 2),
            ('03', 'پارکی', 3),
            ('04', 'خروج‌یافته', 4);");

        // Starter document types — spec §37, editable later via Settings.
        Exec(@"INSERT INTO DocumentTypes (Name) VALUES
            ('قرارداد'), ('نامه'), ('گزارش'), ('صورتجلسه'), ('مجوز'),
            ('گواهینامه'), ('اظهارنامه'), ('فاکتور'), ('ارزیابی'),
            ('مکاتبات'), ('مستندات فنی'), ('مستندات فروش'), ('سایر');");

        // Starter subjects — spec §38.
        Exec(@"INSERT INTO DocumentSubjects (Name) VALUES
            ('استقرار'), ('تمدید استقرار'), ('ارزیابی'), ('کارگروه'),
            ('فعالیت'), ('مالی'), ('حقوقی'), ('محصول'), ('فنی'), ('فروش');");

        // File format map — spec §8.
        Exec(@"INSERT INTO FileFormats (Extension, Label, IconKey) VALUES
            ('pdf', 'PDF', 'pdf'), ('doc', 'Word', 'word'), ('docx', 'Word', 'word'),
            ('xls', 'Excel', 'excel'), ('xlsx', 'Excel', 'excel'), ('csv', 'Excel', 'excel'),
            ('ppt', 'PowerPoint', 'ppt'), ('pptx', 'PowerPoint', 'ppt'),
            ('mdb', 'Access', 'access'), ('accdb', 'Access', 'access'),
            ('zip', 'ZIP', 'zip'), ('rar', 'ZIP', 'zip'), ('7z', 'ZIP', 'zip'),
            ('jpg', 'Image', 'image'), ('jpeg', 'Image', 'image'), ('png', 'Image', 'image'),
            ('bmp', 'Image', 'image'), ('tif', 'Image', 'image'),
            ('mp4', 'Video', 'video'), ('mov', 'Video', 'video'), ('avi', 'Video', 'video'),
            ('txt', 'Text', 'text');");

        // Default operator (Administrator) so the app is usable immediately;
        // ASSUMPTION: no login screen — see README "Operators & roles" note.
        Exec(@"INSERT INTO Operators (DisplayName, Role) VALUES ('اپراتور پیش‌فرض', 'Administrator');");
    }

    // ============================= V2 — Entity Identity Management =============================

    private static void MigrateToV2(SqliteConnection connection, SqliteTransaction transaction)
    {
        // --- 1. Additive columns on Companies (SQLite ADD COLUMN never touches
        //        existing rows/data — safe, non-destructive). ---
        AddColumnIfMissing(connection, transaction, "Companies", "EntityType", "TEXT NOT NULL DEFAULT 'Company'");
        AddColumnIfMissing(connection, transaction, "Companies", "ArchiveCode", "TEXT");
        AddColumnIfMissing(connection, transaction, "Companies", "UpdatedAt", "TEXT");

        // --- 2. New tables (spec addendum: EntityNameHistory, EntityTransitions, CompanyProfiles) ---
        using (var cmd = NewCommand(connection, transaction, @"
CREATE TABLE IF NOT EXISTS EntityNameHistory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EntityId INTEGER NOT NULL REFERENCES Companies(CompanyId),
    Name TEXT NOT NULL,
    NameType TEXT NOT NULL,          -- 'Person' | 'Company'
    FromDate TEXT NOT NULL,
    ToDate TEXT NULL,
    IsCurrent INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS IX_EntityNameHistory_EntityId ON EntityNameHistory(EntityId);
CREATE INDEX IF NOT EXISTS IX_EntityNameHistory_Name ON EntityNameHistory(Name);

CREATE TABLE IF NOT EXISTS EntityTransitions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EntityId INTEGER NOT NULL REFERENCES Companies(CompanyId),
    FromStageId INTEGER NOT NULL REFERENCES ArchiveLevels(ArchiveLevelId),
    ToStageId INTEGER NOT NULL REFERENCES ArchiveLevels(ArchiveLevelId),
    FromEntityType TEXT NOT NULL,
    ToEntityType TEXT NOT NULL,
    PreviousName TEXT NOT NULL,
    NewName TEXT NOT NULL,
    Date TEXT NOT NULL,
    Reason TEXT NULL,
    OperatorId INTEGER NULL REFERENCES Operators(OperatorId)
);
CREATE INDEX IF NOT EXISTS IX_EntityTransitions_EntityId ON EntityTransitions(EntityId);
CREATE INDEX IF NOT EXISTS IX_EntityTransitions_Date ON EntityTransitions(Date);

CREATE TABLE IF NOT EXISTS CompanyProfiles (
    EntityId INTEGER PRIMARY KEY REFERENCES Companies(CompanyId),
    NationalId TEXT NULL,
    RegistrationNumber TEXT NULL,
    RegistrationDate TEXT NULL,
    CompanyType TEXT NULL,
    Ceo TEXT NULL,
    ContactInformation TEXT NULL,
    Website TEXT NULL
);
"))
        {
            cmd.ExecuteNonQuery();
        }

        // --- 3. Backfill existing Companies rows so the new feature works
        //        immediately on an upgraded database, without discarding
        //        anything (addendum: "create stable ArchiveEntity records
        //        for existing companies"). ---
        BackfillExistingCompanies(connection, transaction);
    }

    private static void AddColumnIfMissing(SqliteConnection connection, SqliteTransaction transaction, string table, string column, string definition)
    {
        using (var check = NewCommand(connection, transaction, $"PRAGMA table_info({table});"))
        {
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                var existingColumn = reader.GetString(1); // column "name" is index 1
                if (string.Equals(existingColumn, column, StringComparison.OrdinalIgnoreCase))
                {
                    return; // already present — nothing to do (idempotent across re-runs)
                }
            }
        }

        using var alter = NewCommand(connection, transaction, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
        alter.ExecuteNonQuery();
    }

    private static void BackfillExistingCompanies(SqliteConnection connection, SqliteTransaction transaction)
    {
        // ASSUMPTION: for pre-existing rows we cannot know whether an entity
        // was originally filed as a Person — we infer EntityType from its
        // CURRENT archive level as a reasonable default (level "01 - رشد
        // مقدماتی" → Person, everything else → Company), and the operator
        // can correct it per-entity afterwards via "تغییر نام"/"انتقال پرونده".
        var toBackfill = new List<(int Id, string Name, DateTime CreatedAt, bool IsPreGrowth)>();

        using (var select = NewCommand(connection, transaction, @"
SELECT c.CompanyId, c.Name, c.CreatedAt, al.Code
FROM Companies c
JOIN ArchiveLevels al ON al.ArchiveLevelId = c.ArchiveLevelId
WHERE c.ArchiveCode IS NULL OR c.ArchiveCode = '';"))
        {
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                toBackfill.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    DateTime.Parse(reader.GetString(2)),
                    reader.GetString(3) == "01"));
            }
        }

        // Determine the next free sequence number so codes stay unique even
        // if some rows were already migrated in a partially-applied run.
        var nextSequence = 1;
        using (var maxCmd = NewCommand(connection, transaction, "SELECT ArchiveCode FROM Companies WHERE ArchiveCode LIKE 'A-%';"))
        {
            using var reader = maxCmd.ExecuteReader();
            while (reader.Read())
            {
                var code = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (code is { Length: > 2 } && int.TryParse(code[2..], out var n) && n >= nextSequence)
                {
                    nextSequence = n + 1;
                }
            }
        }

        foreach (var row in toBackfill)
        {
            var archiveCode = $"A-{nextSequence:D4}";
            nextSequence++;
            var entityType = row.IsPreGrowth ? "Person" : "Company";
            var nowIso = DateTime.UtcNow.ToString("O");

            using (var update = NewCommand(connection, transaction,
                "UPDATE Companies SET ArchiveCode = $code, EntityType = $type, UpdatedAt = $updated WHERE CompanyId = $id;"))
            {
                update.Parameters.AddWithValue("$code", archiveCode);
                update.Parameters.AddWithValue("$type", entityType);
                update.Parameters.AddWithValue("$updated", nowIso);
                update.Parameters.AddWithValue("$id", row.Id);
                update.ExecuteNonQuery();
            }

            using (var history = NewCommand(connection, transaction,
                @"INSERT INTO EntityNameHistory (EntityId, Name, NameType, FromDate, ToDate, IsCurrent)
                  VALUES ($id, $name, $type, $from, NULL, 1);"))
            {
                history.Parameters.AddWithValue("$id", row.Id);
                history.Parameters.AddWithValue("$name", row.Name);
                history.Parameters.AddWithValue("$type", entityType);
                history.Parameters.AddWithValue("$from", row.CreatedAt.ToString("O"));
                history.ExecuteNonQuery();
            }
        }
    }
}
