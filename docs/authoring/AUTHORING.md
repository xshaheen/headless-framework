# Authoring Agent-Facing Docs

Rules for the two documentation **surfaces** an agent reads to use this framework:

- **Domain doc** — `docs/llms/<domain>.md`. Cross-domain orientation plus one section per package, loaded on demand.
- **Package README** — `src/Headless.<Package>/README.md`. Ships with the NuGet package, rendered on nuget.org.

Both surfaces share the same **package contract** (the per-package section defined below) and the same facts. They differ only in wrapper: the domain doc adds a domain-level map and cross-package reference; the README carries one package alone, with no frontmatter and no map.

Write for an agent that must **choose**, not just recall. An agent that knows *what* a method does but not *why* the framework offers it — or *when this option beats another* — picks wrong defaults. Every doc states the concept, the trade-off, and the provider decision, not just the signature. A section that reads like a method list is incomplete.

Templates:

- [TEMPLATE.md](TEMPLATE.md) — starting point for a domain doc.
- [PACKAGE-README-TEMPLATE.md](PACKAGE-README-TEMPLATE.md) — starting point for a package README.

---

## The package contract

The per-package section is the **single source of truth** shared by both surfaces. It appears as an H2 (`## Headless.<Package>`) inside a domain doc and as the H1 body of a README. Same sub-sections, same order, same facts; only the heading depth differs.

Lead sentence: one line — what the package is and who uses it.

Sub-sections, in order:

| Sub-section | Content | When empty |
| --- | --- | --- |
| Problem Solved | What the consumer would otherwise build or stitch together | Always present |
| Key Features | Concrete capabilities, one per bullet | Always present |
| Design Notes | *(optional)* A non-obvious choice the agent must know: a default's rationale, an ordering or threading guarantee, why a dependency exists | Omit the heading entirely — never write `None.` |
| Installation | `dotnet add package Headless.<Package>` — version-free | Always present |
| Quick Start | Minimal setup that compiles against the current public API: the registration call and one representative use | Always present |
| Configuration | Options block or table | Write `None.` |
| Dependencies | Direct `Headless.*` and third-party packages (transitive framework packages may be omitted) | Write `None.` |
| Side Effects | DI registrations and their lifetimes, hosted/background services, filesystem/network/process effects | Write `None.` |

Rules for the contract, both surfaces:

- **Every required sub-section is present.** Write `None.` for an empty Configuration, Dependencies, or Side Effects — never drop the heading. Design Notes is the one sub-section you omit when it does not apply.
- **Design Notes earns its place.** Include it only when a conventional reading of the API would lead the agent wrong. Skip it for ordinary packages.
- **Code samples compile** against the package's current public API. No pseudo-code, no `...` inside `using` statements.
- **Headings carry no** emojis, version numbers, or dates — they break anchors and go stale.
- **Prose states facts.** No marketing adjectives (`blazing fast`, `enterprise-grade`, `robust`, `seamless`), no hedging (`should probably`, `might`), no unexplained jargon.

---

## Domain doc

A domain doc wraps the package contracts for one feature family with a shared map. Structure follows what the agent needs first, not a fixed template.

- **Frontmatter is required:**

  ```yaml
  ---
  domain: <Human-readable domain name>
  packages: <comma-separated suffixes, e.g. Caching.Abstractions, Caching.Redis>
  ---
  ```

- **One H1**, matching the domain name. Every package is an H2; its sub-sections are H3. A single-package domain still uses H2 — never promote a package to H1.
- **Lead with the map, then the rules, then the packages.** The reliable order:
    1. `# <Domain>` then a one-line blockquote summary.
    2. `## Orientation` — what the domain solves, the entry-point interfaces, and when to pick which provider. This is the map an agent reads first.
    3. `## Agent Rules` — the highest-leverage section: do/don't bullets, footguns, and banned alternatives. This is what agents act on.
    4. `## Core Concepts` *(optional)* — the vocabulary and mental model, when the domain needs one before an agent can choose.
    5. `## Choosing a Provider` *(required when the domain ships 2+ providers)* — a decision table with `use when` / `avoid when` / `trade-off`.
    6. Other cross-cutting reference (e.g. `## Provider Capabilities`), then the per-package H2 sections separated by `---`.
- **Package order:** Abstractions, then Core, then providers alphabetically.
- **Structure follows the information hierarchy.** Add a compact in-file Table of Contents only when a doc is long enough that an agent needs it to jump between packages; keep short docs flat. A ToC, when present, mirrors the headings exactly. There is no fixed section list to reproduce verbatim — omit a section the domain does not need.
- **Cross-links** use relative paths within `docs/llms/`.
- **Install commands are version-free** — versions live in `Directory.Packages.props`.

### index.md

`docs/llms/index.md` is the cross-domain hub. Keep it in sync with the domain set:

