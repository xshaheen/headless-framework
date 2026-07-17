---
title: Security and Supply Chain Remediation - Plan
type: fix
date: 2026-07-17
deepened: 2026-07-17
artifact_contract: x-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: x-plan-bootstrap
execution: code
---

# Security and Supply Chain Remediation - Plan

## Goal Capsule

- **Objective:** Remediate the confirmed application-security, resource-abuse, and supply-chain findings from the 16-07-2026 repository audit while preserving the repository's existing strong dependency and release controls.
- **Authority:** The user's follow-up directs that every repository-remediable finding be fixed in a new PR except mediator request/response payload logging; current `origin/main`, `CLAUDE.md`, and the audit evidence govern implementation details.
- **Execution profile:** One reviewable PR with separate commits for dashboard trust boundaries, Jobs and Messaging resource limits, integration endpoint hardening, and supply-chain/release controls.
- **Stop conditions:** Do not change mediator payload logging, weaken authentication or release environments, replace existing NuGet advisory enforcement with a slower duplicate gate, or mutate external GitHub repository settings from this PR.
- **Tail ownership:** Implementation includes focused tests, mirrored documentation, formatting and analyzer gates, branch publication, PR creation, CI disposition, and the post-publication GitHub settings report required by R23.

---

## Product Contract

### Summary

The framework will fail closed when selecting dashboard peers, bound work and payloads before allocating scarce resources, reject unsafe remote credential endpoints, keep authentication tokens out of query strings, and make restore and release inputs deterministic. Existing secure controls on current `main` remain intact.

### Problem Frame

The audit found two exploitable dashboard target-selection paths, dedicated-thread creation without admission, unbounded decompression and several backlog/queue materialization paths, configuration values capable of extreme allocation, and public dashboard routes that permit unbounded reads. It also found deterministic-restore, dependency-update coverage, container mutability, package-verification, and duplicate-publication gaps.

Current `origin/main` has moved since the audit. It already makes package publication release-only, isolates npm lifecycle scripts from NuGet credentials, enforces NuGet audit warnings through the pinned Headless SDK, and bounds Jobs post-commit drain work. Those areas require preservation and regression verification rather than replacement.

### Requirements

#### Dashboard trust boundaries

- R1. Messaging dashboard ping requires the configured dashboard authorization mode and accepts only an exact discovered HTTP(S) origin with no userinfo, path, query, or fragment.
- R2. Ping requests use a redirect-disabled, short-timeout client, construct the health URI structurally, and propagate request cancellation.
- R3. Kubernetes direct node resolution applies the same visibility-label and port-selection rules as node listing, defaults only to the configured namespace, and fails closed for invalid or stale cookies.
- R4. The dashboard stores discovery node names rather than client-composed endpoint strings, while preserving cross-node authorization forwarding only to discovery-authorized peers.

#### Credentials and authentication tokens

- R5. Captcha and Paymob credential-bearing endpoints require HTTPS, except loopback HTTP used by local development and tests; userinfo is rejected.
- R6. JWT dashboard demos bootstrap login through URL fragments that are consumed only in Host-auth login mode and removed immediately; query-token auto-login is removed.

#### Jobs and Messaging resource safety

- R7. `LongRunning` Jobs retain dedicated-thread semantics but wait for a configurable bounded admission permit before a thread is created.
- R8. Jobs GZip decompression enforces a configurable maximum expanded size without changing the persisted sentinel format.
- R9. In-memory and EF CAS fallback job claims deterministically materialize no more than the existing native-provider claim batch of 100 roots per sweep.
- R10. Scheduled messaging retains at most `SchedulerBatchSize` near-due messages in memory and returns overflow to durable delayed state rather than dropping or stranding it.
- R11. Messaging thread, buffer, scheduler, and retry batch settings fail startup validation when non-positive, excessive, overflowing, or above the documented allocation budget.
- R12. Feature, permission, and setting initialization retries use capped jitter and stop retrying cancellation and known permanent caller/configuration failures.
- R13. Jobs Dashboard accepts only page sizes 1 through 100, caps request bodies at 1 MiB and batch mutations at 500 IDs, streams JSON deserialization, and removes the unused unbounded all-record routes.
- R14. Blob-backed Data Protection key loading bounds individual XML documents, key count, and aggregate XML bytes, prohibits DTD processing, and fails the load rather than returning a partial key ring.

