# Assumptions & Engineering Decisions

Every item below is a place the specification (either the original UI/UX spec, the
"build it" prompt, or the entity-identity addendum) left a decision to engineering
judgment, plus the one real defect the static review process caught. Each is labeled
by which document it responds to.

## Bugs found and fixed during review

- **Microsoft.Data.Sqlite transaction requirement (`DbInitializer.cs`).** Unlike some
  ADO.NET providers, `Microsoft.Data.Sqlite` requires every `SqliteCommand` executed
  while a transaction is active on its connection to have that transaction explicitly
  assigned to `command.Transaction`, or it throws at runtime. The first version of
  `DbInitializer` opened a transaction and then created several commands without
  setting this, which would have thrown on the very first launch. Fixed by threading
  the transaction through every command via a small `NewCommand(...)` helper. This was
  caught by manually auditing every `BeginTransaction()` call site in the codebase
  against every command created afterward — not by an actual compiler/runtime, since
  none was available in the authoring environment (see `README.md`).
- **`TreeView.SelectedItem` is read-only in WPF and cannot be bound directly in XAML.**
  The original `MainWindow.xaml` bound everything else about the tree (`ItemsSource`,
  `IsExpanded`, `IsSelected` per-item) but nothing forwarded an actual click into
  `ArchiveTreeViewModel.SelectedNode` — meaning the core "click a company to browse
  it" interaction would silently never have fired from the UI (programmatic selection,
  e.g. from a search result, still worked). Fixed by handling
  `TreeView.SelectedItemChanged` in `MainWindow.xaml.cs` and forwarding the new node
  into the ViewModel, and by having `ArchiveTreeViewModel.SelectedNode`'s setter also
  keep `TreeNodeViewModel.IsSelected` in sync in both directions so programmatic and
  real-click selection look and behave identically.
- **Numeric folder-name prefixes were leaking into the entity's logical name.** The
  entity-identity addendum's own examples show folders like `001-محمد احمدی`, but
  `ArchiveTreeService.SynchronizeWithDatabase` was storing the *entire* folder name
  (prefix included) as `Companies.Name` — which would have produced filenames like
  `... 001-محمد احمدی.pdf` instead of `... محمد احمدی.pdf`, contradicting every naming
  example in both the original spec and the addendum. Fixed with
  `StripNumericFolderPrefix`, applied when an entity is first created from a folder;
  `EntityTransitionService.BuildFolderLeafName` already re-attached the same style of
  prefix on the way back out, so the two are now symmetric.

Both of the first two were caught by a manual static-review pass (cross-checking
every `BeginTransaction` call site; cross-checking every XAML binding path against
its ViewModel and re-deriving what WPF actually does at runtime for each binding
type) rather than by an actual compiler or running the app — see `README.md`'s
"Environment limitations" for why, and `BUILD_INSTRUCTIONS.md` for how to verify
these (and catch anything the review missed) on a real Windows machine.

## Original specification

- **No login/authentication screen.** A single default "Administrator" operator is
  seeded on first run; role-gating exists in the data model (`Operator.Role`) but the
  UI does not yet expose a way to switch or add operators. Appropriate for a small
  local-office tool per the spec's own "no unnecessary enterprise security" guidance.
- **Copy vs. move on registration.** Defaults to **copy** the source file into the
  archive (`AppSettings.TransferPolicy`), never deleting the operator's original file
  automatically. Configurable to Move in Settings' underlying model (not yet exposed
  as a UI toggle).
- **Persian year default.** The registration form defaults سال to the current Persian
  (Jalali) year via `System.Globalization.PersianCalendar`, editable by the operator.
- **Font.** `Vazirmatn, Segoe UI, Tahoma` — see `BUILD_INSTRUCTIONS.md` "Fonts" for how
  to embed Vazirmatn directly in the portable build instead of relying on it being
  installed on the target machine.
