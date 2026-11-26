# Tests

Run `scripts/Test.ps1` for the x64 test process, or add `-Platform x86` for a
32-bit test process. Reports are written to `.work/test-results/`. Tests use
isolated files and registry keys without changing actual login registration,
clipboard contents or personal settings.

Coverage includes configuration migration and concurrent writes, translation
fallback, path formatting, AnyCPU metadata, diagnostic privacy, portable first-run
registration, startup registration, theme persistence, daily log rotation,
folder snapshots, application-data cleanup and file hashing (known vectors,
folder-depth expansion, deduplication, parallel batches, cancellation and failures),
dispatcher-owned hash view models, STA worker shutdown, permission rule transformations
and complete-selection transaction handling without changing filesystem permissions. Real Explorer
interaction, login startup and native 32-bit Windows need separate verification.

Optional native permission check after building:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\PermissionSmoke\Invoke-Smoke.ps1
```

Run unelevated; add `-Platform x86` for a 32-bit process. This explicitly changes
ACLs only on newly created fixtures, using the production permission service and
its UAC worker. It checks deny/grant recovery, custom rights, selected identities
and unchanged child ACLs. Preopened handles restore access for cleanup; fixtures
are removed and reports remain under `.work/permission-smoke-*/`. It does not
start the installed app or change its settings or registration.

Optional Shell integration check after building:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\ShellSmoke\Invoke-Smoke.ps1
```

This runs an isolated COM selection forwarder and WPF dispatcher model, checks
x64/x86 COM clients and a native mixed-selection context menu, then removes its
temporary registry entries and stops its host. It does not open the installed
application or use its activation channel. Reports stay under `.work/shell-smoke-*/`.

For a registered installation, exit the application and run
`tests/ShellSmoke/Invoke-InstalledSmoke.ps1 -ApplicationDirectory <directory>`.
It invokes the real menu and reads the WPF completion summary through UI Automation.
The second client verifies reuse of the shared host. Closing the window hides it;
exit the application explicitly after the check.

Manual checks cover tray popup behavior, Explorer restart, drag-and-drop,
light/dark themes, language switching and 100%–200% display scaling.
