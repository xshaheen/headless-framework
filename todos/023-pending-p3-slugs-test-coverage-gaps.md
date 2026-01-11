# Test Coverage Gaps

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, testing, dotnet, slugs

---

## Problem Statement

Current tests only cover happy path scenarios. Missing coverage:

```csharp
// Current tests (SlugTests.cs):
// - should_generate_urls_as_expected (14 cases)
// - should_keep_case (12 cases)
```

**Missing test scenarios:**
- `null` input
- Empty string input
- Whitespace-only input
- Very long inputs (performance/memory)
- Custom `AllowedRanges`
- `Separator = null` behavior
- `MaximumLength` edge cases (0, 1, exact length)
- Thread safety with shared options
- Unicode edge cases (surrogate pairs, ZWJ sequences)
- `CanEndWithSeparator = true`
- Custom `Replacements`
- `Culture` with case transformations

---

## Proposed Solutions

### Add Missing Test Cases
```csharp
[Fact]
public void should_return_null_when_input_is_null()
{
    Slug.Create(null).Should().BeNull();
}

[Fact]
public void should_handle_empty_string()
{
    Slug.Create("").Should().Be("");  // or null?
}

[Theory]
[InlineData(1, "hello", "h")]
[InlineData(5, "hello-world", "hello")]
public void should_respect_maximum_length(int max, string input, string expected)
{
    var options = new SlugOptions { MaximumLength = max };
    Slug.Create(input, options).Should().Be(expected);
}

[Fact]
public void should_handle_emoji_safely()
{
    // Emoji are typically filtered out
    Slug.Create("test ").Should().Be("test");
}
```

---

## Acceptance Criteria

- [ ] Test `null` input returns `null`
- [ ] Test empty string handling
- [ ] Test `MaximumLength` = 0, 1, exact, overflow
- [ ] Test custom `AllowedRanges`
- [ ] Test `Separator` edge cases
- [ ] Test `CanEndWithSeparator = true`
- [ ] Test custom `Replacements`
- [ ] Test very long input (>10KB)
- [ ] Test Unicode edge cases

---

## Technical Details

**Affected Files:**
- `tests/Framework.Slugs.Tests.Unit/SlugTests.cs`

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
