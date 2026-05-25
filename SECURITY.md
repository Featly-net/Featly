# Security policy

Thanks for helping keep Featly and its users safe.

## Supported versions

Featly is in **pre-release**. There are no published versions yet. Security fixes target the `main` branch and will be included in the next preview package.

When stable releases ship, this table will be updated to indicate which versions receive security fixes.

## Reporting a vulnerability

**Please do not open public issues for security vulnerabilities.**

Report privately via [GitHub Security Advisories](https://github.com/Featly-net/Featly/security/advisories/new). This opens a private channel between you and the maintainers and lets us coordinate a fix and disclosure.

When reporting, include:

- A clear description of the vulnerability and the affected component (SDK, Server, Dashboard, Storage provider, CLI, OpenFeature provider, etc.).
- A minimal proof of concept: the smallest code, configuration, or HTTP request that triggers the issue.
- The version (commit SHA, branch, or NuGet version) you tested against.
- The runtime (.NET version, OS) and storage provider in use.
- The impact you believe the issue has (information disclosure, privilege escalation, denial of service, etc.).
- Any suggested mitigation if you have one.

## What to expect

- **Acknowledgement** within 5 business days.
- **Triage and severity assessment** within 10 business days. We use a CVSS-style rubric (confidentiality, integrity, availability, scope).
- **Fix timeline** depends on severity. Critical issues are prioritized; lower-severity issues are scheduled into a regular release.
- **Coordinated disclosure**. We will agree a public disclosure date with you before publishing the advisory.
- **Credit**. We will credit you in the advisory and release notes unless you ask to remain anonymous.

## Scope

In scope:

- Code in this repository.
- Officially published NuGet packages under the `Featly.*` namespace.
- Default configurations and the embedded Dashboard.

Out of scope:

- Third-party integrations and unofficial forks.
- Vulnerabilities in dependencies that already have an upstream CVE — please report those upstream. We track dependency updates via Dependabot.
- Social engineering of maintainers or users.
- Findings from automated scanners without a demonstrated impact.

## Hardening guidance

If you are running Featly in production, please review the security section of `ARCHITECTURE.md` (currently `ARCHITECTURE.md` Section 20). Highlights:

- Use API keys scoped to a single environment, with `SdkRead` for client SDKs and `AdminWrite` only where mutation is needed.
- Configure a real authentication scheme for the Dashboard. Do not rely on the loopback or basic auth filters in production.
- Enable approval workflows on production environments.
- Set the `BootstrapAdminIdentifier` once, then rotate it out.
- Verify webhook signatures on the receiving side.

Thank you for contributing to Featly's security.
