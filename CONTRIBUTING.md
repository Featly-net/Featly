# Contributing to Featly

Thanks for your interest. Featly is an open-source project and contributions are welcome — code, documentation, ideas, bug reports, performance reports, and questions are all useful.

This document covers what you need to know before opening a pull request. If something here is unclear or out of date, opening an issue is itself a useful contribution.

## Code of conduct

Participation in Featly is governed by the [Code of Conduct](CODE_OF_CONDUCT.md). By contributing, you agree to follow it.

## Before you start coding

Featly is in early development with strong design opinions. Before investing time in a non-trivial change, open an issue or a draft PR describing what you intend to do. This avoids the painful case of building something we won't merge because it conflicts with the architectural direction.

The architectural direction is in [ARCHITECTURE.md](ARCHITECTURE.md). The implementation plan and current milestone are in [PLAN.md](PLAN.md). Reading both before proposing a change is well worth the time.

## Reporting bugs

Open an issue using the **Bug report** template. Useful bug reports include:

- A clear description of the expected behavior and the actual behavior.
- A minimal repro: the smallest code or steps that triggers the bug.
- The Featly version, .NET version, OS, and storage provider.
- Relevant logs or stack traces.

Bugs that include a failing unit test in a draft PR are the fastest to get fixed.

## Reporting security vulnerabilities

Do **not** open public issues for security vulnerabilities. See [SECURITY.md](SECURITY.md) for the private disclosure process.

## Proposing features

Open an issue using the **Feature request** template. Describe:

- The problem you are trying to solve (not the solution).
- Who needs this and why.
- How it fits with the architectural principles in `ARCHITECTURE.md`.
- Any alternatives you considered.

If the feature touches the architectural surface (new top-level concept, new entity, new public interface, new deployment pattern), it requires an ADR before implementation. The maintainers will guide you through the ADR process.

## Pull requests

### Branching and commits

- Branch from `main`. Name branches `feat/<short-name>`, `fix/<short-name>`, `docs/<short-name>`, `refactor/<short-name>`.
- Use [Conventional Commits](https://www.conventionalcommits.org/) in commit messages. Example: `feat(sdk): add ambient EvaluationContext via HttpContext`.
- Keep commits focused. Squash unrelated changes into separate PRs.

### Code style

- C# 12+ idioms: `required` members, primary constructors, file-scoped namespaces.
- `<Nullable>enable</Nullable>` everywhere.
- `sealed` on classes by default.
- `ValueTask<T>` on hot paths (SDK evaluation). `Task<T>` elsewhere.
- No exceptions for control flow.
- Treat warnings as errors. `dotnet build` must succeed without warnings.

### Tests

- New code needs unit tests. xUnit + FluentAssertions + NSubstitute.
- Behavior changes need integration tests using `WebApplicationFactory<T>`.
- Engine changes need benchmark coverage in `tests/Featly.Engine.Benchmarks/`.
- All tests must pass on CI for the PR to be reviewed.

### Documentation

- Public APIs need XML documentation comments.
- User-facing changes need a `CHANGELOG.md` entry under `[Unreleased]`.
- New architectural decisions need an ADR.

### Review and merge

- All PRs need at least one maintainer approval.
- Maintainers may request changes; please respond to feedback.
- We use squash-merges to keep `main` history linear.

## Architectural Decision Records (ADRs)

ADRs document architectural decisions, why they were made, what alternatives were considered, and the consequences. Featly's ADRs live in `docs/adr/`.

To propose an ADR:

1. Copy `docs/adr/0000-template.md` to `docs/adr/NNNN-short-title.md` with the next sequential number.
2. Fill in Context, Decision, Alternatives Considered, Consequences. Set Status to `Proposed`.
3. Open a PR. Discussion happens in the PR thread.
4. When the PR merges, status becomes `Accepted`.

Existing ADRs are listed in `ARCHITECTURE.md §22`.

## Local development setup

```bash
git clone https://github.com/featly-net/featly.git
cd featly
dotnet restore
dotnet build
dotnet test
```

To run the embedded sample:

```bash
dotnet run --project samples/SelfHosted.Sample
```

Visit `http://localhost:5000/featly` to see the dashboard.

## Release process

Maintainers handle releases. The summary:

1. PRs land in `main`.
2. At a release point, `CHANGELOG.md`'s `[Unreleased]` becomes `[X.Y.Z] - YYYY-MM-DD`.
3. Tag `vX.Y.Z`.
4. CI builds and publishes NuGet packages.
5. GitHub Release notes link back to the changelog section.

## License

By contributing, you agree that your contributions will be licensed under the MIT license. See [LICENSE](LICENSE).

## Questions

If you are unsure about anything, open a discussion at `github.com/featly-net/featly/discussions` or ask in an issue. The maintainers prefer too many questions to too few.
