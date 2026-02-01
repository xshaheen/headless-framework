# Framework.OpenApi Test Design

## Overview

The Framework.OpenApi packages provide OpenAPI documentation generation for ASP.NET Core APIs using NSwag, with additional support for Scalar UI and OData query parameters. Features include FluentValidation schema processing, ProblemDetails examples, and building blocks primitive mappings.

### Packages
1. **Framework.OpenApi.Nswag** - NSwag-based OpenAPI generation with FluentValidation integration
2. **Framework.OpenApi.Nswag.OData** - OData query parameter documentation
3. **Framework.OpenApi.Scalar** - Scalar UI integration for OpenAPI

### Key Components
- **NswagSetup** - DI registration and middleware configuration
- **FluentValidationSchemaProcessor** - Maps FluentValidation rules to OpenAPI schemas
- **ProblemDetailsOperationProcessor** - Adds ProblemDetails examples to responses
- **ODataOperationFilter** - Adds $select, $filter, $expand parameters
- **ScalarSetup** - Configures Scalar API reference UI

### Existing Tests
**No existing tests found**

---

## 1. Framework.OpenApi.Nswag

### 1.1 NswagSetup - AddHeadlessNswagOpenApi Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 1 | should_register_openapi_document | Unit | Services registered |
| 2 | should_configure_default_settings | Unit | Title, description defaults |
| 3 | should_invoke_setup_action | Unit | Custom configuration |
| 4 | should_set_title_from_assembly | Unit | AssemblyInformation.Entry.Product |
| 5 | should_use_route_name_as_operation_id | Unit | UseRouteNameAsOperationId = true |
| 6 | should_flatten_inheritance_hierarchy | Unit | FlattenInheritanceHierarchy = true |
| 7 | should_add_schema_processors | Unit | FluentValidation, Nullability |
| 8 | should_add_operation_processors | Unit | ProblemDetails, Auth processors |

### 1.2 NswagSetup - Security Configuration Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 9 | should_add_bearer_security_when_enabled | Unit | AddBearerSecurity option |
| 10 | should_not_add_bearer_security_when_disabled | Unit | Default off |
| 11 | should_add_api_key_security_when_enabled | Unit | AddApiKeySecurity option |
| 12 | should_use_custom_api_key_header_name | Unit | ApiKeyHeaderName option |
| 13 | should_add_security_scope_processor | Unit | AspNetCoreOperationSecurityScopeProcessor |

### 1.3 NswagSetup - AddBuildingBlocksPrimitiveMappings Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 14 | should_map_money_to_decimal | Unit | Money → Number/Decimal |
| 15 | should_map_nullable_money | Unit | Money? with IsNullableRaw |
| 16 | should_map_month_to_integer | Unit | Month → Integer |
| 17 | should_map_nullable_month | Unit | Month? with IsNullableRaw |
| 18 | should_map_account_id_to_string | Unit | AccountId → String |
| 19 | should_map_user_id_to_string | Unit | UserId → String |

### 1.4 NswagSetup - MapFrameworkNswagOpenApiVersions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 20 | should_map_openapi_endpoint_per_version | Integration | Multiple version documents |
| 21 | should_order_versions_descending | Integration | Latest version first |
| 22 | should_configure_swagger_ui | Integration | SwaggerUiSettings applied |
| 23 | should_set_document_path | Integration | /openapi/{groupName}.json |

### 1.5 NswagSetup - MapFrameworkNswagOpenApi Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 24 | should_map_openapi_endpoint | Integration | Single document |
| 25 | should_configure_swagger_ui_defaults | Integration | PersistAuthorization, TagsSorter |
| 26 | should_invoke_document_settings | Integration | Custom settings callback |
| 27 | should_invoke_ui_settings | Integration | Custom UI callback |

### 1.6 FluentValidationSchemaProcessor Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 28 | should_skip_non_object_schemas | Unit | Bypasses simple types |
| 29 | should_resolve_validator_from_di | Unit | IValidator<T> resolution |
| 30 | should_apply_not_empty_rule | Unit | Sets minimum length |
| 31 | should_apply_not_null_rule | Unit | Sets required |
| 32 | should_apply_max_length_rule | Unit | Sets maxLength |
| 33 | should_apply_min_length_rule | Unit | Sets minLength |
| 34 | should_apply_length_range_rule | Unit | Both min/max |
| 35 | should_apply_email_rule | Unit | Sets format: email |
| 36 | should_apply_regex_rule | Unit | Sets pattern |
| 37 | should_apply_greater_than_rule | Unit | Sets minimum exclusive |
| 38 | should_apply_less_than_rule | Unit | Sets maximum exclusive |
| 39 | should_apply_between_rule | Unit | Sets range |
| 40 | should_apply_inclusive_between_rule | Unit | Inclusive range |
| 41 | should_handle_include_rules | Unit | Nested validators |
| 42 | should_log_error_on_failure | Unit | Logger called |
| 43 | should_throw_when_configured | Unit | ThrowOnSchemaProcessingError |
| 44 | should_handle_property_context | Unit | Non-object property handling |
| 45 | should_use_custom_rules | Unit | Custom FluentValidationRule |
| 46 | should_replace_default_rules | Unit | Rule override |

