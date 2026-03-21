---
branch: main
reviewers: [strict-dotnet-reviewer, pragmatic-dotnet-reviewer, code-simplicity-reviewer, pattern-recognition-specialist]
findings:
  p1_critical: 0
  p2_important: 4
  p3_nice_to_have: 2
  total: 6
timestamp: 2026-03-21T00:00:00Z
---

# Code Review — HeadlessSaveChangesRunner Refactor

Review of the save pipeline centralization: `HeadlessSaveChangesRunner` (new), `AuditSavePipelineHelper` (updated), `HeadlessDbContext` + `HeadlessIdentityDbContext` (updated).

## Reviewers Used

- `strict-dotnet-reviewer` — correctness, API design, behavioral regressions
- `pragmatic-dotnet-reviewer` — simplicity, shape, anti-patterns
- `code-simplicity-reviewer` — YAGNI, duplication
- `pattern-recognition-specialist` — naming conventions, API parity, ordering

## Key Findings

- No P1 blockers. The refactor is structurally sound.
- `_GetServiceOrDefault` catch-all is an anti-pattern — use `GetInfrastructure().GetService<T>()` instead.
- Delegate call sites pass `PublishMessagesAsync` twice via overload resolution — fragile, needs explicit lambdas.
- `HeadlessIdentityDbContext.ExecuteTransactionAsync` is missing `CancellationToken` on all 4 overloads.
- Behavioral change: Identity context now wraps audit-only saves in a transaction (was outside TX before). Likely correct but needs documentation.

## References

- Todos: `docs/todos/012-017-pending-*.md`
