# ADR-0021: Testing library — stay on FluentAssertions 7.x, then migrate to AwesomeAssertions

- **Status:** Proposed
- **Date:** 2026-05-26
- **Deciders:** @thiagoluga
- **Supersedes:** _(none)_
- **Superseded by:** _(none)_

## Context

The Featly test suites use xUnit v3 + FluentAssertions + NSubstitute. FluentAssertions 7.x is the last Apache-2.0 release of the library; the 8.x line switched to a commercial license that requires a paid Xceed license for organizational use. Open-source maintainers retain the right to use 8.x non-commercially, but the license change introduces three concrete problems for Featly:

1. **Downstream consumers cannot freely consume our test infrastructure.** Anyone copying our test helpers (which use FluentAssertions assertions) into a commercial product inherits the licensing question.
2. **Security and CVE backports stop at 7.x.** Once Microsoft, .NET, or xUnit publishes a CVE in a transitive dependency that FluentAssertions pulls in, only 8.x will receive the fix.
3. **The compiler treats the .NET 10 / xUnit v3 combination as a forward target.** FluentAssertions 7.2.0 works today but will eventually require either upgrading to 8.x (license problem) or migrating to a fork.

The currently pinned version is `FluentAssertions 7.2.0`. The pin includes a comment explaining the constraint; this ADR formalizes the decision.

Two community alternatives have emerged:

- **AwesomeAssertions** — direct hard-fork of FluentAssertions 8.0.0-alpha released by Meir Blachman in February 2025. Apache-2.0, same API surface. Active development; xUnit v3 support landed in early 2026.
- **Shouldly** — independent library with a different idiom (`value.ShouldBe(x)` vs `value.Should().Be(x)`). MIT-licensed, longer track record, but the migration touches every assertion in the codebase.

## Decision

**Stay on `FluentAssertions 7.2.0` for the v0.0.x preview line. Plan a migration to `AwesomeAssertions` as a single mechanical PR before tagging v0.1.0.**

The reasoning:

- 7.2.0 is stable, works with .NET 10 + xUnit v3, and ships under Apache-2.0. We don't need to disrupt the test suite right now.
- AwesomeAssertions preserves the `value.Should().Be(x)` idiom. The migration is a global `using FluentAssertions;` → `using AwesomeAssertions;` plus a `<PackageVersion>` swap in `Directory.Packages.props`. No assertion rewrites.
- The migration moment is "before v0.1.0" so any external consumers contributing tests to Featly land on the new package without needing a transitional period.
- If AwesomeAssertions stalls or fragments before v0.1.0, the decision is reconsidered (probably in favor of Shouldly).

## Alternatives considered

### Alternative 1 — Upgrade to FluentAssertions 8.x

Buy the Xceed license and stay on the original library.

Rejected. Featly is open-source and aims to stay frictionless to adopt. Requiring contributors to acquire a commercial license to run the test suite is hostile. The cost is also recurring, which contradicts the project's "predictable" principle.

### Alternative 2 — Migrate to Shouldly now

Switch to Shouldly during the M5 / M6 window before the codebase grows further.

Rejected for the v0.0.x line because the migration touches every test file (every `Should().Be(...)` becomes `ShouldBe(...)`, every `Should().NotBeNull()` becomes `ShouldNotBeNull()`, etc). The diff is large, the value at v0.0.x is small, and Shouldly's idiom does not align with the rest of the .NET FluentAssertions-style ecosystem the project draws from. Kept as a fallback if AwesomeAssertions fragments.

### Alternative 3 — Drop FluentAssertions entirely; use xUnit's built-in `Assert`

Use only `Assert.Equal`, `Assert.True`, etc. No third-party assertion library.

Rejected. Readability of failure messages and chained assertions is a meaningful win — `result.Should().NotBeNull().And.Be(expected)` is clearer than three separate `Assert` calls. The cost of a license-clean assertion library is low.

### Alternative 4 — Pin 7.2.0 forever

Stay on 7.2.0 indefinitely; never migrate.

Rejected. CVE backports stop at the 7.x line. Long-term, the codebase needs a maintained Apache-2.0 / MIT path.

## Consequences

### Positive

- Test infrastructure stays Apache-2.0 throughout v0.0.x.
- Contributors do not need any commercial license.
- The eventual migration is a one-PR mechanical change, not a per-test rewrite.
- Forces a deliberate check-in on AwesomeAssertions maturity at the v0.1.0 boundary.

### Negative

- Locked into 7.2.0 until v0.1.0. Any 7.x CVEs cannot be fixed unless Xceed backports (unlikely).
- AwesomeAssertions is a young fork. Its long-term viability is not guaranteed; if it stalls, the migration has to pivot.
- Two assertion libraries to know about in project documentation until the migration lands.

### Neutral

- The migration is irreversible in practice (we won't migrate twice). The v0.1.0 boundary becomes the commitment point.

## Implementation notes

- Until v0.1.0: no action. The `Directory.Packages.props` comment already explains the pin.
- v0.1.0 migration:
  1. Replace `<PackageVersion Include="FluentAssertions" Version="7.2.0" />` with `<PackageVersion Include="AwesomeAssertions" Version="<latest>" />`.
  2. Global find/replace `using FluentAssertions;` → `using AwesomeAssertions;` across the test projects.
  3. Run the test suite. Any remaining failures are migration bugs in AwesomeAssertions, not in Featly.
  4. Update CLAUDE.md "Testing conventions" section.
- Tracking: STATUS.md "Open follow-ups" carries the migration item until v0.1.0.

## References

- [FluentAssertions licensing change announcement](https://xceed.com/products/unit-testing/fluent-assertions/)
- [AwesomeAssertions repository](https://github.com/AwesomeAssertions/AwesomeAssertions)
- [Shouldly](https://docs.shouldly.io/)
- ARCHITECTURE.md §1 — Architectural principles ("Predictable, not magical")
- CLAUDE.md — Testing conventions
