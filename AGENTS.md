# Project Guidelines

## Structure

- Do not use the `superpowers` skill
- Keep application code in `src/`, tests in `tests/`, build scripts in `scripts/`, and installer definitions in `packaging/`
- Separate application lifetime, UI, feature logic, configuration, and Windows integration
- Keep the product name `Win10 Explorer Kit` untranslated

## Code and Comments

- Comments are written in English; only file headers, function/method comments, and constant comments are required
- Do not add trailing comments; put any inline note on the line above the code and do not end comments with a period
- Document named functions and methods with their purpose, parameters, and return values: use XML documentation for C# and comment-based help for PowerShell; omit return documentation for void methods
- Give each configuration constant, path, error code, and time threshold its own comment explaining meaning or units
- Keep comments concise and update them when behavior changes; avoid narrating obvious code
- Keep UI strings in locale JSON files and use `TranslationService.T`, with English fallback
- Persist preferences through the configuration service and preserve unknown INI entries

## Build and Documentation

- Maintain concise English and Chinese README files with matching information and an accurate directory tree
- Keep release history in the English CHANGELOG; use version placeholders in general documentation
- Define version metadata in `Directory.Build.props`; packaging increments `BuildRevision` on success while release names use the three-part `Version`
- Use `scripts/Build.ps1`, `scripts/Test.ps1`, and `scripts/Package.ps1` as the standard build, test, and packaging entry points
- Keep release files under `artifacts/<version>/` and temporary work and test reports under `.work/`; package only the declared distribution files
- Verify the relevant tests, package contents, and checksums; distinguish automated checks from manual platform validation

## Commit Workflow

- When the user requests code changes, implement and verify them, then create a local commit automatically without waiting for another request to commit
- If the user requests a correction or addition to the same just-completed task, and no independent work has started in between, update the previous commit with `git commit --amend --no-edit`
- Create a new commit for a new feature or independent change; do not amend it into an unrelated task
- Keep unrelated work separate and respect explicit user instructions about commit boundaries, excluded projects, and whether to commit
- A local commit does not authorize pushing, tagging, deploying, or publishing
