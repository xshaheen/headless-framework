# Headless Framework Miscellaneous Packages Test Design

## Overview

This test design covers smaller utility packages that don't warrant individual test design documents.

### Packages Covered
1. **Headless.Redis** - Redis Lua scripts and connection extensions
2. **Headless.Recaptcha** - Google reCAPTCHA v2/v3 verification
3. **Headless.Sitemaps** - XML sitemap generation
4. **Headless.Slugs** - URL slug generation
5. **Headless.NetTopologySuite** - Geography/geometry utilities
6. **Headless.Logging.Serilog** - Serilog configuration factory
7. **Headless.Media.Indexing** - Document text extraction (PDF, Word, PowerPoint)

### Existing Tests
**No existing tests found for any of these packages**

---

## 1. Headless.Redis

### 1.1 HeadlessRedisScriptsLoader - LoadScriptsAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 1 | should_load_scripts_once | Integration | Idempotent loading |
| 2 | should_use_async_lock | Integration | Concurrency protection |
| 3 | should_skip_replica_servers | Integration | Master-only loading |
| 4 | should_load_all_scripts | Integration | All 5 scripts loaded |
| 5 | should_log_trace_when_enabled | Unit | Logging verification |

### 1.2 HeadlessRedisScriptsLoader - ReplaceIfEqualAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 6 | should_replace_when_equal | Integration | Replace matching value |
| 7 | should_not_replace_when_different | Integration | Skip non-matching |
| 8 | should_replace_when_key_missing | Integration | Create new key |
| 9 | should_set_expiration | Integration | TTL set on replace |
| 10 | should_return_negative_on_mismatch | Integration | Return -1 |

### 1.3 HeadlessRedisScriptsLoader - RemoveIfEqualAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 11 | should_remove_when_equal | Integration | Delete matching |
| 12 | should_not_remove_when_different | Integration | Skip non-matching |
| 13 | should_return_zero_on_mismatch | Integration | Return 0 |

### 1.4 HeadlessRedisScriptsLoader - IncrementAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 14 | should_increment_long_value | Integration | Integer increment |
| 15 | should_increment_double_value | Integration | Float increment |
| 16 | should_set_expiration_on_increment | Integration | TTL set |
| 17 | should_create_key_if_missing | Integration | Initialize to value |

### 1.5 HeadlessRedisScriptsLoader - SetIfHigherAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 18 | should_set_when_higher_long | Integration | Higher value wins |
| 19 | should_not_set_when_lower_long | Integration | Lower value skipped |
| 20 | should_set_when_higher_double | Integration | Float comparison |
| 21 | should_create_key_if_missing | Integration | Initialize |
| 22 | should_set_expiration | Integration | TTL on update |

### 1.6 HeadlessRedisScriptsLoader - SetIfLowerAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 23 | should_set_when_lower_long | Integration | Lower value wins |
| 24 | should_not_set_when_higher_long | Integration | Higher value skipped |
| 25 | should_set_when_lower_double | Integration | Float comparison |

### 1.7 ConnectionMultiplexerExtensions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 26 | should_flush_all_master_databases | Integration | FlushAllAsync |
| 27 | should_skip_replica_on_flush | Integration | Master-only flush |
| 28 | should_count_all_keys | Integration | CountAllKeysAsync |
| 29 | should_return_zero_for_empty | Integration | No endpoints |

### 1.8 RedisScripts Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 30 | should_have_valid_lua_syntax_replace_if_equal | Unit | Lua parsing |
| 31 | should_have_valid_lua_syntax_remove_if_equal | Unit | Lua parsing |
| 32 | should_have_valid_lua_syntax_set_if_higher | Unit | Lua parsing |
| 33 | should_have_valid_lua_syntax_set_if_lower | Unit | Lua parsing |
| 34 | should_have_valid_lua_syntax_increment | Unit | Lua parsing |

---

## 2. Headless.Recaptcha

### 2.1 ReCaptchaSiteVerifyV2 Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 35 | should_verify_valid_token | Unit | Success response |
| 36 | should_include_remote_ip | Unit | Optional remoteip |
| 37 | should_throw_on_http_error | Unit | EnsureSuccessStatusCode |
| 38 | should_deserialize_response | Unit | ReCaptchaSiteVerifyV2Response |
| 39 | should_log_on_failure | Unit | Logger called |
| 40 | should_throw_on_null_response | Unit | InvalidOperationException |

