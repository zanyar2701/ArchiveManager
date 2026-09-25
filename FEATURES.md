# Feature Status

Legend: ✅ implemented · 🟡 implemented, partial/simplified · ⬜ not implemented in this pass

## Original specification (core requirements list, §1)

| # | Feature | Status | Notes |
|---|---|---|---|
| 1 | Archive folder navigation | ✅ | `ArchiveTreeService` + tree in `MainWindow.xaml` |
| 2 | Company archive management | ✅ | extended into full entity-identity management |
| 3 | Archive levels (4 fixed stages) | ✅ | seeded in `DbInitializer` |
| 4 | Company folders | ✅ | |
| 5 | Document browsing | ✅ | `FileBrowserViewModel`, sortable/filterable table |
| 6 | Document registration | ✅ | `DocumentRegistrationService` |
| 7 | Automatic filename generation | ✅ | `FilenameGenerationService`, unit-tested |
| 8 | File renaming | ✅ | `DocumentRegistrationService.Rename` |
| 9 | File registration into archive | ✅ | `SafeFileWriter` (write-temp-then-atomic-move) |
| 10 | File format handling | ✅ | `FileTypeMapperService` |
| 11 | Search | ✅ | FTS5 + entity name-history/archive-code matching |
| 12 | Filtering | ✅ | local file-table filter + advanced `SearchFilters` |
| 13 | Statistics | ✅ | `StatisticsService` |
| 14 | Reports | ✅ | Reports tab in `MainWindow.xaml` |
| 15 | Read-only archive browsing | ✅ | browsing never writes; mutating actions are explicit |
| 16 | SQLite database | ✅ | `Microsoft.Data.Sqlite`, hand-rolled schema + migrations |
| 17 | Backup | ✅ | `BackupService` (`VACUUM INTO`, rotation, restore-with-safety-net) |
| 18 | Activity logging | ✅ | `ActivityLogService`, `ActivityLog` table |
| 19 | Duplicate detection | ✅ | `DuplicateDetectionService` + resolution dialog |
| 20 | Version management | ✅ | optional `نسخه`, auto-suggested next free version |
| 21 | Persian RTL interface | ✅ | `FlowDirection=RightToLeft`, RTL-first layout |
| 22 | English LTR interface | 🟡 | language switch + localization JSON exist; not every screen currently reads from the string table (see Assumptions) |
| 23 | Settings | ✅ | archive root, language, digits, backup/restore |
| 24 | Archive guide | ✅ | `ArchiveGuideViewModel`, static Persian guide text |
| 25 | Keyboard shortcuts | 🟡 | Enter/F5/double-click wired; Ctrl+F/Ctrl+N/Ctrl+C/F2 not yet bound as global `InputBindings` |
| 26 | Safe file operations | ✅ | `SafeFileWriter`, never blind-overwrites |
| 27 | Error handling | ✅ | plain-Persian messages, no raw exceptions surfaced |
| 28 | Empty states | ✅ | empty folder, empty search results |
| 29 | Confirmation dialogs | ✅ | duplicate resolution, restore-backup confirmation |
| 30 | First-run setup | ✅ | prompts for archive root if unconfigured |

## Entity-identity addendum

| Feature | Status | Notes |
|---|---|---|
| Person vs. Company entity type | ✅ | `EntityType` enum, `Companies.EntityType` |
| Stable internal ID across identity changes | ✅ | `Companies.CompanyId` never changes |
| Stable archive code (`A-0001`) | ✅ | generated once, never regenerated |
| Name history (multiple previous names) | ✅ | `EntityNameHistory` table |
| تغییر مرحله / انتقال پرونده (stage transfer) | ✅ | `EntityTransitionService.TransferStage`, folder-move-before-DB-write invariant |
| تغییر نام (rename without stage change) | ✅ | `EntityTransitionService.RenameEntity` |
| تاریخچه پرونده (history timeline) | ✅ | `EntityTransitionService.GetHistory` + dialog |
| Historical filenames untouched on rename/transfer | ✅ | verified by `Rename_DoesNotTouchHistoricalDocumentFilenames` test |
| Optional bulk "update old filenames" action | ⬜ | intentionally not wired to a UI action this pass — see `ASSUMPTIONS.md` |
| Search by previous name / archive code | ✅ | `SearchService.SearchEntities`, extended `Search` |
| Archive code never changes | ✅ | generated once at entity creation, never regenerated |
| Company registration info (شناسه ملی, ...) | 🟡 | collected inline in the transfer dialog when becoming a Company; no separate always-visible edit screen |
| خروج‌یافته preserves prior stage | ✅ | `Companies.PreviousLevelId` set on transfer into "04" |
| COMPANY LIST screen | ✅ | table inside the Reports tab |
| Reporting: entities by stage / recent transfers / renames / Person→Company | ✅ | `StatisticsService` |
| Context menu (مشاهده پرونده / ثبت سند / تغییر نام / تغییر مرحله / تاریخچه / مشاهده اطلاعات / باز کردن پوشه) | 🟡 | all wired except "ثبت سند" (menu item present but not yet routed — use the center panel's "＋ ثبت سند جدید" instead) |
| Database is additive/non-destructive on upgrade | ✅ | `DbInitializer` v1→v2 migration, `ALTER TABLE ADD COLUMN`, backfill |
| 7 required test scenarios | ✅ | `EntityTransitionServiceTests.cs`, one test per scenario |
