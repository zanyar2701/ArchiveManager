# Archive Manager

A Windows desktop application (C# / .NET 8 / WPF, MVVM, local SQLite) for managing
a Science & Technology Park's company archive: browsing the archive tree, registering
and auto-naming documents, tracking an entity's identity as it moves from an
individual applicant to a registered company and through the park's growth stages,
searching across current and historical names, and reporting on the archive.

This repository is the **complete source project**. It has not been compiled in the
environment that produced it (a Linux sandbox with no network access and no .NET SDK
installed — see "Environment limitations" below) but every file is real, complete
C#/XAML, written and statically reviewed against the specification, ready to open and
build on a Windows machine with the .NET 8 SDK.

## What this application does

- Browses the archive (`01 - رشد مقدماتی`, `02 - رشد`, `03 - پارکی`, `04 - خروج‌یافته`)
  as a tree, with a Explorer-like center file list per folder.
- Registers a document through a guided form (نوع سند / موضوع / سال / نسخه / نام شرکت /
  فرمت فایل) that live-generates the standard filename
  `[نوع سند] [موضوع] [سال] [نام شرکت].[فرمت]` (optionally ` نسخه NN` before the
  extension), checks for name collisions, and safely copies/renames the file into
  place — never silently overwriting anything.
- Tracks each archived person/company as a stable **entity** with a permanent internal
  ID and a never-changing archive code (`A-0001`, ...), independent of its current
  name, identity type (Person/Company), or stage. An entity can be **transferred**
  between stages (optionally changing its name and identity type at the same time,
  e.g. a person becoming a registered company), **renamed** without changing stage,
  and its full **history** reviewed as one chronological timeline.
- Historical filenames are **never** renamed automatically when an entity's name
  changes — only new documents use the current name.
- Searches by filename, document type, subject, year, current entity name, **or any
  previous name / archive code the entity has ever had**.
- Reports archive-wide and entity-identity statistics (documents by year/type/company/
  level/format, entities by stage, recent transfers, recent renames, person→company
  transitions, duplicate filenames, missing files).
- Runs as a **portable, install-free** folder (`ArchiveManager.exe` + `Data/`,
  `Config/`, `Backup/`, `Logs/`) against a local SQLite database that is created and
  migrated automatically on first run.

## Environment limitations (read this first)

This project was built in a Linux container with **no network access** and **no .NET
SDK installed**, and WPF is Windows-only (it depends on `PresentationFramework`,
which does not exist on Linux/macOS). That means:

- **No `.exe` was produced, and none could be produced here.** There is nothing to
  "download and run" from this environment — the deliverable is the source project.
- **No `dotnet build`/`dotnet test` was run.** Correctness was instead checked
  statically: every `.xaml` file was parsed as XML and validated well-formed; every
  `.cs` file was checked for balanced braces/parens; every `x:Class` was cross-checked
  against its code-behind; every `StaticResource`/`x:Static` reference in XAML was
  cross-checked against its definition; every service/ViewModel constructor was
  cross-checked against every call site, including in the tests; and the full
  `Microsoft.Data.Sqlite` transaction-per-command requirement was audited across the
  whole codebase (this caught and fixed one real bug — see `ASSUMPTIONS.md`).
- **Build and run it yourself** on Windows with the .NET 8 SDK — see
  `BUILD_INSTRUCTIONS.md` for exact commands. Please treat the first build as a real
  first build: report anything the static review couldn't catch (a compiler will
  always find more than eyes and greps can), and it will be fixed.

## Project layout

```
ArchiveManager/
├── ArchiveManager.sln
├── src/ArchiveManager/              # the WPF application
│   ├── App.xaml(.cs)                # composition root — wires up every service
│   ├── Domain/Models/               # plain data models (Company, Document, EntityTransition, ...)
│   ├── Infrastructure/
│   │   ├── Data/                    # SqliteConnectionFactory, DbInitializer (schema + migrations)
│   │   └── FileSystem/              # SafeFileWriter, PersianTextNormalizer, FileTypeMapperService
│   ├── Application/Services/        # the actual application logic (naming, registration,
│   │                                 #   search, statistics, backup, entity transitions, ...)
│   ├── UI/
│   │   ├── ViewModels/              # MVVM ViewModels, one per screen/dialog
│   │   ├── Views/                   # MainWindow + Dialogs/ (transfer, rename, history)
│   │   └── Converters/              # small XAML value converters
│   ├── Resources/Theme/             # Colors.xaml / Typography.xaml / Styles.xaml (design tokens)
│   ├── Localization/                # strings.fa.json / strings.en.json
│   └── Config/settings.default.json
├── tests/ArchiveManager.Tests/       # xUnit tests (naming engine + entity-identity scenarios)
├── README.md                         # this file
├── BUILD_INSTRUCTIONS.md
├── TEST_CHECKLIST.md
├── ASSUMPTIONS.md
└── FEATURES.md
```

## Quick start (on Windows, once built)

1. Run `ArchiveManager.exe`.
2. On first launch, you'll be asked to choose the archive root folder (the folder
   that contains `01 - رشد مقدماتی`, `02 - رشد`, etc.) — see Settings if you skip it.
3. Browse the tree on the left, pick a company/person, and use **"＋ ثبت سند جدید"**
   in the center panel to register a new document.
4. Right-click a company/person in the tree for **تغییر نام**, **تغییر مرحله / انتقال
   پرونده**, **تاریخچه پرونده**, **مشاهده اطلاعات**, or **باز کردن پوشه**.
5. Use the top-bar search for a quick, cross-company search — it also matches an
   entity's previous names and its archive code.

## Documentation

- `BUILD_INSTRUCTIONS.md` — exact commands to build/publish/test on Windows.
- `TEST_CHECKLIST.md` — manual QA checklist covering the full spec, including the
  entity-identity workflows.
- `ASSUMPTIONS.md` — every place the spec was ambiguous and a reasonable engineering
  decision was made instead, plus the one real bug the static review caught and fixed.
- `FEATURES.md` — feature-by-feature status against both the original specification
  and the entity-identity addendum.
