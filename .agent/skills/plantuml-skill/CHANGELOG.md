# Changelog

All notable changes to the `plantuml-skill` will be documented in this file.

## [2.2.1] - 2026-09-28
### Changed
- The description names real triggers (diagram types, "render this .puml", C4 container diagrams, syntax fixes) and points to `diagram-generator` for choosing a tool. It used to open with "Enterprise-grade engine…", which gave the harness nothing to match.
- Versions 2.0.0 to 2.2.0 were released without changelog entries; see the git history.

## [1.2.0] - 2026-04-14
### Added
- Created `evals/evals.json` with 3 complex test cases (Sequence, C4, State).
- Added professional `assets/premium-theme.puml` (Tokyonight-inspired).
- Added `references/syntax-guide.md` with verified C4 library URLs.
- Added `references/troubleshooting.md`.

### Optimized
- Hardcoded `plantuml-stdlib` URLs in syntax-guide to prevent 404 errors.
- Improved `SKILL.md` description for better triggering and specificity.
- Fixed path resilience for nested `!include` statements.

## [1.1.0] - 2026-04-14
### Added
- Integrated self-healing provisioning logic in `scripts/render.py` (winget/brew/apt support).
- Automatic `plantuml.jar` download.

## [1.0.0] - 2026-04-14
### Added
- Initial release of the `plantuml-skill`.
- Core rendering functionality via Java/CLI.
