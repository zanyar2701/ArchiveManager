# Manual QA Checklist

Run through this after a successful build (`BUILD_INSTRUCTIONS.md`). Grouped by area;
each line is a single, concrete check.

## First run / setup
- [ ] Fresh `Data/`, `Config/`, `Backup/`, `Logs/` folders are created next to the exe.
- [ ] With no archive root configured, the app prompts and routes to Settings rather
      than showing a silently-empty tree.
- [ ] After picking a valid archive root folder and saving, the tree populates with
      the four archive levels and any existing company folders underneath them.
- [ ] Pointing the archive root at a folder that does **not** contain any of the four
      level folders still opens without crashing (empty tree, no false entities).

## Archive navigation
- [ ] Expanding/collapsing a level node works; a company node has no expand arrow.
- [ ] Selecting a company loads its files into the center panel and updates the
      breadcrumb + status bar.
- [ ] Sorting each file-table column (نام فایل، نوع، سال، حجم، تاریخ ثبت) works
      ascending/descending.
- [ ] The local filter box narrows the visible files live as you type.
- [ ] An empty company folder shows the "این پوشه خالی است" empty state.
- [ ] Double-clicking a file opens it with its OS-default application.

## Document registration & naming
- [ ] Filling نوع سند + سال + فرمت فایل (with a file selected) live-updates the
      filename preview; leaving موضوع blank produces no double space.
- [ ] Adding a نسخه appends ` نسخه NN` correctly, zero-padded.
- [ ] "کپی نام" copies exactly the previewed filename to the clipboard.
- [ ] "ثبت در بایگانی" with all required fields present succeeds, the file appears
      copied/moved into the folder under the exact previewed name, and the row shows
      up in the center table immediately after.
- [ ] Attempting to register a name that already exists in the folder shows the
      duplicate dialog instead of silently overwriting; choosing "ایجاد نسخه جدید"
      computes and applies the next free version number correctly.
- [ ] Registering with a required field empty shows a clear inline message and does
      not touch the file system.

## Entity identity — creation & basic info
- [ ] A newly discovered folder under "01 - رشد مقدماتی" is created as a **Person**
      with a generated archive code (`A-000N`); a newly discovered folder anywhere
      else is created as a **Company**.
- [ ] A folder named with a numeric prefix (e.g. `001-محمد احمدی`) results in an
      entity whose **name** is `محمد احمدی` (no prefix) — check via "مشاهده اطلاعات"
      and check that a filename generated for it does **not** contain "001-".
- [ ] "مشاهده اطلاعات" shows archive code, current name, type, stage, and previous
      names (or "—" if none).

## تغییر نام (rename, no stage change)
- [ ] Renaming updates the displayed name everywhere (tree, center panel title,
      registration panel) without changing the entity's stage.
- [ ] With "تغییر نام پوشه فیزیکی نیز انجام شود" checked, the physical folder is
      renamed; unchecked, it is left alone while the DB name still updates.
- [ ] The old name remains findable via search afterward (see Search section).
- [ ] A document registered before the rename keeps its original filename — it is
      **not** renamed automatically.

## تغییر مرحله / انتقال پرونده (stage transfer)
- [ ] Transferring رشد مقدماتی → رشد with a new Company name and identity type moves
      the physical folder into the "02 - رشد" folder, and the entity's internal ID
      and archive code are unchanged (check "مشاهده اطلاعات" before/after).
- [ ] Transferring رشد → پارکی and پارکی → خروج‌یافته each work; the exited entity's
      folder lands under "04 - خروج‌یافته" and its *previous* stage is preserved
      (visible in تاریخچه پرونده).
- [ ] If the destination folder name is already taken, the transfer is refused with a
      clear message and **nothing** changes on disk or in the database.
- [ ] Temporarily making the archive folder read-only (or otherwise blocking the
      move) causes the transfer to fail cleanly, with the database left exactly as it
      was before the attempt (spec's core invariant: folder move must succeed before
      any DB write).
- [ ] Filling in optional company-registration fields (شناسه ملی, شماره ثبت, ...)
      during a transfer to Company saves them; leaving them all blank saves nothing
      (no empty `CompanyProfiles` row is forced into existence).

## تاریخچه پرونده (history)
- [ ] Shows creation, every stage transfer, every identity-type change, and every
      name change, in chronological order, with dates and both old/new values.
- [ ] A reason/description entered on a transfer or rename appears in the history.

## Search
- [ ] Searching an entity's **current** name finds its documents.
- [ ] Searching an entity's **previous** name (before a rename or transfer) still
      finds its current documents, and the quick-search entity result shows
      "نام قبلی: ...".
- [ ] Searching an entity's stable archive code (`A-0001`) finds it regardless of
      current name.
- [ ] Searching a document type + subject + year (e.g. "قرارداد استقرار 1404") finds
      matching documents across **every** company without navigating into folders.
- [ ] A query with no matches shows the "نتیجه‌ای یافت نشد" empty state.

## Reports
- [ ] Company/document counts, by-year/type/company/level/format breakdowns, and the
      entity-identity counts (افراد در پیش‌رشد / شرکت‌ها در رشد / ... / خروج‌یافته)
      all match what's actually in the archive.
- [ ] The "فهرست پرونده‌ها" (company list) table shows every entity with correct
      کد پرونده / نام فعلی / نوع / مرحله / نام قبلی / تاریخ ورود / آخرین تغییر / وضعیت.
    - [ ] "انتقال‌های اخیر" and "تبدیل‌های شخص به شرکت" lists reflect recent activity.
- [ ] Nothing on this screen is editable — it's read-only end to end.

## Settings, backup, localization
- [ ] Changing the archive root path and saving takes effect without a restart.
- [ ] "پشتیبان‌گیری از پایگاه داده" creates a timestamped `.db` file in `Backup/`.
- [ ] "بازیابی پایگاه داده" restores a chosen backup after a confirmation prompt, and
      itself takes a fresh backup of the current state first.
- [ ] Switching FA/EN in Settings updates the UI language and persists across restart.

## Safety / error handling
- [ ] Opening a file that's locked by another program and then trying to register a
      document with the same source file shows a plain-language error, not a raw
      exception.
- [ ] Disconnecting/removing the archive drive mid-session and then trying to browse
      shows a clear, non-crashing error.
- [ ] Running "بررسی سلامت بایگانی" (if wired to a UI entry point) correctly reports
      missing files (in DB, not on disk) and unregistered files (on disk, not in DB)
      without deleting or altering any existing record on its own.

## Packaging
- [ ] The published portable folder runs on a clean Windows machine with no .NET
      installed (self-contained single-file build).
- [ ] No admin-rights prompt appears on launch.