#### Supply-chain and release integrity

- R15. Repository restore and audit sources are cleared and declared locally; `Headless.*` continues to resolve from GitHub Packages through the existing most-specific mapping.
- R16. Dependabot covers the Tus frontend and every maintained Docker/Compose directory, and the Tus frontend build is exercised when its lockfile changes.
- R17. Mutable application/test container references are pinned to reviewed digests and remain updateable through Dependabot where supported; architecture-specific digests require an explicit CI matrix rather than a mutable tag fallback.
- R18. CI verifies the complete expected package set against a committed package-ID manifest, plus archive integrity, identity/version uniqueness, repository commit metadata, and embedded SPDX SBOMs before publication.
- R19. Package publication preflights the complete expected identity/version set and aborts before the first push when any version already exists; push-time races still fail loudly and require explicit maintainer reconciliation.
- R20. Release package artifacts receive GitHub build-provenance attestations using short-lived OIDC permissions, without introducing a long-lived signing credential.

#### Preservation and exclusions

- R21. Existing locked restore, SDK-enforced NuGet auditing, SHA-pinned Actions, read-only workflow defaults, OIDC NuGet publishing, protected release environments, and npm-before-token separation remain enforced.
- R22. Mediator request/response payload logging is unchanged.
- R23. External GitHub settings recommendations remain outside the PR and are reported separately after publication.

### Acceptance Examples

- AE1. Given a discovered peer `allowed:8080`, when ping receives `http://allowed:8080@evil.test`, a host suffix, altered port, path, query, fragment, or redirect, then no attacker-controlled endpoint is requested.
- AE2. Given Kubernetes show-only mode, when a cookie names an unlabelled or hidden service, then direct resolution returns no node and clears the stale selection; a visible service uses the same selected port as the node list.
- AE3. Given five simultaneous long-running submissions and a limit of two, then only two dedicated threads start and cancellation or completion admits the next waiter.
- AE4. Given compressed input that expands beyond its configured limit, then decompression stops and fails before materializing the complete output.
- AE5. Given 101 eligible fallback jobs, then one sweep claims the oldest 100 roots and the next sweep claims the remainder.
- AE6. Given a full scheduled-message acceleration queue, then the next durable message returns to delayed state and remains recoverable by the normal scheduler sweep.
- AE7. Given an oversized dashboard body, invalid page size, or batch of 501 IDs, then the endpoint rejects it without buffering or querying the unbounded set.
- AE8. Given machine-level NuGet sources, then repository restore ignores them and uses only the declared package and advisory sources.
- AE9. Given a package artifact with a missing SBOM, duplicate package identity, wrong repository commit, or incomplete expected set, then publication does not start.
- AE10. Given an already-published package version, then the release fails visibly rather than reporting success through `--skip-duplicate`.

### Scope Boundaries

#### In Scope

- Every repository-remediable finding reported by the security audit except mediator payload logging.
- Conservative public options needed to make limits explicit and configurable.
- Breaking security corrections to unused dashboard routes, remote plaintext endpoints, stale node cookies, old query-token demo links, and duplicate publication semantics.
- Documentation directly affected by changed public behavior or operations.

#### Out of Scope

- Mediator payload logging and its debug/development policy.
- Package certificate signing, which requires certificate custody and rotation design; GitHub provenance attestation is the credential-free improvement in this PR.
- Dedicated peer-to-peer dashboard credentials; discovery authorization remains the target trust boundary.
- GitHub administrator enforcement, tag rulesets, secret-validity settings, and Action publisher allowlists because they are external repository settings.
- Jobs commit-drain changes: current `main` already implements cooperative and hard time bounds with late-fault observation.

---

## Planning Contract

### Key Technical Decisions

