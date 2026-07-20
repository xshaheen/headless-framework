## Summary

<!--
Explain the problem or opportunity, the outcome of this PR, and why this approach was chosen.
Prefer 2-4 concise bullets. Describe observable behavior rather than restating the diff.
-->

## Related Work

<!--
Use "Fixes #123" only when merging this PR fully resolves the issue.
Use "Related: #123" for partial or supporting work. Use "N/A" when there is no tracked item.
-->

## Scope

<!-- List the affected `Headless.*` packages or domains and any intentional non-goals. -->

Affected packages or domains:

-

Out of scope:

-

## Change Type

- [ ] Bug fix
- [ ] Feature
- [ ] Refactor
- [ ] Performance
- [ ] Documentation only
- [ ] Test only
- [ ] Build or CI

## Compatibility and Breaking Changes

- [ ] No breaking changes
- [ ] Breaking change (apply the `major` label and complete the details below)
- [ ] Compatibility impact is uncertain and needs reviewer input

<!--
A breaking change includes more than removed APIs: changed defaults, wire formats, configuration,
storage schemas, or runtime behavior can also require consumer action.
Check every affected surface below; leave them unchecked when there is no breaking change.
-->

- [ ] Source/API compatibility
- [ ] Binary compatibility
- [ ] Behavioral compatibility (including changed defaults)
- [ ] Configuration or deployment compatibility
- [ ] Data, storage, or serialization compatibility

Breaking-change details:

<!--
For each break, describe:
1. Previous behavior or contract.
2. New behavior or contract.
3. Affected packages and consumers.
4. Why the break is necessary.
5. Supported replacement and concrete migration steps.
Use "N/A" when there is no breaking change.
-->

```text
N/A
```

## Validation

<!-- Check the gates run for this change. Explain every relevant skipped or failed gate below. -->

- [ ] Focused build or test for the affected projects
- [ ] `make build`
- [ ] `make format-check`
- [ ] `make test-unit`
- [ ] `make test-integration`
- [ ] Manual or end-to-end verification
- [ ] No runtime validation required (documentation or workflow text only)

Validation details:

```text
Paste the exact commands and results. List intentionally skipped checks with a reason.
```

## Tests and Documentation

- [ ] Added or updated tests for behavior changes
- [ ] Added provider-conformance coverage where shared behavior changed
- [ ] Updated XML docs for public API changes
- [ ] Updated affected package `README.md` files
- [ ] Updated matching `docs/llms/` guidance
- [ ] No package versions were added directly to `.csproj` files
- [ ] Tests and documentation are not affected

<!-- Explain unchecked items that would normally apply, or use "N/A". -->

```text
N/A
```

## Reviewer Guide

<!--
Point reviewers to the highest-risk decisions, important files, or intentionally non-obvious behavior.
Include rollout, rollback, security, performance, or operational concerns when relevant. Use "N/A" if none.
-->

## Release Notes

<!--
Write the consumer-facing changelog entry in present tense. Include migration guidance for breaking changes.
Use "N/A" for internal-only changes that should not appear in release notes.
-->
