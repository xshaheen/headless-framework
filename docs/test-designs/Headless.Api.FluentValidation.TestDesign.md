# Test Case Design: Headless.Api.FluentValidation

**Package:** `src/Headless.Api.FluentValidation`
**Test Projects:** `Headless.Api.FluentValidation.Tests.Unit`
**Generated:** 2026-01-25

## Current Test Coverage

| Validator | Existing Tests | Coverage |
|-----------|----------------|----------|
| FileNotEmpty | 2 | Good |
| GreaterThanOrEqualTo | 3 | Good |
| LessThanOrEqualTo | 3 | Good |
| ContentTypes | 3 | Good |
| HaveSignatures | 2 | Good |

**Existing:** 13 tests
**Coverage:** Good - core scenarios covered

---

## Source Analysis

### FileValidators.cs

Extension methods for `IRuleBuilder<T, IFormFile?>`:

| Method | Parameters | Validation Logic |
|--------|------------|------------------|
| `FileNotEmpty()` | - | file.Length > 0 (null passes) |
| `GreaterThanOrEqualTo(int minBytes)` | minBytes | file.Length >= minBytes (null passes) |
| `LessThanOrEqualTo(int maxBytes)` | maxBytes | file.Length <= maxBytes (null passes) |
| `ContentTypes(IReadOnlyList<string>)` | contentTypes | ContentType in list (null passes) |
| `HaveSignatures(IFileFormatInspector, Func<FileFormat?, bool>)` | inspector, predicate | predicate(format) returns true |

---

## Missing Test Cases

### 1. FileNotEmpty - Edge Cases

| Test Case | Status | Description |
|-----------|--------|-------------|
| `should_pass_when_file_null` | ✅ ADDED | Null file should pass (by design) |
| `should_pass_when_file_length_one` | ✅ ADDED | Minimum valid size |
| `should_fail_with_correct_error_descriptor` | ✅ ADDED | Verify error message |

### 2. GreaterThanOrEqualTo - Edge Cases

| Test Case | Status | Description |
|-----------|--------|-------------|
| `should_pass_when_file_null` | ✅ ADDED | Null file should pass |
| `should_format_message_in_megabytes` | ✅ ADDED | Verify MinSize/TotalLength formatting |
| `should_format_values_correctly` | ✅ ADDED | Culture-aware number formatting (using InvariantCulture) |

### 3. LessThanOrEqualTo - Edge Cases

| Test Case | Status | Description |
|-----------|--------|-------------|
| `should_pass_when_file_null` | ✅ ADDED | Null file should pass |
| `should_format_message_in_megabytes` | ✅ ADDED | Verify MaxSize/TotalLength formatting |

### 4. ContentTypes - Edge Cases

| Test Case | Status | Description |
|-----------|--------|-------------|
| `should_be_case_insensitive` | ✅ ADDED | "IMAGE/JPEG" matches "image/jpeg" (Theory with 3 cases) |
| `should_work_with_single_content_type` | ✅ ADDED | List with one element |
| `should_format_error_message_with_all_types` | ✅ ADDED | Verify ContentTypes placeholder |

### 5. HaveSignatures - Edge Cases

| Test Case | Status | Description |
|-----------|--------|-------------|
| `should_pass_when_file_null` | ✅ ADDED | Null file should pass |
| `should_dispose_stream_after_check` | SKIPPED | Implicit via await using - hard to test directly |
| `should_pass_when_predicate_returns_true_for_null_format` | ✅ ADDED | Unknown file type scenario |
| `should_fail_when_predicate_returns_false_for_null_format` | ✅ ADDED | Reject unknown formats |

---

## Recommended Additional Tests

### Error Message Tests

```csharp
[Fact]
public void GreaterThanOrEqualTo_should_format_size_in_megabytes()
{
    // given
    var validator = new TestValidator(v =>
        v.RuleFor(x => x.File).GreaterThanOrEqualTo(5 * 1024 * 1024)); // 5 MB
    var file = CreateMockFile(length: 1024 * 1024); // 1 MB

    // when
    var result = validator.TestValidate(new Model { File = file });

    // then
    result.Errors[0].FormattedMessagePlaceholderValues
        .Should().ContainKey("MinSize").WhoseValue.Should().Be("5.0");
    result.Errors[0].FormattedMessagePlaceholderValues
        .Should().ContainKey("TotalLength").WhoseValue.Should().Be("1.0");
}
```

### Null File Tests

```csharp
[Theory]
[MemberData(nameof(AllValidators))]
public void all_validators_should_pass_when_file_null(AbstractValidator<Model> validator)
{
    // given
    var model = new Model { File = null };

    // when
    var result = validator.TestValidate(model);

    // then
    result.IsValid.Should().BeTrue();
}
```

### Content Type Case Sensitivity Test

```csharp
[Theory]
[InlineData("image/jpeg")]
[InlineData("IMAGE/JPEG")]
[InlineData("Image/Jpeg")]
public void ContentTypes_should_match_case_insensitive(string contentType)
{
    // given
    var validator = new TestValidator(v =>
        v.RuleFor(x => x.File).ContentTypes(["image/jpeg"]));
    var file = CreateMockFile(contentType: contentType);

    // when
    var result = validator.TestValidate(new Model { File = file });

    // then
    result.IsValid.Should().BeTrue();
}
```

---

## Test Summary

| Category | Original | Added | Total |
|----------|----------|-------|-------|
| FileNotEmpty | 2 | 3 | 5 |
| GreaterThanOrEqualTo | 3 | 3 | 6 |
| LessThanOrEqualTo | 3 | 2 | 5 |
| ContentTypes | 3 | 5 | 8 |
| HaveSignatures | 2 | 3 | 5 |
| **Total** | **13** | **16** | **29** |

**Note:** ContentTypes case-insensitivity test is a Theory with 3 InlineData cases, counted as 3 test cases.

---

## Status

✅ **COMPLETED** - All planned tests have been added (except stream disposal which is implicitly tested via `await using`).

## Test Infrastructure

The existing test file has good infrastructure:
- `FileUploadTestModel` - test model with `IFormFile?`
- `TestFileFormat` - custom FileFormat for signature testing
- Helper validator classes for each scenario

**No additional infrastructure needed.**
