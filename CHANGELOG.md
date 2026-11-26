# Changelog

All notable changes to Win10 Explorer Kit are documented here.

## [2.1.0] - 2026-11-26

### Added

- File and folder permission management from Explorer or by drag-and-drop.
- Quick actions to deny or grant full control to existing users and groups.
- Custom permissions for selected users and groups, with batch progress and cancellation.

### Fixed

- Fixed clipboard errors and delays when copying paths or hash results.
- Fixed custom permission changes affecting unrelated access rules or leaving allowed file access blocked.
- Preserve the current login startup setting during upgrades.

## [2.0.1] - 2026-11-23

### Fixed

- Fixed an error when opening the tray menu.
- Fixed UI freezes when copying paths or automatically recording Explorer folders.
- Improved responsiveness when hashing large selections and saving settings.
- Improved application shutdown and background task cleanup.

## [2.0.0] - 2026-11-20

### Added

- Calculate MD5, SHA-1, SHA-256 and SHA-512 hashes from Explorer or by dragging files and folders into the app.
- Adjustable concurrency and folder depth, with progress tracking and cancellation.
- Copy individual hashes or all results.
- Light, dark and system themes.

### Changed

- Redesigned interface with sidebar navigation for file hashing, folder recovery and settings.

### Fixed

- Fixed timeouts when opening the hash tool from Explorer on 64-bit Windows.

## [1.1.0] - 2026-11-18

### Added

- Optional event-driven Explorer folder snapshots, double-click tray restoration and a configurable list of fixed folders.

### Changed

- Keep settings and daily logs beside the executable, migrate existing data on upgrade and preserve the configured language.

## [1.0.0] - 2026-11-17

### Added

- Explorer "Copy path (Ctrl+Shift+C)" menu and shortcut with multi-selection support.
- English and Simplified Chinese JSON locales, installer language selection, first-run ZIP language detection and English fallback.
- Left- and right-click tray menus with Settings and Language entries, plus a settings window for language, path quoting and login startup.
- A per-user Setup.exe installer with destination folder selection and a Portable.zip package.
- Automatic first-run ZIP registration and a single Uninstall.cmd for registration and personal-data cleanup.
- An AnyCPU application targeting Windows 10 x86/x64.
- Automatic removal of the application data directory during uninstall.
- Local release packaging with SHA-256 checksums for manual GitHub uploads.
- MIT license and English/Chinese documentation.

