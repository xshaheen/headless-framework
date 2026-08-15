# Residual Review Findings — tenant catalog (#253)

Source: `x-code-review` run `20260815-224107-ec752a97` on branch `xshaheen/feat-tenant-catalog`
(base `0f7bf25d6`), verdict **Ready with fixes**. 13 findings survived validation; 12 were applied
in `fix(review): close tenant catalog review findings`. This file records what was deliberately not
applied, plus the risks and gaps the review surfaced.

No tracker tickets were filed (no sink configured for this run), so this file is the durable record.

## Not applied — decision required

- **P2 — Identifier rebrand routes anonymous traffic to the previous tenant.**
  `src/Headless.MultiTenancy/TenantCatalogService.cs:136` (correctness). Re-pointing an identifier to a
  different tenant inside the cache-expiration window keeps routing anonymous, claim-free traffic to
  the old tenant. This is the documented R14 no-reuse trade-off (the SPI is read-only, so there is no
  framework invalidation hook). Authenticated requests fail closed via R19. Options: keep the
  documented no-reuse rule as-is, add an invalidation hook to the SPI, or shorten the documented
  window. Owner: human.

- **P2 — `StatusCodesRewriterMiddleware` accretes per-feature special cases.**
  `src/Headless.Api.Core/Middlewares/StatusCodesRewriterMiddleware.cs:57` (maintainability, demoted to
  residual). The shared 403 branch now carries two feature booleans and a catalog-options dependency
  that reaches every host using the middleware. A per-feature rejection-override seam would stop the
  ladder from growing. Not a user-facing defect. Owner: human.

- **P2 — `UseInMemory(IConfiguration)` deliberately not added.** `TenantInfo` exposes no parameterless
  constructor for the options binder — which is exactly why `ConfigurationTenantSeed` and
  `UseConfiguration(IConfiguration)` exist. Adding it would be a duplicate entry point for a store that
  cannot bind. The trio convention is satisfied by the two delegate overloads plus raw options.

- **P2 residual on the mismatch collapse.** A mismatch visible only to a non-default authentication
  scheme is rejected by the authorization tier, whose forbid is collapsed to the generic 404 by
  `StatusCodesRewriterMiddleware`. A host that does not register that middleware keeps a
  distinguishable 403 for that narrow case. Closing it would require registering a Headless
  `IAuthorizationMiddlewareResultHandler` — a last-wins service that would clobber a consumer's own.

## Residual risks

- Negative identifier caching is keyed on attacker-controlled input with no entry-count bound; probes
  can amplify eviction in the shared `ICache` working set. Rate limiting is delegated to consumers.
- Pre-auth ambient tenant makes idempotency keys attacker-derivable when `RequireUserIdentity=false`.
- Catalog cache lookups bypass `ICache.GetOrAddAsync` stampede protection (deliberate: one store read
  populates two cache axes, which the single-factory model does not support).
- `EfTenantStore<TContext>` is a singleton capturing `IDbContextFactory<TContext>`; a host registering
  the factory as scoped creates a captive dependency. No fixture covers that.
- `TenantRecordConfiguration` maps collations only for SQL Server and PostgreSQL; other providers fall
  back to their default (possibly case-insensitive) collation, weakening the ordinal lookup contract.
- Timing separation between unknown (store round trip) and disabled (cache hit) survives the
  byte-identical bodies.
- `MaxIdentifierLength` is validated only `GreaterThan(0)`; a raised bound plus a custom backtracking
  pattern is a pre-auth ReDoS surface, mitigated only by the generated-regex match timeout.
- Contract/implementation namespace split is permanent by design (KTD1): the moved contracts live in
  `Headless.MultiTenancy`, their default implementations stay in `Headless.Core`.

## Testing gaps

- No byte-identical assertion between the mismatch rejection and the unknown/disabled rejection.
- No test for a catalog host without `UseStatusCodesRewriter()` or with a non-403 forbid handler.
- No cache-key test with a hostile identifier under a custom `IdentifierPattern`.
- No conformance case for a store returning a `TenantInfo` whose `Identifier` differs from the queried
  normalized identifier — a contract the catalog service trusts without checking.
- No stampede/concurrency test for cold-cache bursts.
- AE10's fail-at-startup claim is proven via forced service resolution, not `IHost.StartAsync()`.

## Coverage note

The independent cross-model adversarial pass (Codex, `gpt-5.6-luna` at xhigh) started but terminated
on a provider usage limit and produced no usable output. The adversarial lens was therefore covered by
local personas only — recorded as degraded coverage for this run.

## Pre-existing, out of scope

- `src/Headless.MultiTenancy/HeadlessTenancyStartupValidator.cs:20` — MA0204 (unnecessary partial).
- `src/Headless.MultiTenancy.Abstractions/TenantInfo.cs:30` — IDE0290 (use primary constructor).
- `src/Headless.Api.Core/SetupApiTenancy.cs:183` — RCS1146.
  All three verified byte-identical to `HEAD` and left unsuppressed; they make
  `make quality-analyzers-project` exit non-zero for those projects.
