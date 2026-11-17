# Project Guidelines

## Structure

- Do not use the `superpowers` skill
- Keep application code in `src/`, tests in `tests/`, build scripts in `scripts/`, and installer definitions in `packaging/`
- Separate application lifetime, UI, feature logic, configuration, and Windows integration
- Keep the product name `Win10 Explorer Kit` untranslated

## Code and Comments

- Write English comments above the relevant code, without trailing periods or end-of-line comments
- Document named functions and methods with their purpose, parameters, and return values: use XML documentation for C# and comment-based help for PowerShell; omit return documentation for void methods
- Give each configuration constant, path, error code, and time threshold its own comment explaining meaning or units
- Explain state ownership, lifecycle, failure handling, and non-obvious decisions; update comments when behavior changes
- Keep UI strings in locale JSON files and use `TranslationService.T`, with English fallback
- Persist preferences through the configuration service and preserve unknown INI entries

## Build and Documentation

- Maintain concise English and Chinese README files with matching information and an accurate directory tree
- Keep release history in the English CHANGELOG; use version placeholders in general documentation
- Define version metadata in `Directory.Build.props`; packaging increments `BuildRevision` on success while release names use the three-part `Version`
- Use `scripts/Build.ps1`, `scripts/Test.ps1`, and `scripts/Package.ps1` as the standard build, test, and packaging entry points
- Keep release files under `artifacts/<version>/` and temporary work and test reports under `.work/`; package only the declared distribution files
- Verify the relevant tests, package contents, and checksums; distinguish automated checks from manual platform validation
