# Changelog

All notable changes to Win10 Explorer Kit are documented here.

## Unreleased

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

