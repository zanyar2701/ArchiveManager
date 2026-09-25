using System;
using System.IO;
using System.Linq;
using ArchiveManager.Application.Services;
using ArchiveManager.Domain.Models;
using ArchiveManager.Infrastructure.Data;
using ArchiveManager.Infrastructure.FileSystem;
using Xunit;

namespace ArchiveManager.Tests;

/// <summary>
/// Covers the seven scenarios from the entity-identity addendum end to end,
/// against a real (temp-folder) SQLite database and a real (temp-folder)
/// archive tree — these are small integration tests rather than pure unit
/// tests, because the behavior under test (folder-move-then-DB-commit,
/// stable ID across identity changes) is inherently about that interaction.
/// </summary>
public sealed class EntityTransitionServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _archiveRoot;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SettingsService _settings;
    private readonly ActivityLogService _activityLog;
    private readonly ArchiveTreeService _treeService;
    private readonly EntityTransitionService _transitionService;
    private readonly SearchService _searchService;

    public EntityTransitionServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ArchiveManagerTests_" + Guid.NewGuid().ToString("N"));
        _archiveRoot = Path.Combine(_tempRoot, "Archive");
        Directory.CreateDirectory(_archiveRoot);

        var dbPath = Path.Combine(_tempRoot, "test.db");
        _connectionFactory = new SqliteConnectionFactory(dbPath);
        new DbInitializer(_connectionFactory).InitializeAndSeed();

        var configPath = Path.Combine(_tempRoot, "settings.json");
        _settings = SettingsService.LoadOrCreate(configPath, configPath); // no default file — fine, falls back to new AppSettings()
        _settings.UpdateArchiveRoot(_archiveRoot);

        _activityLog = new ActivityLogService(_connectionFactory);
        _treeService = new ArchiveTreeService(_connectionFactory, _settings, new FileTypeMapperService());
        _transitionService = new EntityTransitionService(_connectionFactory, _settings, _activityLog);
        _searchService = new SearchService(_connectionFactory);
    }

    public void Dispose()
    {
        _connectionFactory.Dispose();
        try { Directory.Delete(_tempRoot, true); } catch { /* best effort cleanup */ }
    }

    private int CreatePersonInPreGrowth(string name)
    {
        var preGrowthDir = Path.Combine(_archiveRoot, "01 - رشد مقدماتی");
        Directory.CreateDirectory(Path.Combine(preGrowthDir, $"001-{name}"));
        _treeService.SynchronizeWithDatabase();

        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT CompanyId FROM Companies WHERE Name = $name;";
        cmd.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private Company LoadEntity(int id)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT CompanyId, Name, ArchiveLevelId, FolderPath, PreviousLevelId, CreatedAt, EntityType, ArchiveCode, UpdatedAt
                             FROM Companies WHERE CompanyId = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return new Company
        {
            CompanyId = reader.GetInt32(0),
            Name = reader.GetString(1),
            ArchiveLevelId = reader.GetInt32(2),
            FolderPath = reader.GetString(3),
            PreviousLevelId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            CreatedAt = DateTime.Parse(reader.GetString(5)),
            EntityType = reader.GetString(6) == "Person" ? EntityType.Person : EntityType.Company,
            ArchiveCode = reader.GetString(7)
        };
    }

    private int LevelIdByCode(string code)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ArchiveLevelId FROM ArchiveLevels WHERE Code = $code;";
        cmd.Parameters.AddWithValue("$code", code);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ---------------------------- Test 1 ----------------------------
    [Fact]
    public void CreatePerson_InPreGrowth_GeneratesArchiveCode()
    {
        var id = CreatePersonInPreGrowth("محمد احمدی");
        var entity = LoadEntity(id);

        Assert.Equal(EntityType.Person, entity.EntityType);
        Assert.StartsWith("A-", entity.ArchiveCode);
        Assert.Equal("01", ArchiveLevelCode(entity.ArchiveLevelId));
    }

    // ---------------------------- Test 2 ----------------------------
    [Fact]
    public void TransferStage_PersonToCompany_PreservesIdAndCode_UpdatesTypeStageName()
    {
        var id = CreatePersonInPreGrowth("محمد احمدی");
        var before = LoadEntity(id);

        var result = _transitionService.TransferStage(new StageTransferRequest(
            EntityId: id,
            ToStageId: LevelIdByCode("02"),
            NewEntityType: EntityType.Company,
            NewName: "شرکت فناوران کردستان",
            Reason: "ثبت رسمی شرکت",
            OperatorId: null));

        Assert.True(result.Succeeded, result.ErrorMessage);

        var after = LoadEntity(id);
        Assert.Equal(before.CompanyId, after.CompanyId);          // same internal ID
        Assert.Equal(before.ArchiveCode, after.ArchiveCode);       // same stable code
        Assert.Equal(EntityType.Company, after.EntityType);
        Assert.Equal("02", ArchiveLevelCode(after.ArchiveLevelId));
        Assert.Equal("شرکت فناوران کردستان", after.Name);

        var history = _transitionService.GetHistory(id);
        Assert.Contains(history, e => e.EventType == "انتقال مرحله");
        Assert.Contains(history, e => e.EventType == "تغییر هویت");
        Assert.Contains(history, e => e.EventType == "تغییر نام");
    }

    // ---------------------------- Test 3 ----------------------------
    [Fact]
    public void Search_ByPreviousName_FindsCurrentEntity()
    {
        var id = CreatePersonInPreGrowth("محمد احمدی");
        _transitionService.TransferStage(new StageTransferRequest(
            id, LevelIdByCode("02"), EntityType.Company, "شرکت فناوران کردستان", null, null));

        var results = _searchService.SearchEntities("محمد احمدی");

        Assert.Contains(results, r => r.CompanyId == id && r.CurrentName == "شرکت فناوران کردستان");
        Assert.Equal("محمد احمدی", results.First(r => r.CompanyId == id).MatchedPreviousName);
    }

    // ---------------------------- Test 4 ----------------------------
    [Fact]
    public void TransferStage_GrowthToPark_KeepsIdentityUnchanged()
    {
        var id = CreatePersonInPreGrowth("محمد احمدی");
        _transitionService.TransferStage(new StageTransferRequest(id, LevelIdByCode("02"), EntityType.Company, "شرکت فناوران کردستان", null, null));

        var result = _transitionService.TransferStage(new StageTransferRequest(
            id, LevelIdByCode("03"), EntityType.Company, "شرکت فناوران کردستان", "ورود به پارکی", null));

        Assert.True(result.Succeeded, result.ErrorMessage);
        var after = LoadEntity(id);
        Assert.Equal(id, after.CompanyId);
        Assert.Equal("شرکت فناوران کردستان", after.Name);
        Assert.Equal("03", ArchiveLevelCode(after.ArchiveLevelId));
    }

    // ---------------------------- Test 5 ----------------------------
    [Fact]
    public void TransferStage_ParkToExited_MovesFolder_PreservesIdentityAndHistory()
    {
        var id = CreatePersonInPreGrowth("محمد احمدی");
        _transitionService.TransferStage(new StageTransferRequest(id, LevelIdByCode("02"), EntityType.Company, "شرکت فناوران کردستان", null, null));
        _transitionService.TransferStage(new StageTransferRequest(id, LevelIdByCode("03"), EntityType.Company, "شرکت فناوران کردستان", null, null));

        var beforeFolder = LoadEntity(id).FolderPath;
        var result = _transitionService.TransferStage(new StageTransferRequest(
            id, LevelIdByCode("04"), EntityType.Company, "شرکت فناوران کردستان", "خروج از پارک", null));

        Assert.True(result.Succeeded, result.ErrorMessage);
        var after = LoadEntity(id);

        Assert.NotEqual(beforeFolder, after.FolderPath);
        Assert.True(Directory.Exists(Path.Combine(_archiveRoot, after.FolderPath)));
        Assert.False(Directory.Exists(Path.Combine(_archiveRoot, beforeFolder)));
        Assert.Equal(id, after.CompanyId); // identity unchanged
        Assert.Equal(3, _transitionService.GetHistory(id).Count(e => e.EventType == "انتقال مرحله"));
    }

    // ---------------------------- Test 6 ----------------------------
    [Fact]
    public void RenameEntity_KeepsOldNameSearchable()
    {
        var id = CreatePersonInPreGrowth("محمد احمدی");
        _transitionService.TransferStage(new StageTransferRequest(id, LevelIdByCode("02"), EntityType.Company, "شرکت فناوران کردستان", null, null));

        var result = _transitionService.RenameEntity(new RenameEntityRequest(id, "شرکت فناوری کردستان", "اصلاح نام", null));
        Assert.True(result.Succeeded, result.ErrorMessage);

        var results = _searchService.SearchEntities("فناوران کردستان");
        Assert.Contains(results, r => r.CompanyId == id);
    }

    // ---------------------------- Test 7 ----------------------------
    [Fact]
    public void Rename_DoesNotTouchHistoricalDocumentFilenames()
    {
        // Register a document while the entity is still "محمد احمدی", then
        // rename the entity, and verify the historical Documents.FileName row
        // is untouched (addendum: "historical filename is not automatically renamed").
        var id = CreatePersonInPreGrowth("محمد احمدی");
        var entity = LoadEntity(id);
        var folderAbsolute = Path.Combine(_archiveRoot, entity.FolderPath);

        var sourceFile = Path.Combine(_tempRoot, "source.pdf");
        File.WriteAllText(sourceFile, "dummy pdf content");

        var namingService = new FilenameGenerationService(new PersianTextNormalizer());
        var duplicateService = new DuplicateDetectionService();
        var registrationService = new DocumentRegistrationService(
            _connectionFactory, _settings, namingService, duplicateService, _activityLog, new FileTypeMapperService());

        var regResult = registrationService.Register(new RegistrationRequest(
            SourceFilePath: sourceFile,
            DestinationFolderAbsolutePath: folderAbsolute,
            DestinationFolderRelativePath: entity.FolderPath,
            CompanyId: id,
            DocumentTypeName: "قرارداد",
            SubjectName: "استقرار",
            Year: 1404,
            Version: null,
            Extension: "pdf",
            CompanyDisplayName: entity.Name,
            OperatorId: null));

        Assert.True(regResult.Succeeded, regResult.ErrorMessage);
        var expectedHistoricalName = "قرارداد استقرار 1404 محمد احمدی.pdf";
        Assert.Equal(expectedHistoricalName, regResult.GeneratedFileName);

        // Now transition the entity — historical filename must remain as-is.
        _transitionService.TransferStage(new StageTransferRequest(
            id, LevelIdByCode("02"), EntityType.Company, "شرکت فناوران کردستان", null, null));

        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT FileName FROM Documents WHERE CompanyId = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        var storedFileName = (string)cmd.ExecuteScalar()!;

        Assert.Equal(expectedHistoricalName, storedFileName);
    }

    private string ArchiveLevelCode(int levelId)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Code FROM ArchiveLevels WHERE ArchiveLevelId = $id;";
        cmd.Parameters.AddWithValue("$id", levelId);
        return (string)cmd.ExecuteScalar()!;
    }
}