- KTD1. **session-settled: user-directed — leave mediator payload logging unchanged.** The user classified these logs as debug/development-only. Rejected alternative: introduce payload redaction or metadata-only logging in this PR. Reason: it is explicitly outside the requested remediation scope.
- KTD2. **session-settled: user-directed — ship one new PR.** Keep security domains as separate commits and implementation units inside one PR. Rejected alternative: split the remediation into several PRs. Reason: the user asked to fix the remaining findings into a new PR.
- KTD3. **Discovery membership is the dashboard network trust boundary.** Exact parsed origins secure ping; Kubernetes direct lookup must reuse listing visibility and port logic. Private addresses remain valid because peer dashboards are expected to be internal.
- KTD4. **Preserve cross-node Host authentication.** Continue forwarding `Authorization` only after the target is resolved as a discovery-authorized peer. Rejected alternative: strip it universally, which breaks authenticated dashboard federation without providing replacement credentials.
- KTD5. **Bound dedicated work before allocation.** Long-running Jobs wait on a separate configurable semaphore before creating OS threads; ordinary scheduler priority and worker behavior remain unchanged.
- KTD6. **Use existing semantic capacities when available.** Fallback claims use the existing native batch of 100, and scheduled messaging uses `SchedulerBatchSize`; new public knobs are added only for long-running concurrency and decompression size.
- KTD7. **Fail configuration early.** Messaging uses explicit numeric ceilings and checked capacity multiplication; invalid settings fail startup instead of relying on downstream allocation exceptions.
- KTD8. **Remove unused unbounded HTTP routes.** The SPA already uses paginated siblings. Rejected alternative: silently cap endpoints documented as returning all records, which would preserve the URL while violating its contract.
- KTD9. **Fail cryptographic key-ring loading as a unit.** Oversized/count-exceeding key rings fail rather than returning a partial ring that could hide active decryption keys.
- KTD10. **Make repository sources deterministic without changing package ownership.** Add `<clear />` to package, mapping, and audit source sections; preserve `Headless.*` on GitHub Packages because NuGet's most-specific prefix outranks `*`.
- KTD11. **Pin every container input by digest.** Prefer multi-architecture manifest-list digests. When an image lacks one, select reviewed per-architecture digests through an explicit CI matrix or document the image as blocked; a mutable tag is not an immutable fallback. Verify Compose resolution on ARM64-compatible and x64 CI surfaces.
- KTD12. **Preflight collisions, then fail push races loudly.** Query the registry for every expected package identity/version immediately before publication and abort before the first push if any exists. Remove `--skip-duplicate`; a race detected during push still stops the release and requires maintainer reconciliation or a new version. Rejected alternative: compare rebuilt package hashes, which is unsafe until package output reproducibility is independently proven.
- KTD13. **Attest rather than add a certificate secret.** Use GitHub artifact provenance with narrowly scoped OIDC permissions. Certificate-based NuGet signing remains a separate custody decision.

### High-Level Technical Design

```mermaid
flowchart TB
  Request[Dashboard request] --> Auth[Dashboard auth boundary]
  Auth --> Discovery[Server-side discovery membership]
  Discovery -->|exact origin or visible node| Peer[Peer dashboard]
  Discovery -->|mismatch| Reject[Reject and clear stale selection]

  Work[Queued work or payload] --> Admission[Configured admission and size bounds]
  Admission -->|within bound| Execute[Execute or accelerate]
  Admission -->|capacity exceeded| Durable[Remain or return to durable recovery]

  Source[Declared source and pinned input] --> Build[Locked audited build]
  Build --> Verify[Package set and SBOM verification]
  Verify --> Attest[Provenance attestation]
  Attest --> Publish[Immutable publication]
```

### Assumptions

- `origin/main` at planning time is `08e58dfdf`; implementation refreshes from the latest `origin/main` before editing and revalidates touched paths.
- The repository's greenfield posture permits deliberate security-breaking changes when old behavior is unsafe or unused.
- The existing Headless SDK remains the primary NuGet audit enforcement surface; repo properties make the policy visible but do not duplicate its slow bounded-report target on every PR.
- No new NuGet or npm package is required. A GitHub-owned provenance Action may be added only at a reviewed full commit SHA.

### Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Existing node cookies stop resolving. | Fail closed, clear them, and let users reselect a discovered node once. |
| New limits reject legitimate large workloads. | Use conservative defaults, expose only the two limits lacking an existing semantic option, and document the escape hatch. |
| Queue overflow strands a message in queued state. | Persist the overflow back to delayed state and retain stale-queued recovery as a fallback. |
| Removing all-record routes breaks a hidden client. | Confirm the shipped SPA uses paginated routes, update API documentation, and make the breaking removal explicit in release notes/PR. |
| Container digest is architecture-specific. | Resolve and verify manifest-list digests; run Compose configuration and affected provider tests. |
| A registry collision could otherwise leave a partial release. | Preflight every expected identity/version before the first push, retain push-time race failure, document reconciliation/new-version recovery, and keep protected-environment approval before publication. |
| Attestation permissions broaden a normal PR build. | Grant `id-token` and `attestations` only to the release-only attestation/publish job. |

---

## Implementation Units

| Unit | Title | Primary files | Depends on |
| --- | --- | --- | --- |
| U1 | Secure Kubernetes peer resolution | Messaging Dashboard K8s provider, gateway proxy, Nodes SPA | None |
| U2 | Secure dashboard ping | Messaging dashboard endpoints and HTTP client registration | U1 |
| U3 | Require secure credential endpoints | FluentValidation, Captcha, Paymob options | None |
| U4 | Remove JWT query-token demo flow | Both dashboard login SPAs and JWT demos | None |
| U5 | Bound Jobs execution and fallback claims | Jobs scheduler, options, providers | None |
| U6 | Bound compressed and key-ring payloads | Jobs GZip helper, Data Protection blob repository | None |
| U7 | Bound Messaging capacity and initialization retries | Messaging queue/options and three seeders | None |
| U8 | Bound Jobs Dashboard HTTP surfaces | Jobs Dashboard endpoints/options/SPA | None |
| U9 | Make dependency inputs deterministic | NuGet config, Dependabot, frontend CI, containers | None |
| U10 | Verify and attest immutable packages | CI, package scripts, publishing guide | U9 |

### U1. Secure Kubernetes peer resolution

- **Goal:** Ensure client cookies can select only server-discovered nodes that satisfy Kubernetes visibility and port rules.
- **Requirements:** R3, R4
- **Dependencies:** None
- **Files:** `src/Headless.Messaging.Dashboard.K8s/K8sNodeDiscoveryProvider.cs`, `src/Headless.Messaging.Dashboard/GatewayProxy/GatewayProxyAgent.cs`, `src/Headless.Messaging.Dashboard/wwwroot/src/views/Nodes.vue`, `tests/Headless.Messaging.Dashboard.K8s.Tests.Unit/K8sNodeDiscoveryProviderTests.cs`, `tests/Headless.Messaging.Dashboard.Tests.Unit/GatewayProxy/GatewayProxyAgentTests.cs`, `src/Headless.Messaging.Dashboard.K8s/README.md`, `docs/llms/messaging.md`
- **Approach:** Extract one service-to-node mapping path used by list and direct lookup. Store node names—not namespaces or endpoints—in the selection cookie, resolve only within the server-configured namespace, clear invalid selections, and preserve authorization forwarding after successful discovery authorization.
- **Test scenarios:** Visible nodes resolve with the configured port; hidden/unlabelled nodes fail under the default policy; the explicit show-all option retains its documented behavior; a selection that omits namespace resolves only within the configured namespace and lookup fails closed when none is configured; cookies cannot select another namespace; endpoint-shaped and stale cookies never reach the downstream client.
- **Verification:** K8s discovery and dashboard proxy unit suites pass, the SPA build passes, and no direct lookup bypasses the shared visibility mapper.

### U2. Secure dashboard ping

