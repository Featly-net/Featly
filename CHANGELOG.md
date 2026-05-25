# Changelog

All notable changes to Featly will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Until version `1.0.0`, the public API is unstable and minor versions may introduce breaking changes. Breaking changes will be called out explicitly in the notes for each release.

## [Unreleased]

### Added

- Initial project foundation: `ARCHITECTURE.md`, `PLAN.md`, `CONTRIBUTING.md`, `NOTICE.md`, `README.md`, `LICENSE` (MIT).
- Repository scaffolding: `SECURITY.md`, `CHANGELOG.md`, issue and pull request templates, CODEOWNERS, Dependabot configuration, ADR template.
- **M1 skeleton.** Solution structure with 11 `src/` projects, 5 `tests/` projects, 2 `samples/` projects. Build infrastructure: `global.json`, `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props` (Central Package Management).
- Placeholder contract types in `Featly.Abstractions`: `Flag`, `Variant`, `FlagType`, `Config`, `ConfigType`, `EvaluationContext`, `EvaluationReason`, `EvaluationResult<T>`, `IFeatlyClient`, `IFeatlyStore`.
- `Featly.Server.MapFeatlyApi()` mounts a `GET /health/live` liveness probe.
- `Featly.Dashboard.MapFeatlyDashboard("/featly")` serves an embedded "coming soon" placeholder.
- `Featly.Storage.InMemory.AddFeatlyInMemoryStore()` registers a no-op `IFeatlyStore` so the server can boot.
- `samples/SelfHosted.Sample` runs the embedded host end-to-end.
- GitHub Actions CI workflow: matrix [ubuntu-latest, windows-latest] with .NET 8 + .NET 10 SDKs. Restore / build (warnings-as-errors) / test / pack preview.
- `STATUS.md` tracking the active milestone.

### Changed

_(nothing yet)_

### Deprecated

_(nothing yet)_

### Removed

_(nothing yet)_

### Fixed

_(nothing yet)_

### Security

_(nothing yet)_

---

<!--
Release entries follow this shape:

## [0.1.0] - YYYY-MM-DD

### Added
- ...

### Changed
- ...

[Unreleased]: https://github.com/Featly-net/Featly/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Featly-net/Featly/releases/tag/v0.1.0
-->

[Unreleased]: https://github.com/Featly-net/Featly/commits/main
