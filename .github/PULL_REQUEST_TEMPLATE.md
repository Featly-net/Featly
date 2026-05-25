<!--
Thanks for contributing to Featly! Please fill in the sections below.
If something does not apply, write "n/a" rather than deleting the section,
so reviewers know it was considered.

Before submitting, please read CONTRIBUTING.md if you have not already.
-->

## Summary

<!-- One or two sentences: what does this PR do and why. -->

## Related issue / milestone

<!--
Link the issue this PR closes (e.g. "Closes #123") or the milestone it is
part of (see PLAN.md). For pure docs or repo-infra PRs, write "n/a".
-->

Closes #

## Type of change

<!-- Check all that apply. -->

- [ ] `feat` — new feature
- [ ] `fix` — bug fix
- [ ] `docs` — documentation only
- [ ] `refactor` — code change that neither fixes a bug nor adds a feature
- [ ] `perf` — performance improvement
- [ ] `test` — adding or updating tests
- [ ] `chore` — tooling, CI, repo housekeeping
- [ ] `breaking` — breaking change to a public API

## Architectural impact

<!--
Does this PR introduce a new top-level concept, a new public interface,
a new entity, a new deployment pattern, or change an existing one?
If yes, link the ADR. If no ADR exists yet, explain why this PR does not
require one or open a draft ADR before continuing.
-->

- [ ] No architectural impact
- [ ] ADR added or updated: `docs/adr/NNNN-...md`
- [ ] ADR not yet needed because: <!-- explain -->

## Checklist

- [ ] Branch name follows `feat/`, `fix/`, `docs/`, `refactor/`, `chore/` convention
- [ ] Commits follow [Conventional Commits](https://www.conventionalcommits.org/)
- [ ] `dotnet build` passes with no warnings (when code exists)
- [ ] `dotnet test` passes locally (when tests exist)
- [ ] New code has unit tests (xUnit + FluentAssertions + NSubstitute)
- [ ] Public APIs have XML doc comments
- [ ] User-facing changes have a `CHANGELOG.md` entry under `[Unreleased]`
- [ ] No secrets, credentials, or personal data committed
- [ ] No emojis in code, comments, or documentation (per CLAUDE.md)

## Notes for the reviewer

<!--
Anything the reviewer should pay particular attention to: tricky logic,
trade-offs you considered, follow-up work intentionally left out, etc.
-->
