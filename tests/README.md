# Tests

Run `scripts/Test.ps1` for the x64 test process, or add `-Platform x86` for a
32-bit test process. Reports are written to `.work/test-results/`. Tests use
isolated files and registry keys without changing actual login registration,
clipboard contents or personal settings.

Coverage includes configuration migration and concurrent writes, translation
fallback, path formatting, AnyCPU metadata, diagnostic privacy, portable first-run
registration, startup registration, tray popup window styles, daily log rotation,
folder snapshots, application-data cleanup and file hashing (known vectors,
first-level expansion, deduplication, parallel batches, cancellation and failures). Real Explorer
interaction, login startup and native 32-bit Windows need separate verification.

Optional Shell integration check after building:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\ShellSmoke\Invoke-Smoke.ps1
```

This opens an isolated hash window, checks x64/x86 COM clients and a native
mixed-selection context menu, then removes its temporary registry entries and
stops its host. Use `-Count 10000` for a larger batch or `-Language zh-CN` for the
Chinese window. Reports and a screenshot stay under `.work/shell-smoke-*/`.

To verify a registered installation or portable directory, close its hash window
and run `tests/ShellSmoke/Invoke-InstalledSmoke.ps1 -ApplicationDirectory <directory>`.
This uses the shipped EXEs and real context-menu registration, checks cold starts
from both client architectures, and closes each completed test window.
