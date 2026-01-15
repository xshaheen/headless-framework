# Add HTTP API Endpoints for Permission Management

## Implementation Status

**✅ COMPLETED** - Implemented as demo/sample project at `demo/Framework.Permissions.Api.Demo/`

**Decision:** Created demo/documentation instead of framework package to avoid violating framework's unopinionated design (would be first domain-specific API package).

**Demo Location:** `/demo/Framework.Permissions.Api.Demo/`
- `Controllers/PermissionsController.cs` - HTTP API endpoints
- `Models/GrantPermissionRequest.cs` - Request DTO
- `Program.cs` - Authorization policy configuration
- `README.md` - Usage documentation

## Overview

Create `Framework.Permissions.Api` package to expose permission management operations via REST endpoints. Currently, permission system provides clean programmatic interfaces (`IPermissionManager`, `IPermissionDefinitionManager`) but no HTTP API, preventing external agents, webhooks, and cross-service calls from managing permissions.

## Problem Statement

**Current State:**
- Permission operations only accessible via direct service injection
- No REST endpoints for permission checks, grants, or revokes
- External systems cannot query or modify permissions without custom controllers

**Impact:**
- Agent-native architecture blocked (agents can't manage permissions via HTTP)
- Webhooks can't respond to events by modifying permissions
- Microservices must use shared libraries instead of HTTP calls
- Frontends require BFF layer for permission operations

**References:**
- `src/Framework.Permissions.Abstractions/Grants/IPermissionManager.cs` - Core operations interface
- `src/Framework.Permissions.Abstractions/Definitions/IPermissionDefinitionManager.cs` - Definition queries
- `todos/016-ready-p3-no-http-api.md` - Original finding from Agent-Native review

## Proposed Solution

Create new NuGet package `Framework.Permissions.Api` following framework's abstraction + provider pattern. Package will contain ASP.NET Core MVC controllers extending `ApiControllerBase` with endpoints for:

1. **Permission Definitions** - List and query permission metadata
2. **Permission Checks** - Verify current user's permissions
3. **Permission Grants** - Admin operations to grant permissions
4. **Permission Revokes** - Admin operations to revoke permissions

**Architecture:**
```
┌─────────────────────────────────┐
│  Framework.Permissions.Api      │
│  ┌───────────────────────────┐  │
│  │ PermissionsController     │  │
│  │ - GetDefinitions()        │  │
│  │ - GetDefinition(name)     │  │
│  │ - CheckPermissions()      │  │
│  │ - GrantPermission()       │  │
│  │ - RevokePermission()      │  │
│  └───────────┬───────────────┘  │
│              │ uses             │
│  ┌───────────▼───────────────┐  │
│  │ DTOs & Request Models     │  │
│  │ - PermissionDefinitionDto │  │
│  │ - GrantPermissionRequest  │  │
│  │ - CheckPermissionResponse │  │
│  └───────────────────────────┘  │
└──────────┬──────────────────────┘
           │ depends on
┌──────────▼──────────────────────┐
│ Framework.Permissions.Core      │
│ - IPermissionManager            │
│ - IPermissionDefinitionManager  │
└─────────────────────────────────┘
```

## Technical Approach

### Package Structure

```
src/Framework.Permissions.Api/
├── Controllers/
│   └── PermissionsController.cs
├── Models/
│   ├── PermissionDefinitionDto.cs
│   ├── PermissionGroupDto.cs
│   ├── GrantedPermissionDto.cs
│   ├── GrantPermissionRequest.cs
│   └── RevokePermissionRequest.cs
├── Setup.cs
├── Framework.Permissions.Api.csproj
└── README.md
```

### Endpoint Design

**READ Operations (Authentication Required):**

```http
GET /api/permissions
GET /api/permissions/{name}
GET /api/permissions/groups
GET /api/permissions/check?names=Orders.View&names=Orders.Edit
```

**WRITE Operations (Admin Authorization Required):**

```http
POST /api/permissions/grants
DELETE /api/permissions/grants
DELETE /api/permissions/grants/{providerName}/{providerKey}
```

### Controller Implementation

**Base Structure:**
```csharp
namespace Framework.Permissions.Api.Controllers;

[ApiController]
[Route("api/permissions")]
public sealed class PermissionsController(
    IPermissionManager permissionManager,
    IPermissionDefinitionManager definitionManager,
    ICurrentUser currentUser
) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetDefinitionsAsync(CancellationToken ct)
    {
        var groups = await definitionManager.GetGroupsAsync(ct);
        var dtos = groups.SelectMany(g => g.Permissions).Select(ToDto);
        return Ok(dtos);
    }

    [HttpGet("{name}")]
    public async Task<IActionResult> GetDefinitionAsync(string name, CancellationToken ct)
    {
        var definition = await definitionManager.FindAsync(name, ct);
        return definition is null
            ? NotFoundProblemDetails($"Permission '{name}' not found")
            : Ok(ToDto(definition));
    }

    [HttpGet("check")]
    public async Task<IActionResult> CheckPermissionsAsync(
        [FromQuery] string[] names,
        CancellationToken ct)
    {
        if (names.Length == 0)
            return MalformedSyntax("At least one permission name required");

        var results = await permissionManager.GetAllAsync(
            names,
            currentUser.ProviderName,
            currentUser.ProviderKey,
            ct);

        return Ok(results.Select(ToDto));
    }

    [HttpPost("grants")]
    [Authorize(Policy = "PermissionsManage")]
    public async Task<IActionResult> GrantPermissionAsync(
        GrantPermissionRequest request,
        CancellationToken ct)
    {
        await permissionManager.SetAsync(
            request.PermissionName,
            request.ProviderName,
            request.ProviderKey,
            isGranted: true,
            ct);

        return NoContent();
    }

    [HttpDelete("grants")]
    [Authorize(Policy = "PermissionsManage")]
    public async Task<IActionResult> RevokePermissionAsync(
        [FromQuery] string permissionName,
        [FromQuery] string providerName,
        [FromQuery] string providerKey,
        CancellationToken ct)
    {
        await permissionManager.SetAsync(
            permissionName,
            providerName,
            providerKey,
            isGranted: false,
            ct);

        return NoContent();
    }

    [HttpDelete("grants/{providerName}/{providerKey}")]
    [Authorize(Policy = "PermissionsManage")]
    public async Task<IActionResult> DeleteAllGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken ct)
    {
        await permissionManager.DeleteAsync(providerName, providerKey, ct);
        return NoContent();
    }

    private static PermissionDefinitionDto ToDto(PermissionDefinition def) => new()
    {
        Name = def.Name,
        DisplayName = def.DisplayName,
        IsEnabled = def.IsEnabled,
        ParentName = def.Parent?.Name,
        GroupName = def.Group.Name,
        AllowedProviders = def.Providers.ToArray()
    };

    private static GrantedPermissionDto ToDto(GrantedPermissionResult result) => new()
    {
        Name = result.Name,
        IsGranted = result.IsGranted,
        Providers = result.Providers.Select(p => new ProviderDto
        {
            Name = p.Name,
            Keys = p.Keys.ToArray()
        }).ToArray()
    };
}
```

### DTOs

**PermissionDefinitionDto.cs:**
```csharp
namespace Framework.Permissions.Api.Models;

public sealed class PermissionDefinitionDto
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsEnabled { get; init; }
    public string? ParentName { get; init; }
    public required string GroupName { get; init; }
    public required string[] AllowedProviders { get; init; }
}
```

**GrantedPermissionDto.cs:**
```csharp
namespace Framework.Permissions.Api.Models;

public sealed class GrantedPermissionDto
{
    public required string Name { get; init; }
    public required bool IsGranted { get; init; }
    public required ProviderDto[] Providers { get; init; }
}

public sealed class ProviderDto
{
    public required string Name { get; init; }
    public required string[] Keys { get; init; }
}
```

**GrantPermissionRequest.cs:**
```csharp
namespace Framework.Permissions.Api.Models;

public sealed class GrantPermissionRequest
{
    [Required]
    public required string PermissionName { get; init; }

    [Required]
    public required string ProviderName { get; init; }

    [Required]
    public required string ProviderKey { get; init; }
}
```

### Service Registration

**Setup.cs:**
```csharp
namespace Framework.Permissions.Api;

public static class Setup
{
    public static IServiceCollection AddPermissionsApi(
        this IServiceCollection services)
    {
        // Controllers already added by Framework.Api.Mvc
        // Just register authorization policy
        services.AddAuthorizationBuilder()
            .AddPolicy("PermissionsManage", policy =>
                policy.RequireClaim("Permission", "Permissions.Manage"));

        return services;
    }
}
```

**Usage in Program.cs:**
```csharp
builder.Services
    .AddPermissionsManagementCore()
    .AddPermissionsManagementDbContextStorage<AppDbContext>()
    .AddPermissionsApi();

builder.AddHeadlessApi().ConfigureHeadlessMvc();
builder.Services.AddControllers();

app.MapControllers();
```

### Dependencies

**Framework.Permissions.Api.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Framework.Permissions.Api</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Framework.Permissions.Core\Framework.Permissions.Core.csproj" />
    <ProjectReference Include="..\Framework.Api.Mvc\Framework.Api.Mvc.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Core" />
  </ItemGroup>
</Project>
```

## Acceptance Criteria

### Functional Requirements

- [ ] GET /api/permissions returns all permission definitions with group, parent, enabled status
- [ ] GET /api/permissions/{name} returns single definition or 404
- [ ] GET /api/permissions/groups returns permission groups with nested permissions
- [ ] GET /api/permissions/check validates names parameter and returns 400 if empty
- [ ] GET /api/permissions/check returns permission status for current authenticated user
- [ ] GET /api/permissions/check returns 401 for unauthenticated users
- [ ] POST /api/permissions/grants requires "Permissions.Manage" authorization
- [ ] POST /api/permissions/grants validates request body and returns 400 for invalid data
- [ ] POST /api/permissions/grants returns 409 if permission doesn't exist or is disabled
- [ ] POST /api/permissions/grants returns 409 if provider not allowed for permission
- [ ] POST /api/permissions/grants invalidates cache after successful grant
- [ ] DELETE /api/permissions/grants sets explicit deny (isGranted=false)
- [ ] DELETE /api/permissions/grants/{providerName}/{providerKey} removes all grants for provider
- [ ] All endpoints use RFC 7807 Problem Details for errors

### Non-Functional Requirements

- [ ] All endpoints async with CancellationToken support
- [ ] OpenAPI/Swagger documentation generated with XML comments
- [ ] Response times <100ms for permission checks (leveraging cache)
- [ ] Idempotent grant/revoke operations (204 No Content)
- [ ] URL-encoded permission names with dots handled correctly (Orders.View)
- [ ] Maximum 100 permission names per check request (validation)

### Quality Gates

- [ ] Unit tests for all controller actions in Framework.Permissions.Api.Tests.Unit
- [ ] Integration tests with WebApplicationFactory covering auth/authz scenarios
- [ ] Test coverage: ≥85% line, ≥80% branch
- [ ] XML doc comments on all public types and endpoints
- [ ] README.md with usage examples and endpoint documentation
- [ ] CSharpier formatted, passes Roslyn analyzers

## Implementation Phases

### Phase 1: Package Setup
- Create Framework.Permissions.Api project
- Add dependencies (Framework.Permissions.Core, Framework.Api.Mvc)
- Create Setup.cs with authorization policy registration
- Add README.md with package overview

### Phase 2: DTOs & Models
- Create PermissionDefinitionDto
- Create PermissionGroupDto
- Create GrantedPermissionDto with ProviderDto
- Create GrantPermissionRequest with validation attributes
- Add XML doc comments

### Phase 3: Controller Implementation
- Create PermissionsController extending ApiControllerBase
- Implement GET /api/permissions (list definitions)
- Implement GET /api/permissions/{name} (single definition)
- Implement GET /api/permissions/groups (permission groups)
- Implement GET /api/permissions/check (current user permissions)
- Add error handling with Problem Details

### Phase 4: Admin Endpoints
- Implement POST /api/permissions/grants with authorization
- Implement DELETE /api/permissions/grants with authorization
- Implement DELETE /api/permissions/grants/{providerName}/{providerKey}
- Add validation and error responses

### Phase 5: Testing
- Create Framework.Permissions.Api.Tests.Unit project
- Write unit tests for controller actions
- Create integration tests with WebApplicationFactory
- Test authorization policies (anonymous, authenticated, admin)
- Test error scenarios (404, 400, 409, 403)
- Verify cache invalidation on grants/revokes

### Phase 6: Documentation
- Add XML doc comments to all endpoints
- Update README.md with endpoint examples
- Document authorization requirements
- Add OpenAPI annotations for better Swagger UI
- Update root README.md to list new package

## Security Considerations

**Authorization:**
- Read endpoints require authentication (ICurrentUser must exist)
- Write endpoints require "Permissions.Manage" claim
- Prevent privilege escalation (users can't grant to themselves without permission)

**Input Validation:**
- Validate permission names against definitions (return 409 for non-existent)
- Validate provider names (only "User", "Role" allowed initially)
- Validate provider keys (non-empty, max length)
- Limit check request to 100 permission names (prevent URL length abuse)

**Error Information Disclosure:**
- Use 404 for non-existent permissions in GET (safe information)
- Use 409 for invalid permissions in POST (admin-only, safe to disclose)
- Don't leak internal exception details in Production

**Audit Logging:**
- Grant/revoke operations already logged via PermissionManager events
- Cache invalidation publishes local events for subscribers

## Alternative Approaches Considered

### Option B: Minimal API Instead of Controllers

**Pros:**
- Lighter weight, better performance
- Simpler registration
- Modern .NET 10 approach

**Cons:**
- No ApiControllerBase benefits (lazy services, problem details helpers)
- Requires separate endpoint classes for organization
- Less familiar pattern in framework (all existing API packages use controllers)

**Decision:** Use Controllers for consistency with Framework.Api.Mvc pattern

### Option C: Separate Read/Write Packages

**Pros:**
- CQRS separation
- Read package could be public, write package admin-only
- Smaller dependencies

**Cons:**
- Over-engineering for simple CRUD
- Two packages to maintain
- Violates YAGNI

**Decision:** Single package with authorization on write endpoints

### Option D: Document Implementation Guide Only

**Pros:**
- No framework change
- Each app customizes

**Cons:**
- Inconsistent implementations
- Every app re-implements same endpoints
- Not agent-native (agents can't use without custom controllers)

**Decision:** Provide standardized package (Option A)

## Dependencies & Risks

**Dependencies:**
- Framework.Permissions.Core must be configured with ICurrentUser implementation
- Application must configure authentication middleware before authorization
- Controllers require AddControllers() and MapControllers() in Program.cs

**Risks:**

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| ICurrentUser not configured | Medium | High | Document requirement, add runtime check |
| Cache invalidation lag | Low | Medium | Already handled by PermissionManager events |
| URL length limits (check) | Low | Low | Validate max 100 names, document limit |
| Provider extensibility | Low | Medium | Accept any providerName, PermissionManager validates |

## Open Questions

### Critical (Must Answer Before Implementation)

1. **Batch Operations:** Should grant/revoke support array of permissions in single request?
   - **Recommendation:** No, keep atomic; clients can parallelize if needed
   - **Reason:** Simpler error handling, transaction semantics unclear for partial failures

2. **Check Endpoint for Non-Existent Permissions:** Return 404 or isGranted=false?
   - **Recommendation:** Return 200 with isGranted=false
   - **Reason:** Consistent with disabled permissions, less client error handling

3. **Revoke Semantics:** DELETE removes grant record or sets explicit deny?
   - **Recommendation:** DELETE sets isGranted=false (explicit deny per AWS IAM pattern)
   - **Reason:** Matches PermissionManager.SetAsync(false) behavior

4. **Anonymous Access to Definitions:** Allow GET /api/permissions without auth?
   - **Recommendation:** Require authentication
   - **Reason:** Permission structure is system metadata, should not be public

### Important (Can Decide During Implementation)

5. **Pagination:** Add pagination to GET /api/permissions?
   - **Recommendation:** Not initially; optimize if >500 permissions
   - **Reason:** Most systems have <100 permissions, YAGNI

6. **Response Format:** Use detailed GrantedPermissionResult or simplified?
   - **Recommendation:** Keep detailed format with providers array
   - **Reason:** Useful for debugging, shows grant source (User vs Role)

7. **Error Codes:** Define custom error codes in PermissionApiErrors?
   - **Recommendation:** Use existing ConflictException, ValidationException
   - **Reason:** Reuse framework error handling infrastructure

## Success Metrics

**Adoption:**
- At least 2 demo projects using Framework.Permissions.Api
- Package downloaded from internal NuGet feed

**Functionality:**
- All acceptance criteria met
- Zero security vulnerabilities in review
- API usable by external agents without custom code

**Quality:**
- Test coverage ≥85% line, ≥80% branch
- All integration tests pass across Development/Production environments
- CSharpier + Roslyn analyzers pass

## References & Research

### Internal Code References

**Permission System:**
- `src/Framework.Permissions.Abstractions/Grants/IPermissionManager.cs:15-35` - Core operations
- `src/Framework.Permissions.Abstractions/Definitions/IPermissionDefinitionManager.cs:10-25` - Definition queries
- `src/Framework.Permissions.Core/Grants/PermissionManager.cs:42-280` - Implementation with AWS IAM-style deny
- `src/Framework.Permissions.Core/Setup.cs:10-45` - Service registration pattern

**API Infrastructure:**
- `src/Framework.Api.Mvc/Controllers/ApiControllerBase.cs:15-80` - Base controller with helpers
- `src/Framework.Api/ProblemDetails/IProblemDetailsCreator.cs` - Error response creation
- `tests/Framework.Api.Tests.Integration/ProblemDetailsTests.cs:20-150` - Integration test pattern

**Similar Features:**
- `src/Framework.Features.Core/` - Similar definition + value pattern
- `src/Framework.Settings.Core/` - Similar hierarchical resolution

### External Documentation

**ASP.NET Core Best Practices:**
- [ASP.NET Core Best Practices | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/best-practices?view=aspnetcore-10.0)
- [REST API Best Practices | Code Maze](https://code-maze.com/aspnetcore-webapi-best-practices/)

**Permission API Patterns:**
- [Keycloak Authorization Services](https://www.keycloak.org/docs/latest/authorization_services/) - Industry pattern for permission APIs
- [Choosing Error Codes 401, 403, 404 | Authress](https://authress.io/knowledge-base/articles/choosing-the-right-http-error-code-401-403-404)

**OpenAPI & .NET 10:**
- [OpenAPI in ASP.NET Core 10 | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0)
- [Policy-Based Authorization | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0)

### Project Conventions

**Code Style (CLAUDE.md):**
- File-scoped namespaces
- Primary constructors for DI
- `sealed` by default
- `required`/`init` properties
- Collection expressions `[]`

**Package Management:**
- All versions in Directory.Packages.props
- Never add Version attribute in .csproj

**Testing:**
- xUnit + AwesomeAssertions + NSubstitute
- Integration tests use Testcontainers (Docker required)

## Related Issues

- `todos/016-ready-p3-no-http-api.md` - This todo (Agent-Native review finding)
- Future: Add Minimal API version in Framework.Permissions.Api.MinimalApi
- Future: Add permission audit log endpoint
- Future: Add SignalR hub for real-time permission change notifications
