using System;
using System.IO;
using System.Text.Json;
using ArchiveManager.Domain.Models;
using ArchiveManager.Infrastructure.Data;
using ArchiveManager.Infrastructure.FileSystem;
using Microsoft.Data.Sqlite;

namespace ArchiveManager.Application.Services;

public sealed record RegistrationRequest(
    string SourceFilePath,
    string DestinationFolderAbsolutePath,
    string DestinationFolderRelativePath,
    int CompanyId,
    string DocumentTypeName,
    string? SubjectName,
    int Year,
    int? Version,
    string Extension,
    string CompanyDisplayName,
    int? OperatorId);

public sealed class RegistrationResult
{
    public bool Succeeded { get; init; }
    public bool RequiresDuplicateResolution { get; init; }
    public string? GeneratedFileName { get; init; }
    public string? FinalPath { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Orchestrates the full "register a document" workflow (spec §9): generate
/// name → check duplicate → safe-write → persist metadata → log activity.
/// This is the one place that ties the naming engine, duplicate detection,
/// safe file writer, and database together, so the workflow stays atomic
/// from the caller's point of view.
/// </summary>
public sealed class DocumentRegistrationService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SettingsService _settings;
    private readonly FilenameGenerationService _namingService;
    private readonly DuplicateDetectionService _duplicateService;
    private readonly ActivityLogService _activityLog;
    private readonly FileTypeMapperService _fileTypes;
    private readonly SafeFileWriter _fileWriter = new();

    public DocumentRegistrationService(
        SqliteConnectionFactory connectionFactory,
        SettingsService settings,
        FilenameGenerationService namingService,
        DuplicateDetectionService duplicateService,
        ActivityLogService activityLog,
        FileTypeMapperService fileTypes)
    {
        _connectionFactory = connectionFactory;
        _settings = settings;
        _namingService = namingService;
        _duplicateService = duplicateService;
        _activityLog = activityLog;
        _fileTypes = fileTypes;
    }

    public string PreviewFileName(RegistrationRequest request)
    {
        var parts = new FilenameParts(
            request.DocumentTypeName, request.SubjectName, request.Year,
            request.CompanyDisplayName, request.Version, request.Extension);
        return _namingService.Generate(parts);
    }

    /// <summary>
    /// Attempts registration. If a collision exists and the caller has not
    /// already resolved it (by supplying a Version), returns
    /// RequiresDuplicateResolution = true instead of writing anything —
    /// the UI must then re-call with a chosen version or an explicit override.
    /// </summary>
    public RegistrationResult Register(RegistrationRequest request, bool overwriteConfirmedByAdmin = false)
    {
        var fileName = PreviewFileName(request);
        var destinationFolder = request.DestinationFolderAbsolutePath;

        var collision = _duplicateService.Exists(destinationFolder, fileName);
        if (collision && !overwriteConfirmedByAdmin)
        {
            return new RegistrationResult
            {
                Succeeded = false,
                RequiresDuplicateResolution = true,
                GeneratedFileName = fileName
            };
        }

        try
        {
            var mode = _settings.Current.TransferPolicy == FileTransferPolicy.Move
                ? FileTransferMode.Move
                : FileTransferMode.Copy;

            string finalPath;
            if (collision && overwriteConfirmedByAdmin)
            {
                // Explicit, typed-confirmation overwrite (Administrator-only in the UI layer).
                File.Delete(Path.Combine(destinationFolder, fileName));
                finalPath = _fileWriter.TransferInto(request.SourceFilePath, destinationFolder, fileName, mode);
            }
            else
            {
                finalPath = _fileWriter.TransferInto(request.SourceFilePath, destinationFolder, fileName, mode);
            }

            var documentId = PersistMetadata(request, fileName, finalPath);

            _activityLog.Log(
                overwriteConfirmedByAdmin ? "جایگزینی سند" : "ثبت سند",
                documentId,
                JsonSerializer.Serialize(new { fileName, request.DestinationFolderRelativePath }),
                request.OperatorId);

            return new RegistrationResult { Succeeded = true, GeneratedFileName = fileName, FinalPath = finalPath };
        }
        catch (SafeFileWriter.ConflictException)
        {
            return new RegistrationResult { Succeeded = false, RequiresDuplicateResolution = true, GeneratedFileName = fileName };
        }
        catch (IOException ex)
        {
            return new RegistrationResult { Succeeded = false, ErrorMessage = TranslateIoError(ex) };
        }
        catch (UnauthorizedAccessException)
        {
            return new RegistrationResult { Succeeded = false, ErrorMessage = "دسترسی لازم برای ثبت در این پوشه وجود ندارد." };
        }
    }

    /// <summary>Renames an already-registered document (spec §9 "Rename" flow).</summary>
    public RegistrationResult Rename(int documentId, string oldAbsolutePath, RegistrationRequest request)
    {
        var newFileName = PreviewFileName(request);
        try
        {
            var newPath = _fileWriter.RenameInPlace(oldAbsolutePath, newFileName);

            using var connection = _connectionFactory.CreateOpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE Documents SET
                DocumentTypeId = (SELECT DocumentTypeId FROM DocumentTypes WHERE Name = $type),
                SubjectId = (SELECT SubjectId FROM DocumentSubjects WHERE Name = $subject),
                Year = $year, Version = $version, FileName = $fileName,
                RelativePath = $relPath
                WHERE DocumentId = $id;";
            cmd.Parameters.AddWithValue("$type", request.DocumentTypeName);
            cmd.Parameters.AddWithValue("$subject", (object?)request.SubjectName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$year", request.Year);
            cmd.Parameters.AddWithValue("$version", (object?)request.Version ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$fileName", newFileName);
            cmd.Parameters.AddWithValue("$relPath", Path.Combine(request.DestinationFolderRelativePath, newFileName));
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.ExecuteNonQuery();

            _activityLog.Log("تغییر نام", documentId,
                JsonSerializer.Serialize(new { oldPath = oldAbsolutePath, newFileName }), request.OperatorId);

            return new RegistrationResult { Succeeded = true, GeneratedFileName = newFileName, FinalPath = newPath };
        }
        catch (SafeFileWriter.ConflictException)
        {
            return new RegistrationResult { Succeeded = false, RequiresDuplicateResolution = true, GeneratedFileName = newFileName };
        }
        catch (IOException ex)
        {
            return new RegistrationResult { Succeeded = false, ErrorMessage = TranslateIoError(ex) };
        }
    }

    private int PersistMetadata(RegistrationRequest request, string fileName, string finalAbsolutePath)
    {
        using var connection = _connectionFactory.CreateOpenConnection();

        var formatId = GetOrCreateFormatId(connection, request.Extension);
        var typeId = GetOrCreateLookupId(connection, "DocumentTypes", "DocumentTypeId", request.DocumentTypeName);
        int? subjectId = string.IsNullOrWhiteSpace(request.SubjectName)
            ? null
            : GetOrCreateLookupId(connection, "DocumentSubjects", "SubjectId", request.SubjectName!);

        var relativePath = Path.Combine(request.DestinationFolderRelativePath, fileName);
        var size = new FileInfo(finalAbsolutePath).Length;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO Documents
            (CompanyId, DocumentTypeId, SubjectId, Year, Version, FileFormatId, FileName, RelativePath, SizeBytes, RegisteredAt, RegisteredByOperatorId, IsMissing)
            VALUES ($companyId, $typeId, $subjectId, $year, $version, $formatId, $fileName, $relPath, $size, $registeredAt, $operatorId, 0);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$companyId", request.CompanyId);
        cmd.Parameters.AddWithValue("$typeId", typeId);
        cmd.Parameters.AddWithValue("$subjectId", (object?)subjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$year", request.Year);
        cmd.Parameters.AddWithValue("$version", (object?)request.Version ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$formatId", formatId);
        cmd.Parameters.AddWithValue("$fileName", fileName);
        cmd.Parameters.AddWithValue("$relPath", relativePath);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$registeredAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$operatorId", (object?)request.OperatorId ?? DBNull.Value);

        var documentId = Convert.ToInt32(cmd.ExecuteScalar());

        using var fts = connection.CreateCommand();
        fts.CommandText = @"INSERT INTO DocumentsFTS (rowid, FileName, CompanyName, DocumentTypeName, SubjectName, Year)
                             VALUES ($id, $fileName, $company, $type, $subject, $year);";
        fts.Parameters.AddWithValue("$id", documentId);
        fts.Parameters.AddWithValue("$fileName", fileName);
        fts.Parameters.AddWithValue("$company", request.CompanyDisplayName);
        fts.Parameters.AddWithValue("$type", request.DocumentTypeName);
        fts.Parameters.AddWithValue("$subject", (object?)request.SubjectName ?? "");
        fts.Parameters.AddWithValue("$year", request.Year.ToString());
        fts.ExecuteNonQuery();

        return documentId;
    }

    private static int GetOrCreateFormatId(SqliteConnection connection, string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT FileFormatId FROM FileFormats WHERE Extension = $ext;";
            select.Parameters.AddWithValue("$ext", ext);
            var existing = select.ExecuteScalar();
            if (existing is not null) return Convert.ToInt32(existing);
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = @"INSERT INTO FileFormats (Extension, Label, IconKey) VALUES ($ext, $label, 'other');
                                SELECT last_insert_rowid();";
        insert.Parameters.AddWithValue("$ext", ext);
        insert.Parameters.AddWithValue("$label", "سایر");
        return Convert.ToInt32(insert.ExecuteScalar());
    }

    private static int GetOrCreateLookupId(SqliteConnection connection, string table, string idColumn, string name)
    {
        using (var select = connection.CreateCommand())
        {
            select.CommandText = $"SELECT {idColumn} FROM {table} WHERE Name = $name;";
            select.Parameters.AddWithValue("$name", name);
            var existing = select.ExecuteScalar();
            if (existing is not null) return Convert.ToInt32(existing);
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO {table} (Name, IsActive) VALUES ($name, 1); SELECT last_insert_rowid();";
        insert.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(insert.ExecuteScalar());
    }

    private static string TranslateIoError(IOException ex)
    {
        // Never surface a raw exception to the operator (spec §32).
        if (ex.Message.Contains("used by another process", StringComparison.OrdinalIgnoreCase) ||
            ex.HResult == unchecked((int)0x80070020))
        {
            return "امکان انجام عملیات وجود ندارد؛ ممکن است فایل توسط برنامه دیگری در حال استفاده باشد. لطفاً فایل را ببندید و دوباره تلاش کنید.";
        }
        return "در حین ثبت سند خطایی رخ داد. لطفاً دوباره تلاش کنید.";
    }
}