- **Goal:** Remove anonymous and prefix-based target bypasses from the peer health probe.
- **Requirements:** R1, R2
- **Dependencies:** U1
- **Files:** `src/Headless.Messaging.Dashboard/Endpoints/MessagingDashboardEndpoints.cs`, `src/Headless.Messaging.Dashboard/DashboardOptionsExtensions.cs`, `tests/Headless.Messaging.Dashboard.Tests.Unit/Security/PingServicesSecurityTests.cs`, `tests/Headless.Messaging.Dashboard.Tests.Unit/Security/AuthorizationTests.cs`
- **Approach:** Place ping under the protected API group, parse and normalize the requested origin, compare exact scheme/IDN host/effective port to discovery results, construct the health URI structurally, and use a named redirect-disabled client with timeout and cancellation.
- **Test scenarios:** Exact registered origins succeed; userinfo, suffix, port, scheme, path, query, fragment, malformed, and redirect targets fail without an attacker request; Host auth protects ping; explicit no-auth remains deliberate.
- **Verification:** Focused endpoint tests prove request destination and auth behavior, with no `StartsWith` origin validation or URL concatenation remaining.

### U3. Require secure credential endpoints

- **Goal:** Prevent remote plaintext transport of captcha and payment credentials while preserving loopback development fixtures.
- **Requirements:** R5
- **Dependencies:** None
- **Files:** `src/Headless.FluentValidation/UrlValidators.cs`, `tests/Headless.FluentValidation.Tests.Unit/UrlValidatorsTests.cs`, Captcha option validators/tests, Paymob CashIn/CashOut option validators/tests, package READMEs, `docs/llms/captcha.md`, `docs/llms/payments.md`, `docs/llms/utilities.md`
- **Approach:** Add an additive HTTPS-or-loopback-HTTP validator that rejects userinfo. Apply it only to credential-bearing integration URLs; keep generic `HttpUrl()` behavior unchanged.
- **Test scenarios:** External HTTPS and loopback HTTP variants pass; remote HTTP, private-network hostnames over HTTP, userinfo, and malformed URLs fail; every affected options validator rejects remote plaintext configuration.
- **Verification:** FluentValidation, Captcha, and Paymob focused suites pass and mirrored docs describe the same transport rule.

### U4. Remove JWT query-token demo flow

- **Goal:** Keep demo bearer tokens out of query strings, request logs, and browser history entries.
- **Requirements:** R6
- **Dependencies:** None
- **Files:** both JWT demo `wwwroot/js/site.js` files, both dashboard SPA login views/utilities/tests, both JWT demo `Program.cs` files, and the Messaging JWT demo README
- **Approach:** Generate URL-encoded fragment tokens, consume them only in Host login mode, immediately clean the URL with history replacement, remove broad demo `OnMessageReceived` query extraction, and leave general SignalR token conventions outside scope.
- **Test scenarios:** Fragment tokens are normalized, consumed, and removed; query tokens and non-Host fragments are ignored; invalid login clears state; demo-generated links contain no token query parameter.
- **Verification:** Both dashboard frontend test/build gates and both demo project builds pass.

### U5. Bound Jobs execution and fallback claims

- **Goal:** Prevent dedicated-thread floods and full-backlog allocation while retaining deterministic recovery.
- **Requirements:** R7, R9
- **Dependencies:** None
- **Files:** `src/Headless.Jobs.Core/JobsThreadPool/JobsTaskScheduler.cs`, `src/Headless.Jobs.Core/JobsOptionsBuilder.cs`, `src/Headless.Jobs.Core/DependencyInjection/SetupJobs.cs`, `src/Headless.Jobs.EntityFramework/Infrastructure/JobsClaimStrategy.cs`, `src/Headless.Jobs.Core/Provider/JobsInMemoryPersistenceProvider.cs`, Jobs unit/provider tests, and Jobs Core README
- **Approach:** Add a positive `MaxLongRunningConcurrency` with a conservative derived default, wait before dedicated-thread creation, release permits on every terminal path, and apply stable order plus the existing 100-root limit to CAS/in-memory time-job and cron fallback claims.
- **Test scenarios:** Admission never exceeds the configured count; completion, faults, cancellation, and disposal release or cancel waiters; 101 eligible roots drain as 100 then one in oldest-first order for both time and cron paths.
- **Verification:** Jobs Core/EntityFramework builds, scheduler tests, and provider claim tests pass without changing ordinary-priority behavior.

