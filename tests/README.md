# Tests

Run `scripts/Test.ps1` for the x64 test process, or add `-Platform x86` for a
32-bit test process. Reports are written to `.work/test-results/`. Tests use
isolated files and registry keys without changing actual login registration,
clipboard contents or personal settings.

Coverage includes configuration migration and concurrent writes, translation
fallback, path formatting, AnyCPU metadata, diagnostic privacy, portable first-run
registration, startup registration, tray popup window styles, daily log rotation, folder snapshots and application-data cleanup. Real Explorer
interaction, login startup and native 32-bit Windows need separate verification.