- The **Domain documentation** list carries one line per domain doc.
- The **Packages** catalog groups every package.
- The **End-to-End Example** compiles against the current public API of the packages it threads; update it when a registration entry point or abstraction signature it uses changes.

---

## Package README

A README carries one package contract alone. It is the `## Headless.<Package>` section of the domain doc, lifted out and promoted:

- **One `README.md`** per `src/Headless.<Package>/` directory. H1 is `# Headless.<Package>` verbatim, no tagline.
- **No frontmatter, no ToC, no map.** The domain-level Orientation, Agent Rules, and cross-cutting reference stay in the domain doc so they appear once, not once per package.
- **Sub-sections are H2** (the contract's H3 promoted one level), same order, same facts as the matching domain-doc section.
- **Cross-links to other packages use the package name in backticks** (`` `Headless.Caching.Abstractions` ``), not relative paths — nuget.org breaks relative links.

---

## Keeping docs in sync with code

The two surfaces **mirror** each other, and both mirror the code. A change to one requires the same change to the other in the same commit.

### When a code change requires a docs change

Update both surfaces when a change to `src/Headless.<Package>/` does any of:

- Changes public API surface — a `public` type or method added, removed, or renamed.
- Changes consumer-visible behavior — a default, side effect, ordering guarantee, retry or cancellation semantics, or threading rule.
- Adds or removes a configuration option.
- Adds or removes a direct dependency (update Dependencies) or a DI registration (update Side Effects).
- Adds, renames, or removes a package (also update `index.md`).

No docs change is required for an internal refactor, a `private`/`internal`-only change, a perf-only change with no behavior difference, or a test/formatting/header change.

### Drift check before committing a package change

Run this before committing any `Headless.*` change that meets a trigger above. Each item has a checkable bound:

1. **README ↔ code:** every type and method named in a README code sample still exists; `Dependencies` matches the direct `<PackageReference>` entries; `Side Effects` matches what `Setup<Provider>.Add<Feature>(...)` registers.
2. **Domain doc ↔ code:** re-read the matching `docs/llms/<domain>.md`; no sub-section still describes the old behavior.
3. **grep the changed name** — type, method, or option — across `docs/llms/` and fix every match.
4. **Domain doc ↔ README:** the `## Headless.<Package>` section and the README state the same facts. Fix whichever is wrong, or both.
5. **Package added, renamed, or removed:** update the `index.md` link list and catalog.

If a behavior change is hard to describe accurately, flag it in the PR rather than committing stale docs.

---

## Scoped reference

Consult these only when the trigger applies.

### Provider SDK types in options

*When documenting a provider options class whose property type comes from the backend SDK* (`AWSSDK.S3`, `Azure.Storage.Blobs`, `Azure.Core`, `MailKit`, `SixLabors.ImageSharp`, `SSH.NET`, `StackExchange.Redis`, …).

Exposing an SDK type directly is deliberate and allowed — do not reflexively abstract it away. Decide by fidelity:

- **Full-fidelity pass-throughs are the intended shape.** When the SDK type carries a large or open-ended surface a Headless wrapper could only re-expose lossily — `AWSOptions`, a `BlobServiceClient` factory, `IImageEncoder`, `IConnectionMultiplexer`, `ConfigurationOptions`, `TokenCredential`, MailKit socket enums, `S3CannedACL`, `PublicAccessType`, `ProxyTypes` — pass it through verbatim. The consumer needs the SDK on their reference graph to construct the value anyway; accept the coupling openly.
- **Low-fidelity, trivially abstractable enums should be wrapped** in a Headless type: a small, closed, stable enum with an obvious one-to-one equivalent does not justify pulling the SDK into the option's type just to name three values.

Every SDK pass-through property carries an XML `<remarks>` stating it is a deliberate full-fidelity pass-through of the named SDK type that intentionally couples the option to that package. This makes the choice auditable and warns the consumer their reference graph now includes the SDK. See `AwsBlobStorageOptions.CannedAcl`, `AzureStorageOptions.ContainerPublicAccessType`, `SshBlobStorageOptions.ProxyType`, `ImageSharpOptions.*CompressEncoder`, `MailkitSmtpOptions.SocketOptions`, `AzureCommunicationEmailOptions.TokenCredential`, and `MessagingRedisOptions`/`RedisPubSubOptions.Configuration` for the established shape.

### Observability and operations safety

*When documenting metrics, dashboards, or operations UI.*

- Metric examples use bounded dimensions. Never label a metric with message, replay, operation, payload, header, or free-form tenant values. A tenant label requires a documented default-off cardinality opt-in.
- Operations UI examples identify the authorization boundary and use safe lifecycle projections. Payloads and raw headers do not belong in inbox generation, retention, replay, or recovery views.
- Reliability claims distinguish atomic commit of enlisted state from handler entry and effects outside that transaction. Neither direct transport nor external effects are exactly once.
