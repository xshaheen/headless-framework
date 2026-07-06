# Documentation Authoring Rules

Rules for writing and maintaining the two agent-facing documentation surfaces:

- `docs/llms/*.md` — cross-domain orientation + per-package sections (loaded by agents on demand).
- `src/Headless.<Package>/README.md` — per-package README shipped with the NuGet package (rendered on nuget.org).

Both must be **deterministic**: predictable structure, stable anchors, no per-author drift. They share the same facts and the same per-package sub-section shape; the only differences are heading depth and that the README has no domain-level wrapper.

**Not pure API reference.** An agent that knows *what* a method does but not *why* the framework offers it — or *when to pick this option over another* — will choose wrong defaults. Every domain doc must explain:

- **Core concepts**: the vocabulary and mental model the agent needs before picking a package or option.
- **Trade-offs**: why a default value, ordering guarantee, threading model, or dependency was chosen — especially when an obvious alternative exists.
- **Decisions between providers**: when 2+ providers exist, give the agent a decision table with explicit *use when* / *avoid when* / *trade-off* columns.

API surface (signatures, options, side effects) is necessary but not sufficient. If a section reads like a method list, it is incomplete.

Templates:

- [TEMPLATE.md](TEMPLATE.md) — canonical shape for `docs/llms/<domain>.md`.
- [PACKAGE-README-TEMPLATE.md](PACKAGE-README-TEMPLATE.md) — canonical shape for `src/Headless.<Package>/README.md`.

---

## Domain docs (`docs/llms/<domain>.md`)

### Structural invariants

- **One file per domain**, kebab-case filename (e.g., `caching.md`, `multi-tenancy.md`). One H1 per file matching the domain name.
- **YAML frontmatter is required**:

  ```yaml
  ---
  domain: <Human-readable domain name>
  packages: <comma-separated package suffixes, e.g., Caching.Abstractions, Caching.InMemory>
  ---
  ```

- **Top-level section order is fixed — do not reorder or rename**:
    1. `# <Domain>` (H1)
    2. `## Table of Contents` (4-space indent for nested items)
    3. One-line blockquote summary (`> ...`)
    4. `## Quick Orientation`
    5. `## Agent Instructions` (bulleted rules)
    6. `## Core Concepts` *(optional, fixed position when present)*
    7. `## Choosing a Provider` *(optional, fixed position when present — required if the domain ships 2+ providers)*
    8. Other optional cross-cutting H2 sections (e.g., `## Provider Capabilities`) — before per-package sections
    9. Per-package sections — H2 `## Headless.<Package>`, separated by `---`
- **Per-package required H3 sub-sections, in order**: `Problem Solved`, `Key Features`, `Installation`, `Quick Start`, `Configuration`, `Dependencies`, `Side Effects`. Write `None.` when truly empty — never omit the heading.
- **Per-package optional H3 sub-section**: `Design Notes`, placed between `Key Features` and `Installation`. Include when the package makes a non-obvious choice that affects how the agent must use it (default rationale, ordering guarantees, threading model, why a dependency exists). Skip entirely for conventional packages — do **not** write `None.`.
- **Naming**: `Quick Start` (not `Usage`, `Minimal Setup`, `Getting Started`). `Configuration` (not `Options`). `Side Effects` (not `Effects`, `Registrations`).
- **Package order**: Abstractions first, then Core, then providers alphabetically.
- **Exactly one H1 — package headings are H2.** The domain title is the file's only `#` heading. Every package section is H2 (`## Headless.<Package>`) and its sub-sections are H3 — never promote a package to its own H1. Multiple H1s per file break the single-H1 invariant and corrupt the generated ToC anchors. This holds for single-package domains too (e.g., `logging.md`, `identity.md`, `mediator.md`): the one package is still H2, not H1.
- **Banned in headings**: emojis (break anchors and chunking), version numbers, dates.
- **Banned in prose**: marketing adjectives (`blazing fast`, `enterprise-grade`, `robust`, `seamless`), unexplained jargon, hedging (`should probably`, `might`).
- **Install commands are version-free**: `dotnet add package Headless.<Name>` only — versions live in `Directory.Packages.props`.
- **Cross-links** use relative paths within `docs/llms/`.
- **`docs/llms/index.md`** is the cross-domain hub. Keep its **Domain documentation** list and **Packages** catalog in sync when domains or packages change, and keep the **End-to-End Example** compiling against the current public API of the packages it threads (update it when a registration entry point or abstraction signature it uses changes).

