using Demo.Models;
using Headless.Abstractions;
using Headless.Api;
using Headless.Permissions;
using Headless.Permissions.Definitions;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add permissions management core
// Note: In a real application, you would also call:
// builder.Services.AddPermissionsManagementDbContextStorage(...)
// to configure the database storage
builder.Services.AddPermissionsManagementCore();

// Configure OpenAPI documentation
builder.Services.AddHeadlessNswagOpenApi();

// Configure authorization policies
builder.Services.AddAuthentication();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        "PermissionsManage",
        policy =>
            policy.RequireAssertion(context =>
                context.User.Identity?.IsAuthenticated == true
                && (context.User.IsInRole("Admin") || context.User.IsInRole("PermissionsAdmin"))
            )
    );
});

var app = builder.Build();

// Configure middleware pipeline
app.UseAuthentication();
app.UseAuthorization();
app.MapHeadlessNswagOpenApi();

// List all permission definitions
app.MapGet(
        "/api/permissions",
        async (IPermissionDefinitionManager definitionManager, CancellationToken ct) =>
        {
            var permissions = await definitionManager.GetPermissionsAsync(ct);
            return Results.Ok(permissions);
        }
    )
    .RequireAuthorization()
    .WithSummary("List all permission definitions")
    .WithDescription("Retrieves all registered permission definitions in the system")
    .Produces<PermissionDefinition[]>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized);

// Get single permission definition
app.MapGet(
        "/api/permissions/{name}",
        async (string name, IPermissionDefinitionManager definitionManager, CancellationToken ct) =>
        {
            var permission = await definitionManager.FindAsync(name, ct);
            return permission is null
                ? Results.NotFound(
                    new
                    {
                        type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                        title = "Not Found",
                        status = 404,
                        detail = $"Permission '{name}' not found",
                    }
                )
                : Results.Ok(permission);
        }
    )
    .RequireAuthorization()
    .WithSummary("Get a permission definition by name")
    .WithDescription("Retrieves a specific permission definition by its unique name")
    .Produces<PermissionDefinition>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status401Unauthorized);

// Check permissions for current user
app.MapGet(
        "/api/permissions/check",
        async (
            [FromQuery] string[] names,
            IPermissionManager permissionManager,
            ICurrentUser currentUser,
            CancellationToken ct
        ) =>
        {
            if (names.Length == 0)
            {
                return Results.BadRequest(
                    new
                    {
                        type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                        title = "Bad Request",
                        status = 400,
                        detail = "At least one permission name is required",
                    }
                );
            }

            var results = await permissionManager.GetAllAsync(names, currentUser, cancellationToken: ct);
            return Results.Ok(results);
        }
    )
    .RequireAuthorization()
    .WithSummary("Check if user has permissions")
    .WithDescription("Verifies whether the authenticated user has one or more specified permissions")
    .Produces<GrantedPermissionResult[]>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized);

// Grant permission
app.MapPost(
        "/api/permissions/grants",
        async ([FromBody] GrantPermissionRequest request, IPermissionManager permissionManager, CancellationToken ct) =>
        {
            await permissionManager.SetAsync(
                request.Name,
                request.ProviderName,
                request.ProviderKey,
                isGranted: true,
                ct
            );

            return Results.NoContent();
        }
    )
    .RequireAuthorization("PermissionsManage")
    .WithSummary("Grant a permission")
    .WithDescription(
        "Grants a permission to a specific provider (User, Role, etc.). Requires PermissionsManage policy."
    )
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

// Revoke permission
app.MapDelete(
        "/api/permissions/grants",
        async (
            [FromQuery] string name,
            [FromQuery] string providerName,
            [FromQuery] string providerKey,
            IPermissionManager permissionManager,
            CancellationToken ct
        ) =>
        {
            if (
                string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(providerName)
                || string.IsNullOrWhiteSpace(providerKey)
            )
            {
                return Results.BadRequest(
                    new
                    {
                        type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                        title = "Bad Request",
                        status = 400,
                        detail = "name, providerName, and providerKey are required",
                    }
                );
            }

            await permissionManager.SetAsync(name, providerName, providerKey, isGranted: false, ct);
            return Results.NoContent();
        }
    )
    .RequireAuthorization("PermissionsManage")
    .WithSummary("Revoke a permission")
    .WithDescription("Revokes a specific permission from a provider. Requires PermissionsManage policy.")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

// Revoke all permissions for provider
app.MapDelete(
        "/api/permissions/grants/{providerName}/{providerKey}",
        async (string providerName, string providerKey, IPermissionManager permissionManager, CancellationToken ct) =>
        {
            await permissionManager.DeleteAsync(providerName, providerKey, ct);
            return Results.NoContent();
        }
    )
    .RequireAuthorization("PermissionsManage")
    .WithSummary("Revoke all permissions for a provider")
    .WithDescription("Deletes all permission grants for a specific provider. Requires PermissionsManage policy.")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

await app.RunAsync();