### U6. Bound compressed and key-ring payloads

- **Goal:** Stop decompression and XML key-ring inputs from allocating unbounded memory.
- **Requirements:** R8, R14
- **Dependencies:** None
- **Files:** `src/Headless.Jobs.Core/JobsHelper.cs`, `src/Headless.Jobs.Core/JobsOptionsBuilder.cs`, Jobs helper/options tests and README, `src/Headless.Api.DataProtection/BlobStorageDataProtectionXmlRepository.cs`, its logger extensions and unit tests
- **Approach:** Preserve the GZip sentinel format, add a 64 MiB default configurable expanded-size limit, and stream into a bounded buffer. Load Data Protection XML incrementally with 1 MiB per item, 1,000 items, 16 MiB aggregate, DTD prohibition, and all-or-nothing limit failure.
- **Test scenarios:** Exact limits pass; limit-plus-one, high-ratio, lying metadata, too many XML blobs, excessive aggregate size, malformed/truncated data, and DTD input fail deterministically without partial state; existing payloads and normal key rings still load.
- **Verification:** Jobs and Data Protection focused unit/integration suites pass and backend streams are disposed on rejection.

### U7. Bound Messaging capacity and initialization retries

- **Goal:** Keep accelerated scheduling, configured concurrency, and replicated initialization within explicit resource budgets.
- **Requirements:** R10, R11, R12
- **Dependencies:** None
- **Files:** `src/Headless.Messaging.Core/Internal/ScheduledMediumMessageQueue.cs`, `src/Headless.Messaging.Core/Processor/Dispatcher.cs`, `src/Headless.Messaging.Core/Configuration/MessagingOptions.cs`, related Messaging unit tests, three initialization background services and their tests/READMEs
- **Approach:** Use `SchedulerBatchSize` as in-memory queue capacity, persist overflow back to delayed state, validate thread/factor values in `1..1024`, checked buffer product at most 100,000, and batch sizes in `1..100,000`. Copy `RetryBatchSize`. Enable jitter with a 30-second maximum and exclude cancellation, argument, and unsupported-operation failures from retries.
- **Test scenarios:** Concurrent queue capacity and dequeue release are correct; overflow remains durable; boundary and excessive options validate correctly; arithmetic cannot overflow; retryable failures retain ten attempts while permanent/cancelled failures execute once and virtual delays remain capped.
- **Verification:** Messaging Core plus Features/Permissions/Settings focused suites pass, including copy and startup-validation tests.

### U8. Bound Jobs Dashboard HTTP surfaces

- **Goal:** Make dashboard requests bounded before body buffering or repository access.
- **Requirements:** R13
- **Dependencies:** None
- **Files:** `src/Headless.Jobs.Dashboard/Endpoints/DashboardEndpoints.cs`, `src/Headless.Jobs.Dashboard/DashboardOptionsBuilder.cs`, `src/Headless.Jobs.Dashboard/DependencyInjection/ServiceCollectionExtensions.cs`, Jobs Dashboard README, focused endpoint tests, and unused SPA service exports
- **Approach:** Remove all-record route mappings, reject page sizes outside 1..100, attach 1 MiB request-size metadata to body routes, deserialize from the request stream, reject batch mutations above 500 IDs, and preserve API 404 responses while retaining the SPA fallback only for client-side navigation.
- **Test scenarios:** Boundary page sizes pass; invalid sizes return 400; oversized/chunked bodies return 413 before full buffering; invalid JSON returns 400; 501 IDs fail; removed API routes return 404, SPA deep links still resolve, and paginated responses remain stable.
- **Verification:** Dashboard project and endpoint tests pass and the SPA has no all-record route dependency.

### U9. Make dependency inputs deterministic