### 2.2 ReCaptchaSiteVerifyV3 Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 41 | should_verify_valid_token | Unit | Success response |
| 42 | should_return_score | Unit | Score property |
| 43 | should_return_action | Unit | Action property |

### 2.3 ReCaptchaOptions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 44 | should_require_site_key | Unit | Required validation |
| 45 | should_require_site_secret | Unit | Required validation |

### 2.4 ReCaptchaError Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 46 | should_map_error_codes | Unit | Error code mapping |
| 47 | should_handle_unknown_error | Unit | Unknown error |

### 2.5 TagHelpers Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 48 | should_render_v2_script_tag | Unit | ReCaptchaV2ScriptTagHelper |
| 49 | should_render_v2_div_tag | Unit | ReCaptchaV2DivTagHelper |
| 50 | should_render_v3_script_tag | Unit | ReCaptchaV3ScriptTagHelper |
| 51 | should_include_language_code | Unit | IReCaptchaLanguageCodeProvider |

### 2.6 Setup Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 52 | should_register_v2_services | Unit | DI registration |
| 53 | should_register_v3_services | Unit | DI registration |
| 54 | should_configure_http_client | Unit | Named HttpClient |

---

## 3. Headless.Sitemaps

### 3.1 SitemapIndexBuilder Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 55 | should_write_valid_xml | Unit | XML structure |
| 56 | should_include_sitemapindex_root | Unit | Root element |
| 57 | should_write_sitemap_elements | Unit | sitemap children |
| 58 | should_include_loc_element | Unit | Location URL |
| 59 | should_include_lastmod_when_present | Unit | Optional lastmod |
| 60 | should_omit_lastmod_when_null | Unit | Conditional element |
| 61 | should_use_correct_namespace | Unit | sitemaps.org namespace |
| 62 | should_respect_cancellation | Unit | CancellationToken |

### 3.2 SitemapUrl Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 63 | should_write_url_element | Unit | url element |
| 64 | should_include_priority | Unit | Priority element |
| 65 | should_include_changefreq | Unit | Change frequency |
| 66 | should_include_images | Unit | Image sub-elements |
| 67 | should_include_alternate_urls | Unit | Hreflang links |

### 3.3 SitemapUrls Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 68 | should_write_urlset_root | Unit | urlset element |
| 69 | should_write_multiple_urls | Unit | Collection support |

### 3.4 ChangeFrequency Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 70 | should_serialize_always | Unit | Enum value |
| 71 | should_serialize_hourly | Unit | Enum value |
| 72 | should_serialize_daily | Unit | Enum value |
| 73 | should_serialize_weekly | Unit | Enum value |
| 74 | should_serialize_monthly | Unit | Enum value |
| 75 | should_serialize_yearly | Unit | Enum value |
| 76 | should_serialize_never | Unit | Enum value |

---

## 4. Headless.Slugs

### 4.1 Slug.Create Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 77 | should_return_null_for_null | Unit | Null handling |
| 78 | should_lowercase_by_default | Unit | ToLowerCase transformation |
| 79 | should_uppercase_when_configured | Unit | ToUpperCase transformation |
| 80 | should_preserve_case_when_none | Unit | No transformation |
| 81 | should_replace_spaces_with_separator | Unit | Default hyphen |
| 82 | should_use_custom_separator | Unit | Custom separator |
| 83 | should_apply_replacements | Unit | Custom replacements |
| 84 | should_respect_max_length | Unit | Truncation |
| 85 | should_remove_diacritics | Unit | Unicode normalization |
| 86 | should_filter_allowed_characters | Unit | IsAllowed predicate |
| 87 | should_not_start_with_separator | Unit | Leading separator |
| 88 | should_not_end_with_separator | Unit | Trailing separator |
| 89 | should_allow_ending_separator | Unit | CanEndWithSeparator |
| 90 | should_handle_consecutive_spaces | Unit | Multiple separators |
| 91 | should_use_culture_for_casing | Unit | Culture-aware |
| 92 | should_normalize_output | Unit | FormC normalization |

### 4.2 SlugOptions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 93 | should_default_separator_to_hyphen | Unit | Default value |
| 94 | should_default_casing_to_lowercase | Unit | Default value |
| 95 | should_default_max_length_to_zero | Unit | Unlimited |
| 96 | should_default_can_end_separator_false | Unit | Default value |

---

## 5. Headless.NetTopologySuite

### 5.1 GeoExtensions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 97 | should_create_point_from_coordinates | Unit | Point creation |
| 98 | should_calculate_distance | Unit | Distance calculation |

