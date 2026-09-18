---
title: "ProblemDetails error codes use the g:lower_snake_case shape"
date: 2026-09-18
last_updated: 2026-09-18
category: conventions
module: Headless.Api
problem_type: naming_convention
component: api_contract
severity: medium
tags:
  - problem-details
  - error-codes
  - localization
  - resx
related_components:
  - MessageDescriber
  - Messages.resx
applies_when:
  - "Adding an error code to a ProblemDetails response"
  - "Adding or renaming a MessageDescriber entry"
---

# ProblemDetails error codes

Error codes embedded in `ProblemDetails` responses use the `g:lower_snake_case` shape. The `g:` prefix marks a
"general" code that belongs to the framework's shared descriptor space: `g:tenant_required`,
`g:idempotency_key_reused`, `g:concurrency_failure`.

Do not use kebab-case (`g:tenant-required`) or any other separator. Every existing framework code is
snake_case, and a client parsing `errors[].code` should see one consistent shape.

A new code goes in the relevant `MessageDescriber` class plus matching `Messages.resx` and `Messages.ar.resx`
entries. The resx `<data name="...">` attribute uses the same `g:snake_case` form; the generated C# field
collapses the `:` to `_`, giving `Messages.g_snake_case`.

Expose a code that clients reference as a `public const string` on a `*ErrorCodes` static class marked
`[PublicAPI]`, so client code has a compile-time link to it.