### 1.7 NullabilityAsRequiredSchemaProcessor Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 47 | should_mark_non_nullable_as_required | Unit | Required property detection |
| 48 | should_not_mark_nullable_as_required | Unit | Nullable property handling |

### 1.8 ProblemDetailsOperationProcessor Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 49 | should_process_operation | Unit | Returns true |
| 50 | should_register_schema_definitions | Unit | All ProblemDetails types |
| 51 | should_set_400_example | Unit | BadRequestProblemDetails |
| 52 | should_set_401_example | Unit | UnauthorizedProblemDetails |
| 53 | should_set_403_example | Unit | ForbiddenProblemDetails |
| 54 | should_set_404_example | Unit | EntityNotFoundProblemDetails |
| 55 | should_set_409_example | Unit | ConflictProblemDetails |
| 56 | should_set_422_example | Unit | UnprocessableEntityProblemDetails |
| 57 | should_set_429_example | Unit | TooManyRequestsProblemDetails |
| 58 | should_use_schema_reference | Unit | $ref to definitions |
| 59 | should_set_problem_json_content_type | Unit | application/problem+json |

### 1.9 UnauthorizedResponseOperationProcessor Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 60 | should_add_401_response_when_authorized | Unit | [Authorize] detection |
| 61 | should_skip_anonymous_endpoints | Unit | [AllowAnonymous] |

### 1.10 ForbiddenResponseOperationProcessor Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 62 | should_add_403_response_when_authorized | Unit | Policy-based auth |
| 63 | should_skip_anonymous_endpoints | Unit | [AllowAnonymous] |

### 1.11 ApiExtraInformationOperationProcessor Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 64 | should_process_extra_information | Unit | Custom metadata |

### 1.12 HeadlessNswagOptions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 65 | should_default_add_bearer_security_false | Unit | Default value |
| 66 | should_default_add_api_key_security_false | Unit | Default value |
| 67 | should_default_add_primitive_mappings_true | Unit | Default value |
| 68 | should_default_throw_on_error_false | Unit | Default value |

### 1.13 ProblemDetails Models Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 69 | should_serialize_bad_request_problem_details | Unit | JSON serialization |
| 70 | should_serialize_conflict_problem_details | Unit | With Errors array |
| 71 | should_serialize_unprocessable_entity | Unit | With Errors dictionary |
| 72 | should_include_trace_id | Unit | TraceId property |
| 73 | should_include_timestamp | Unit | Timestamp property |

### 1.14 SwaggerInformation Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 74 | should_provide_responses_description | Unit | Markdown content |

---

## 2. Framework.OpenApi.Nswag.OData

### 2.1 ODataOperationFilter Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 75 | should_add_select_parameter | Unit | $select query param |
| 76 | should_add_expand_parameter | Unit | $expand query param |
| 77 | should_add_filter_parameter | Unit | $filter query param |
| 78 | should_add_search_parameter | Unit | $search query param |
| 79 | should_add_top_parameter | Unit | $top query param |
| 80 | should_add_skip_parameter | Unit | $skip query param |
| 81 | should_add_orderby_parameter | Unit | $orderby query param |
| 82 | should_detect_enable_query_attribute | Unit | [EnableQuery] on method |
| 83 | should_detect_enable_query_on_class | Unit | [EnableQuery] on type |
| 84 | should_detect_odata_query_options_parameter | Unit | ODataQueryOptions<T> |
| 85 | should_remove_odata_query_options_from_params | Unit | Removes redundant param |
| 86 | should_skip_non_odata_endpoints | Unit | No modification |
| 87 | should_set_correct_schema_types | Unit | String vs Integer |
| 88 | should_mark_parameters_as_optional | Unit | IsRequired = false |

---

## 3. Framework.OpenApi.Scalar

