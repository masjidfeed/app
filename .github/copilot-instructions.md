# Masjid App Project Guidelines

Masjid App is a multi-tenant .NET MAUI app backed by the WordPress Masjid API.

## Sources of Truth

- [docs/REQUIREMENTS.md](../docs/REQUIREMENTS.md) is authoritative for product behavior and constraints. Preserve its stable requirement IDs and RFC 2119 meanings; cite relevant IDs when implementing requirements.
- [docs/DESIGN.md](../docs/DESIGN.md) describes the current architecture, feature behavior, and verified gaps. Read the relevant section before changing a feature and update it when the implementation changes those facts.
- [docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md) owns environment and developer setup instructions.
- If documentation conflicts, requirements win. Confirm current behavior in code and tests rather than treating the design document as aspirational work.

## Engineering Rules

- Follow the boundaries documented in `DESIGN.md`: keep domain logic in `MasjidApp.Core`, platform integration and UI in `MasjidApp`, and WordPress behavior in the backend plugin.
- Use MVVM and dependency injection. Keep code-behind thin and put behavior in ViewModels or injectable services.
- Keep prayer calculations, timezone/DST handling, RRULE expansion, sanitization, and other domain logic platform-neutral and unit-tested (`QA-02`, `QA-06`).
- Route JSON API access through the typed resilient client and durable SQLite cache. Preserve HTTPS validation, timeouts, retries, conditional metadata, offline fallback, and tolerant parsing (`API-07`–`API-11`, `NFR-OF-03`).
- Preserve multi-masjid isolation. Branding, URLs, and feature flags come from the active masjid configuration (`API-06`, `API-10`).
- Native widgets read only app-written shared cached data; they do not call the network (`TC-04`).

## Non-Negotiable Constraints

- Never store or process payment/card data. Open only a configured HTTPS donation page and hide donations when none is valid (`FR-DN-01`–`FR-DN-04`, `NFR-SC-03`).
- Never hard-code secrets or API keys; use platform secure storage (`NFR-SC-01`).
- Sanitize remote rich content before rendering (`NFR-SC-02`).
- Compute times in the SalahAPI IANA timezone, not the device timezone, and honor DST and Ramadan overrides (`FR-PT-06`, `FR-PT-08`).
- Treat malformed or partial remote data as a recoverable cached or empty state (`API-07`, `API-09`, `NFR-RL-01`).
- Request permissions with a rationale and degrade gracefully when denied (`FR-CM-03`, `FR-PN-05`).

## Product Conventions

- Put user-facing text in RTL-aware localization resources (`TC-05`).
- Preserve light/dark/system themes, 12/24-hour formatting, dynamic type, screen-reader labels, and sufficient contrast (`FR-ST-03`, `FR-WG-06`, `NFR-AC-01`).
- Keep environment base URLs configurable without code changes (`DV-03`).
- Use iCal RRULE strings for recurring events (`FR-EV-07`).
- Do not add Apple Watch, in-app Stripe, chat, or web/desktop clients unless explicitly requested; they are outside v1 scope.

## Pull Requests

- Follow [.github/PULL_REQUEST_TEMPLATE.md](PULL_REQUEST_TEMPLATE.md) when opening a pull request. Read the template first and use its current content as the PR body structure.
- Keep every template heading, in order, and replace each HTML comment placeholder with real content. Do not drop, rename, or reorder sections; write `N/A` when a section genuinely does not apply.
- Under `## Why`, link the issue the PR closes and cite the relevant requirement IDs.
- Report validation results (tests and warnings-as-errors build) in the PR body, including any check that could not be run and why.

## Definition of Done

1. Add or update tests for modified core logic; include reference-value tests for prayer calculations where applicable (`QA-02`, `QA-06`).
2. Run the focused tests first, then the full `dotnet test` suite.
3. Run a warnings-as-errors `dotnet build` (`QA-01`).
4. For cross-platform UI or integration changes, prefer Mac Catalyst for the fastest local validation. Use Android/iOS validation when the change is platform-specific or depends on platform-only behavior, and validate any required local backend behavior (`DV-01`, `DV-02`).
5. Fix failures introduced by the change and report every validation result. If a change cannot be automated, state why and describe the manual check.
6. Keep public API/module documentation and `DESIGN.md` current when behavior, architecture, or known gaps change (`QA-05`).