- **FTS5 ranking.** Quick search ORs each whitespace-separated term in the query
  against the `DocumentsFTS` virtual table — adequate at the scale of a single
  organization's archive; a fancier ranking function was judged unnecessary complexity.
- **Digit style (Persian vs. Latin numerals).** A `UsePersianDigits` setting exists in
  the data model; the UI does not yet apply it to every numeric display — dates and
  sizes currently render with standard (Latin) digits regardless of the setting.

## Entity-identity addendum

- **`Companies` table extended in place, not replaced.** The addendum's conceptual
  `ArchiveEntity` is the existing `Companies` table — it already had the stable
  internal ID (`CompanyId`), current stage (`ArchiveLevelId`), and folder path the
  addendum asks for. Migration `v2` adds `EntityType`, `ArchiveCode`, `UpdatedAt`
  columns via `ALTER TABLE ADD COLUMN` (additive, never destructive) plus three new
  tables (`EntityNameHistory`, `EntityTransitions`, `CompanyProfiles`), and backfills
  every pre-existing row with a generated archive code and a best-guess entity type.
- **Backfill entity-type guess for pre-existing data.** An upgraded database cannot
  know whether an existing company was originally a Person — `MigrateToV2` infers
  Person for anything currently in "01 - رشد مقدماتی" and Company everywhere else.
  The operator can correct any individual entity afterward via "تغییر نام"/"انتقال
  پرونده"; nothing is locked in.
- **Numeric folder prefixes (e.g. `001-محمد احمدی`) are a folder-naming convention,
  not part of the entity's logical name.** `ArchiveTreeService.StripNumericFolderPrefix`
  strips a leading `\d+-` when deriving `Companies.Name` from a folder, and
  `EntityTransitionService.BuildFolderLeafName` re-attaches the *same* prefix when
  building a new folder name during a transfer/rename — so generated filenames never
  accidentally contain "001-", matching every worked example in both the original
  spec and the addendum.
- **New-company creation stays inside `ArchiveTreeService.SynchronizeWithDatabase`,**
  triggered by discovering an unrecognized folder on disk, rather than adding a
  separate "create entity" UI flow — consistent with the existing "sync, never
  silently delete" design already in place before the addendum.
- **"Reasonable reverse/correction transitions"** (the addendum explicitly allows
  these) are implemented as: the same `TransferStage`/`RenameEntity` operations with
  no directional restriction — an operator can transfer رشد → رشد مقدماتی exactly the
  same way as the forward direction, always logged and always requiring the same
  confirmation dialog. No separate "correction mode" was added; the addendum didn't
  ask for different UX for corrections versus forward moves, only that both be
  possible, confirmed, and logged.
- **"به‌روزرسانی نام فایل‌های قدیمی" (bulk-rename historical files) is *not*
  implemented as a UI action in this pass** — the addendum is explicit that it "must
  NEVER happen automatically" and is optional; the service-layer pieces it would need
  (`DocumentRegistrationService.Rename`, per-file) already exist, but wiring an
  explicit opt-in bulk action was judged lower priority than the core transfer/rename/
  history/search work given the scope of this pass. Flagged here rather than silently
  dropped.
- **Company registration fields (شناسه ملی, شماره ثبت, ...) collected only inline in
  the transfer dialog**, shown/used solely when the new identity type is Company, and
  saved only if the operator actually fills at least one field — never required, per
  the addendum. `RegistrationDate` exists in the data model but has no dedicated input
  control in this pass (str/date picker was judged non-essential to the addendum's
  core ask); it can be added the same way the other fields were.
- **"COMPANY LIST" (spec section of that name) implemented as a table inside the
  existing Reports screen** rather than as a new top-level tab, since its nature —
  a read-only, filterable overview of every entity — fits the Reports screen's
  existing "read-only browsing/reporting" role and avoids adding a fifth top-level
  navigation destination for what is fundamentally another report.