### 5.2 GeoConstants Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 99 | should_have_correct_srid | Unit | SRID constant |

---

## 6. Headless.Logging.Serilog

### 6.1 SerilogFactory Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 100 | should_create_logger_configuration | Unit | Factory method |
| 101 | should_configure_enrichers | Unit | Standard enrichers |
| 102 | should_configure_console_sink | Unit | Console output |
| 103 | should_configure_file_sink | Unit | File output |
| 104 | should_use_options_for_configuration | Unit | SerilogOptions |

### 6.2 SerilogOptions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 105 | should_default_minimum_level | Unit | Default value |
| 106 | should_configure_output_template | Unit | Template setting |

---

## 7. Headless.Media.Indexing

### 7.1 PdfMediaFileTextProvider Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 107 | should_extract_text_from_pdf | Integration | PDF text extraction |
| 108 | should_handle_empty_pdf | Integration | Empty document |
| 109 | should_handle_multi_page_pdf | Integration | Multiple pages |

### 7.2 WordDocumentMediaFileTextProvider Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 110 | should_extract_text_from_docx | Integration | Word text extraction |
| 111 | should_handle_empty_docx | Integration | Empty document |

### 7.3 PresentationDocumentMediaFileTextProvider Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 112 | should_extract_text_from_pptx | Integration | PowerPoint text extraction |
| 113 | should_handle_empty_pptx | Integration | Empty document |

### 7.4 IMediaFileTextProvider Interface Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 114 | should_report_supported_extensions | Unit | SupportedExtensions |

---

## Summary

| Package | Unit Tests | Integration Tests | Total |
|---------|------------|-------------------|-------|
| Headless.Redis | 5 | 24 | 29 |
| Headless.Recaptcha | 17 | 3 | 20 |
| Headless.Sitemaps | 22 | 0 | 22 |
| Headless.Slugs | 20 | 0 | 20 |
| Headless.NetTopologySuite | 3 | 0 | 3 |
| Headless.Logging.Serilog | 7 | 0 | 7 |
| Headless.Media.Indexing | 2 | 6 | 8 |
| **Total** | **76** | **33** | **109** |

### Test Distribution
- **Unit tests**: 76 (mock-based)
- **Integration tests**: 33 (requires Redis/real files)
- **Existing tests**: 0
- **Missing tests**: 109 (all tests new)

### Test Project Structure
```
tests/
├── Headless.Redis.Tests.Integration/           (NEW - 24 tests)
│   ├── HeadlessRedisScriptsLoaderTests.cs
│   └── ConnectionMultiplexerExtensionsTests.cs
├── Headless.Redis.Tests.Unit/                  (NEW - 5 tests)
│   └── RedisScriptsTests.cs
├── Headless.Recaptcha.Tests.Unit/              (NEW - 20 tests)
│   ├── V2/ReCaptchaSiteVerifyV2Tests.cs
│   ├── V3/ReCaptchaSiteVerifyV3Tests.cs
│   └── TagHelpers/TagHelperTests.cs
├── Headless.Sitemaps.Tests.Unit/               (NEW - 22 tests)
│   ├── SitemapIndexBuilderTests.cs
│   ├── SitemapUrlTests.cs
│   └── SitemapUrlsTests.cs
├── Headless.Slugs.Tests.Unit/                  (NEW - 20 tests)
│   └── SlugTests.cs
├── Headless.NetTopologySuite.Tests.Unit/       (NEW - 3 tests)
│   └── GeoExtensionsTests.cs
├── Headless.Logging.Serilog.Tests.Unit/        (NEW - 7 tests)
│   └── SerilogFactoryTests.cs
└── Headless.Media.Indexing.Tests.Integration/  (NEW - 8 tests)
    └── MediaFileTextProviderTests.cs
```

### Key Testing Considerations

1. **Redis Integration**: Tests require a running Redis instance. Use Testcontainers for CI.

2. **Lua Script Syntax**: Unit tests can verify Lua syntax by attempting to prepare scripts without execution.

3. **reCAPTCHA Mocking**: Use mock HTTP handlers to simulate Google's verify endpoint responses.

4. **Sitemap XML**: Tests should verify XML validity and namespace declarations.

5. **Slug Edge Cases**: Unicode handling, RTL text, and multi-byte characters need thorough testing.

6. **Document Extraction**: Integration tests need sample PDF, DOCX, and PPTX files in test resources.