- **Goal:** Eliminate inherited restore sources and unmonitored/mutable dependency surfaces.
- **Requirements:** R15, R16, R17, R21
- **Dependencies:** None
- **Files:** `nuget.config`, `.github/dependabot.yml`, `.github/workflows/dashboards.yml`, all maintained Dockerfiles/Compose files, `src/Headless.Testing.Testcontainers/TestImages.cs`, and affected integration fixtures
- **Approach:** Clear and declare package/mapping/audit sources, verify the pinned SDK's existing audit enforcement without adding a duplicate repository policy surface, extend Dependabot and Tus CI coverage, and pin every maintained container reference to a reviewed digest.
- **Test scenarios:** Effective restore excludes inherited feeds; locked Release restore retains advisory enforcement; YAML parses; Tus installs/builds; every Compose file resolves; affected x64/ARM-capable test images start and provider tests pass.
- **Verification:** NuGet source listing, locked restore, npm audit/build, Compose validation, focused integration suites, and action-SHA audit succeed.

### U10. Verify and attest immutable packages

- **Goal:** Ensure only the complete, source-linked, SBOM-bearing package set from the release commit can reach a registry.
- **Requirements:** R18, R19, R20, R21
- **Dependencies:** U9
- **Files:** `.github/workflows/ci.yml`, `Makefile`, `eng/expected-packages.txt`, `scripts/verify-packages.sh`, `tests/scripts/verify-packages-tests.sh`, and `docs/solutions/guides/publish-packages-guide.md`
- **Approach:** Maintain an independently reviewed, committed package-ID manifest; compare it with evaluated packable project identities before packing and with artifact identities afterward. Verify embedded SPDX metadata and repository commit provenance, preflight the complete identity/version set against NuGet.org before the first push, remove silent duplicate acceptance, add a release-only GitHub provenance attestation step pinned to a full SHA, and scope attestation/OIDC permissions to that job.
- **Test scenarios:** Valid complete artifacts pass; missing/extra/duplicate packages, corrupt archives, wrong IDs/versions/commit metadata, missing/invalid SBOMs, and preflight collisions fail before publication; push-time collisions stop immediately; PR builds never receive attestation or publishing permissions.
- **Verification:** ShellCheck and script fixtures pass, a full package build verifies successfully, workflow permission review passes, and a dry-run/release-path inspection proves publishing cannot begin after a failed verifier.

---

## Verification Contract

| Gate | Applies to | Done signal |
| --- | --- | --- |
| Focused project builds | U1-U8 | Every touched source/test project builds through `make build-project` without warnings. |
| Focused unit tests | U1-U8 | Security bypass, boundary, cancellation, durability, and compatibility scenarios pass. |
| Frontend tests/builds | U1, U2, U4, U8, U9 | Both dashboards pass unit/lint/build gates; Tus passes clean install and build. |
| Provider integration tests | U5, U6, U9 | Changed EF, Data Protection, and container-backed providers pass where Docker is available. |
| Dependency verification | U9 | Locked Release restore uses only declared feeds and completes with NuGet audit enforcement; npm audits are clean. |
| Package verification | U10 | Full pack output passes manifest/SBOM/source checks and negative verifier fixtures fail as intended. |
| Shell and workflow quality | U9, U10 | ShellCheck passes; YAML parses; every Action is full-SHA pinned; permissions remain least-privilege. |
| Repository quality | All | `make format-check`, relevant analyzer gates, and final branch-diff review pass. |
| CI | All | Required GitHub checks reach a decided green state or an external blocker is recorded without weakening gates. |

---

## Definition of Done

- All R1-R23 requirements are implemented or explicitly preserved, with R22 verified by an unchanged mediator logging diff.
- Every exploit and resource-boundary acceptance example has a focused regression test.
- Public options, breaking endpoint behavior, retry semantics, and operational release recovery are documented in canonical and mirrored docs.
- No inherited NuGet source, unmonitored maintained lockfile/Docker directory, floating container reference, or silent package collision remains; every maintained container reference follows KTD11.
- Existing secure CI controls remain intact and no new long-lived secret is introduced.
- The final diff contains no abandoned experiments, unrelated refactors, generated artifacts, or user-owned changes.
- The work is committed in coherent slices, pushed on a new `xshaheen/` branch, opened as a PR against `main`, and its CI/review state is made durable.
- After PR publication, R23's GitHub administrator settings recommendations are reported separately without mutating repository settings.