### Workflow: writing a new doc

1. Copy [TEMPLATE.md](TEMPLATE.md) to `docs/llms/<domain>.md`.
2. Fill frontmatter, `Quick Orientation`, `Agent Instructions`, then each per-package section in order.
3. Add the file to `docs/llms/index.md` in both places — the **Domain documentation** list (one-line summary) and the **Packages** catalog (grouped section).
4. Regenerate the Table of Contents so every H2/H3 has a matching entry and every anchor resolves.
5. Self-check against the invariants above before committing.

### Workflow: updating an existing doc

- **Preserve section order and names.** Add new H3s only inside the right H2; never invent new top-level sections without first updating [TEMPLATE.md](TEMPLATE.md) and these rules.
- **Append, don't rewrite.** If a restructure is genuinely needed, do it in a separate commit so the diff is reviewable.
- **Regenerate the ToC** when adding or removing sections so anchors stay accurate.
- **Highest-leverage edit:** update `## Agent Instructions` when you discover a footgun the existing rules don't cover. That section is what agents actually act on.
- **Do not duplicate** content across `Quick Orientation` and per-package sections; orientation is the map, package sections are the territory.

### Workflow: keeping docs in sync with code

A code change in `src/Headless.*` **requires** a `docs/llms/` update when any of these are true:

- Public API surface changes — new, removed, or renamed `public` type or method.
- New package, package rename, or package removal — update both the per-domain file and `index.md`.
- Behavior visible to consumers changes — default values, side effects, ordering guarantees, retry semantics, cancellation behavior, threading rules.
- New or removed configuration option.

A code change does **not** require a doc update for:

- Internal refactors and `internal`/`private`-only changes.
- Performance improvements with no API or behavior change.
- Test-only changes.
- Comment, formatting, or copyright-header changes.

**Drift check before committing a change to a `Headless.*` package**:

1. Re-read the matching `docs/llms/<domain>.md`. Does any sub-section still describe the old behavior?
2. `grep` for the changed type, method, or option name across `docs/llms/` and fix every match.
3. If a package was removed or renamed, search `docs/llms/index.md` and update both the link list and the catalog.
4. Update the matching `src/Headless.<Package>/README.md` so it mirrors the new `## Headless.<Package>` section content (see Package READMEs below).
5. If the change is ambiguous (e.g., behavior change that's hard to describe), flag it in the PR description rather than leaving stale docs silently.

---

## Package READMEs (`src/Headless.*/README.md`)

Every package ships a `README.md` to nuget.org. Each README is the **per-package surface** that mirrors the matching `## Headless.<Package>` section inside `docs/llms/<domain>.md`. Same sub-section order, same content, same facts — the only differences are heading depth and that the README has no domain-level wrapper (no frontmatter, no ToC, no `Quick Orientation`, no `Agent Instructions`).

### Structural invariants

- **Exactly one `README.md`** per `src/Headless.<Package>/` directory.
- **H1 is the package name** verbatim: `# Headless.<Package>`. No tagline in the heading.
- **Required sub-sections, in order, all H2**: `Problem Solved`, `Key Features`, `Installation`, `Quick Start`, `Configuration`, `Dependencies`, `Side Effects`. Write `None.` when truly empty — never omit the heading.
- **Optional sub-section**: `Design Notes`, H2, placed between `Key Features` and `Installation`. Include when the package makes a non-obvious choice the agent must understand. Skip entirely for conventional packages — do **not** write `None.`. Content must mirror the matching `### Design Notes` in `docs/llms/<domain>.md`.
- **No frontmatter, no Table of Contents** — READMEs are short enough to skim. ToC belongs in `docs/llms/<domain>.md`, not here.
- **Banned in headings**: emojis, version numbers, dates. Same rule as domain docs.
- **Banned in prose**: marketing adjectives, unexplained jargon, hedging.
- **Install command is version-free**: `dotnet add package Headless.<Name>` only.
- **Code samples must compile** against the package's current public API. No pseudo-code, no `...` placeholders in `using` statements.
- **Cross-links to other Headless packages**: use the package name in backticks (`` `Headless.Caching.Abstractions` ``), not relative file paths — the README is rendered on nuget.org where relative links break.

### Workflow: writing a new package README

1. Copy [PACKAGE-README-TEMPLATE.md](PACKAGE-README-TEMPLATE.md) to `src/Headless.<Package>/README.md`.
2. Fill every section. Write `None.` for `Dependencies` or `Side Effects` only when genuinely empty.
3. Add or update the matching `## Headless.<Package>` section in `docs/llms/<domain>.md` so the two stay aligned. If the package belongs to a new domain, also create the domain doc.
4. Add the package to `docs/llms/index.md` **Packages** catalog under the right group.

### Workflow: updating a package README

- **Preserve sub-section order and names.** Add new H3s only inside the right H2; never invent new top-level sections without first updating [PACKAGE-README-TEMPLATE.md](PACKAGE-README-TEMPLATE.md) and these rules.
- **Mirror the edit in `docs/llms/<domain>.md`** within the same commit. The two files must not disagree on facts.
- **Do not paste long architecture explanations** into the README; those belong in `docs/llms/<domain>.md` cross-cutting sections so they show up once, not 28 times.

### Workflow: keeping READMEs in sync with code

A code change in `src/Headless.<Package>/` **requires** a `README.md` update when any of these are true:

- Public API surface changes (new, removed, or renamed `public` type or method).
- Behavior visible to consumers changes (default values, side effects, ordering guarantees, retry semantics, cancellation behavior, threading rules).
- New or removed configuration option.
- A new dependency is added or removed (update the `Dependencies` section).
- DI registrations change (update the `Side Effects` section).

A code change does **not** require a README update for the same exclusions listed under domain docs (internal refactors, perf-only, test-only, formatting).

**Drift check before committing a change to a `Headless.*` package**:

1. Diff `src/Headless.<Package>/README.md` against the package's actual public API — every type and method named in code samples must still exist.
2. Verify `Dependencies` matches `<PackageReference>` entries (transitively-pulled framework packages can be omitted; direct dependencies must be listed).
3. Verify `Side Effects` matches what `Setup<Provider>.Add<Feature>(...)` actually registers.
4. Confirm the matching `## Headless.<Package>` section in `docs/llms/<domain>.md` says the same things — fix whichever side is wrong, or both.

## Provider SDK types in options — policy

A provider options class may expose a property whose type comes straight from the backend SDK (`AWSSDK.S3`, `Azure.Storage.Blobs`, `Azure.Core`, `MailKit`, `SixLabors.ImageSharp`, `SSH.NET`, `StackExchange.Redis`, …). This is **deliberate and allowed** — do not "abstract it away" reflexively. Decide by fidelity:

- **Full-fidelity pass-throughs are the intended shape.** When the SDK type carries a large or open-ended surface that a Headless wrapper could only re-expose lossily — `AWSOptions`, a `BlobServiceClient` factory, `IImageEncoder`, `IConnectionMultiplexer`, `ConfigurationOptions`, `TokenCredential`, MailKit's socket enums, `S3CannedACL`, `PublicAccessType`, `ProxyTypes` — pass the SDK type through verbatim so no backend capability is lost. Wrapping it would trade fidelity for a false sense of decoupling: the consumer still needs the SDK on their reference graph to construct the value. Accept the coupling openly instead.
- **Low-fidelity, trivially-abstractable enums should be wrapped** in a Headless type when practical: a small, closed, stable enum with an obvious one-to-one Headless equivalent does not justify pulling the SDK into the option's type just to name three values.

**Every SDK pass-through option property must carry an XML `<remarks>` noting the coupling** — one sentence stating it is a deliberate full-fidelity pass-through of the named SDK type and that it intentionally couples the option to that package. This makes the decision auditable (a reviewer sees it was a choice, not an oversight) and warns the consumer their reference graph now includes the SDK. See `AwsBlobStorageOptions.CannedAcl`, `AzureStorageOptions.ContainerPublicAccessType`, `SshBlobStorageOptions.ProxyType`, `ImageSharpOptions.*CompressEncoder`, `MailkitSmtpOptions.SocketOptions`, `AzureCommunicationEmailOptions.TokenCredential`, and `MessagingRedisOptions`/`RedisPubSubOptions.Configuration` for the established shape.