### 3.1 ScalarSetup - MapFrameworkScalarOpenApi Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 89 | should_map_scalar_endpoint | Integration | Scalar UI accessible |
| 90 | should_configure_default_route_pattern | Integration | /openapi/{documentName}.json |
| 91 | should_enable_dark_mode_by_default | Unit | DarkMode = true |
| 92 | should_use_modern_layout | Unit | Layout.Modern |
| 93 | should_sort_tags_alphabetically | Unit | TagSorter.Alpha |
| 94 | should_sort_operations_by_method | Unit | OperationSorter.Method |
| 95 | should_configure_enabled_targets | Unit | CSharp, Go, JavaScript, etc. |
| 96 | should_configure_enabled_clients | Unit | HttpClient, Curl, Axios, etc. |
| 97 | should_invoke_setup_action | Integration | Custom configuration |
| 98 | should_use_custom_endpoint_prefix | Integration | Custom prefix parameter |

---

## 4. Integration Tests

### 4.1 Full OpenAPI Generation Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 99 | should_generate_valid_openapi_document | Integration | Full document |
| 100 | should_include_fluent_validation_constraints | Integration | Schema constraints |
| 101 | should_include_problem_details_schemas | Integration | Error schemas |
| 102 | should_include_odata_parameters | Integration | OData endpoints |
| 103 | should_include_security_definitions | Integration | Auth schemes |
| 104 | should_generate_versioned_documents | Integration | Multi-version |

---

## Summary

| Category | Unit Tests | Integration Tests | Total |
|----------|------------|-------------------|-------|
| Nswag Setup | 8 | 8 | 16 |
| Security Configuration | 5 | 0 | 5 |
| Primitive Mappings | 6 | 0 | 6 |
| FluentValidation Processor | 19 | 0 | 19 |
| Nullability Processor | 2 | 0 | 2 |
| ProblemDetails Processor | 11 | 0 | 11 |
| Auth Processors | 4 | 0 | 4 |
| Options/Models | 9 | 0 | 9 |
| OData Filter | 14 | 0 | 14 |
| Scalar Setup | 6 | 4 | 10 |
| Full Integration | 0 | 6 | 6 |
| **Total** | **84** | **18** | **102** |

### Test Distribution
- **Unit tests**: 84 (isolated component tests)
- **Integration tests**: 18 (full pipeline tests)
- **Existing tests**: 0
- **Missing tests**: 102 (all tests new)

### Test Project Structure
```
tests/
├── Framework.OpenApi.Nswag.Tests.Unit/              (NEW - 76 tests)
│   ├── Setup/
│   │   └── NswagSetupTests.cs
│   ├── SchemaProcessors/
│   │   ├── FluentValidationSchemaProcessorTests.cs
│   │   └── NullabilityAsRequiredSchemaProcessorTests.cs
│   ├── OperationProcessors/
│   │   ├── ProblemDetailsOperationProcessorTests.cs
│   │   ├── UnauthorizedResponseOperationProcessorTests.cs
│   │   └── ForbiddenResponseOperationProcessorTests.cs
│   └── Models/
│       └── ProblemDetailsModelsTests.cs
├── Framework.OpenApi.Nswag.OData.Tests.Unit/        (NEW - 14 tests)
│   └── ODataOperationFilterTests.cs
├── Framework.OpenApi.Scalar.Tests.Unit/             (NEW - 6 tests)
│   └── ScalarSetupTests.cs
└── Framework.OpenApi.Tests.Integration/             (NEW - 6 tests)
    └── OpenApiGenerationTests.cs
```

### Key Testing Considerations

1. **FluentValidation Integration**: Tests need a mock `IServiceProvider` with registered `IValidator<T>` implementations to verify schema processing.

2. **NSwag Context Mocking**: `SchemaProcessorContext` and `OperationProcessorContext` need careful setup with real NSwag types.

3. **Include Rules**: The `_AddRulesFromIncludedValidators` method uses reflection on `ChildValidatorAdaptor<,>` - edge cases need thorough testing.

4. **OData Detection**: Tests should verify both `[EnableQuery]` attribute detection and `ODataQueryOptions<T>` parameter detection.

5. **Schema References**: ProblemDetails tests should verify `$ref` usage in generated schemas, not inline definitions.

6. **Integration Testing**: Full pipeline tests require a minimal ASP.NET Core application with controllers/endpoints.

7. **Concurrent Schema Processing**: `ConcurrentDictionary` caching in FluentValidationSchemaProcessor needs thread-safety verification.
