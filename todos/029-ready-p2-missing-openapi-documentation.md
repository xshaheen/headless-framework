---
status: ready
priority: p2
issue_id: "029"
tags: [code-review, documentation, openapi, api-demo, agent-native]
dependencies: []
---

# Missing OpenAPI/Swagger Documentation

## Problem Statement

Demo lacks OpenAPI/Swagger configuration, preventing interactive API testing and agent discovery. For a demo showing HTTP API patterns, this is a critical missing piece that forces developers to read code instead of using interactive documentation.

**Impact:** Poor developer experience, agents cannot discover endpoints programmatically, no machine-readable API schema.

## Findings

**Location:** `demo/Framework.Permissions.Api.Demo/Program.cs`

**Missing:**
- No `AddEndpointsApiExplorer()` call
- No `AddSwaggerGen()` configuration
- No `UseSwagger()` / `UseSwaggerUI()` middleware

**Comparison:** Other demos include OpenAPI:
- Framework.Api.Demo: Uses Nswag OpenAPI
- Framework.OpenApi.Nswag.Demo: Full OpenAPI + Scalar integration

**Agent-Native Impact:**
- Agents cannot auto-generate client SDKs
- No programmatic endpoint discovery
- Missing authentication scheme documentation

**Sources:** security-sentinel, agent-native-reviewer, pattern-recognition-specialist

## Proposed Solutions

### Option A: Nswag + Scalar (Recommended for Framework Consistency)
**Pros:** Matches other framework demos, interactive UI, better than Swashbuckle
**Cons:** Additional package reference
**Effort:** Small
**Risk:** Low

```csharp
// .csproj
<PackageReference Include="Framework.OpenApi.Nswag" />

// Program.cs
builder.Services.AddHeadlessNswagOpenApi(c =>
{
    c.Title = "Permissions Management API";
    c.Description = "Demo API for Framework.Permissions";
    c.Version = "v1";
});

app.MapFrameworkNswagOpenApi();
```

### Option B: Built-in Swashbuckle
**Pros:** No additional packages, built into ASP.NET Core
**Cons:** Less feature-rich than Nswag
**Effort:** Small
**Risk:** Low

```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Permissions Management API",
        Version = "v1"
    });
});

app.UseSwagger();
app.UseSwaggerUI();
```

### Option C: Document in README Only
**Pros:** Minimal effort
**Cons:** Not interactive, not agent-friendly
**Effort:** Minimal
**Risk:** High (poor UX)

## Recommended Action

<!-- Fill during triage -->

## Technical Details

**Affected files:**
- `demo/Framework.Permissions.Api.Demo/Program.cs`
- `demo/Framework.Permissions.Api.Demo/Framework.Permissions.Api.Demo.csproj` (package reference)

**Additional improvements:**
- Add XML documentation comments to controller methods
- Add `[ProducesResponseType]` attributes
- Document authentication requirements in OpenAPI
- Include example requests/responses

**Agent-Native requirement:** OpenAPI schema enables agent tool generation

## Acceptance Criteria

- [ ] OpenAPI/Swagger UI accessible at `/swagger`
- [ ] All endpoints documented with descriptions
- [ ] Request/response schemas included
- [ ] Authentication scheme documented
- [ ] Example requests shown in UI
- [ ] README links to Swagger UI
- [ ] OpenAPI JSON downloadable for agent consumption

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-15 | Created from code review | Multiple agents identified missing documentation |
| 2026-01-15 | Resolved - Added OpenAPI/Swagger | Used Framework.OpenApi.Nswag with endpoint metadata (WithSummary/WithDescription/Produces) |

## Resources

- Agent-Native Reviewer findings
- Security Sentinel recommendations
- Pattern Recognition analysis
- Framework.OpenApi.Nswag package
