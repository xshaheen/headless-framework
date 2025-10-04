# Copilot Instructions

This is a modern headless .NET Core framework for building web/general applications.

---

## Project Overview

This is **headless-framework**, a modern, headless .NET framework designed for building web applications and services.
It provides composable NuGet packages for common functionality like APIs, databases, messaging, caching, and more.

**Key characteristics:**

* Unopinionated and modular design - use only what you need.
* Supports .NET 9.0 with preview language features enabled.
* Uses centralized package management via `Directory.Packages.props`.
* Extensive test coverage with both unit and integration tests.

---

## Architecture & Structure

The framework follows a modular architecture with clear separation of concerns.

### Source Structure (`src/`)

The framework is organized into functional domains:

* **Framework.Base** - Core abstractions, utilities, and base interfaces.
* **Framework.Api*** - Web API related packages (MVC, Minimal APIs, validation, etc.).
* **Framework.Database*** - Database providers (SQL Server, PostgreSQL, SQLite).
* **Framework.Orm*** - ORM integrations (Entity Framework, Dapper, Couchbase).
* **Framework.Messaging*** - Message queue abstractions and implementations.
* **Framework.Caching*** - Caching abstractions and providers (Redis, Memory, etc.).
* **Framework.Blobs*** - File storage abstractions (Azure, AWS S3, FileSystem, SSH, Redis).
* **Framework.Emails*** - Email service providers (AWS SES, SendGrid, MailKit, etc.).
* **Framework.Sms*** - SMS service providers (Twilio, Vonage, Infobip, etc.).
* **Framework.Testing** - Testing utilities and helpers.
* **Framework.Features*** - Feature flags and toggles.
* **Framework.Permissions*** - Permission and authorization abstractions.
* **Framework.Settings*** - Settings management abstractions.

### Test Structure (`tests/`)

* **Unit tests**: `*.Tests.Unit` or `*.Tests.Units`
    * Fast, isolated tests with mocked dependencies.
    * No external dependencies (databases, networks, etc.).
* **Integration tests**: `*.Tests.Integration` or `*.Tests.Integrations`
    * Tests with real dependencies using Testcontainers.
    * Requires Docker for execution.
* **Test harnesses**: `*.Tests.Harness`
    * Provides shared infrastructure, common fixtures, and test data builders for testing projects.

---

## Development Workflow & Commands

### Build System (NUKE)

The project uses the NUKE build system for automation. All commands must be compatible with **PowerShell Core (pwsh)**.

**PowerShell:**

```powershell
# Clean artifacts and build outputs
.\build.ps1 Clean

# Build the solution
.\build.ps1 Compile

# Run all tests
.\build.ps1 Test

# Create NuGet packages
.\build.ps1 Pack
```

**Linux/macOS (Bash):**

```bash
# Clean artifacts and build outputs
./build.sh Clean

# Build the solution
./build.sh Compile

# Run all tests
./build.sh Test

# Create NuGet packages
./build.sh Pack
```

### Common .NET CLI Commands

For faster execution, target the specific project you want to build or test instead of the entire solution.

```powershell
# Restore all NuGet packages
dotnet restore

# Build the entire solution (slower)
dotnet build

# Build a specific project (PREFERRED - much faster)
dotnet build src/Framework.Orm.EntityFramework/Framework.Orm.EntityFramework.csproj

# Run a specific test project
dotnet test tests/Framework.Base.Tests.Unit/Framework.Base.Tests.Unit.csproj

# Run a specific test by name
dotnet test --filter "FullyQualifiedName~test_method_name"

# Format code (requires dotnet-csharpier tool)
dotnet csharpier .

# Add an Entity Framework migration
dotnet ef migrations add <MigrationName> --project <ProjectPath>

# Update the database with migrations
dotnet ef database update --project <ProjectPath>
```

### Development Tools

The following tools are available via dotnet tool restore from the `.config/dotnet-tools.json` manifest:

- csharpier: An opinionated code formatter.
- dotnet-ef: The command-line tool for Entity Framework Core.
- minver: A tool for automatic semantic versioning from git history.
- husky: A tool for managing Git hooks (e.g., for pre-commit actions).

## Code Standards & Conventions

### C# Language Features & Patterns

**Modern C# is required:**

* ✅ **Nullable Reference Types (NRT)**: All code must be null-safe.
* ✅ **File-scoped namespaces**: Always use `namespace X;` instead of `namespace X { }`.
* ✅ **Primary Constructors**: Use for dependency injection and simple classes.
* ✅ **`required` keyword**: Use for mandatory properties.
* ✅ **`init` accessors**: Prefer `init` over `set` for immutable-after-construction properties.
* ✅ **Pattern matching**: Prefer modern pattern matching over old-style checks.
* ✅ **Collection expressions**: Use `[]` for collections (e.g., `int[] numbers = [1, 2, 3];`).
* ✅ **UTF-8 string literals**: Use `"text"u8` when appropriate.
* ✅ **`sealed` classes**: Prefer sealing classes by default unless inheritance is explicitly needed.

**Example:**

```csharp
// CORRECT: File-scoped namespace, primary constructor, sealed class, required/init properties
namespace Framework.MyFeature;

public sealed class MyService(IDependency dependency) : IMyService
{
    public required string ConfigValue { get; init; }

    public async Task<Result> DoSomethingAsync(string? input)
    {
        // Use pattern matching and modern C#
        return input switch
        {
            null => Result.Failure("Input required"),
            "" => Result.Failure("Input cannot be empty"),
            _ => await dependency.ProcessAsync(input)
        };
    }
}
```

### Naming Conventions (Strictly Enforced by .editorconfig)

| Element                      | Convention                               | Example                                               |
|:-----------------------------|:-----------------------------------------|:------------------------------------------------------|
| **Private Fields**           | `_camelCase`                             | `private readonly IService _service;`                 |
| **Private `const`/`static`** | `_PascalCase`                            | `private const string _DefaultValue = "default";`     |
| **Public/Protected Fields**  | **DISALLOWED** (use properties)          | N/A                                                   |
| **Public Methods**           | `PascalCase`                             | `public async Task ProcessDataAsync()`                |
| **Private Methods**          | `_PascalCase` (prefixed with underscore) | `private void _ValidateInput(string input)`           |
| **Local Functions**          | `camelCase`                              | `void logError(string msg) => _logger.LogError(msg);` |
| **Properties**               | `PascalCase`                             | `public required string ApiKey { get; init; }`        |
| **Classes/Structs/Records**  | `PascalCase`                             | `public sealed class UserService`                     |
| **Interfaces**               | `IPascalCase` (prefixed with `I`)        | `public interface IUserRepository`                    |
| **Type Parameters**          | `TPascalCase` (prefixed with `T`)        | `public class Result<TValue>`                         |
| **Parameters/Locals**        | `camelCase`                              | `string userId`, `var user = new User();`             |

## Testing Standards

- Test Naming Convention: `should_{action}_{expected_behavior}_when_{condition}`.
- Pattern: Use the Given-When-Then pattern for structuring tests.

### Testing Stack:

- xUnit: Test framework.
- FluentAssertions: Fluent assertion library.
- NSubstitute: Mocking framework.
- Bogus: Test data generator.
- DeepCloner: Deep cloning for test data.
- Testcontainers: Docker containers for integration tests.

```csharp
[Fact]
public async Task should_return_user_when_id_is_valid()
{
    // given
    var userId = "user-123";
    var expectedUser = new User { Id = userId };
    _repository.GetByIdAsync(userId).Returns(expectedUser);
    var service = new UserService(_repository);

    // when
    var result = await service.GetUserAsync(userId);

    // then
    result.Should().BeEquivalentTo(expectedUser);
}
```

## Package Management

- ✅ All package versions MUST be managed centrally in the Directory.Packages.props file.
- ❌ NEVER include a Version attribute in <PackageReference> elements within .csproj files.

## Formatting & Style (.editorconfig)

- Indentation: 4 spaces.
- Line Endings: LF (Unix-style).
- Charset: UTF-8.
- Braces: Required for multi-line statements.
- var usage: Use when the type is apparent from the right side of the assignment.

## Performance & Best Practices

- Async/Await: Always use async/await for I/O. Use `AnyContext()` which is extention that replace
  `ConfigureAwait(false)` in library code. Avoid async void, .Result, and .Wait().
- Cancellation Tokens: Always accept and pass CancellationToken in async methods.
- Logging: Use structured logging with ILogger<T> and message templates (e.g., _logger.LogInformation("Processing
  {UserId}", userId)).
- Null Safety: All code must be null-safe using NRTs. Use is not null, ?., and ?? for checks and assignments.

## Additional Resources

- [.NET 9 Documentation](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9)
- [C# 12 Features](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-12)
- [EditorConfig Documentation](https://editorconfig.org/)
- [xUnit Documentation](https://xunit.net/)
- [FluentAssertions Documentation](https://fluentassertions.com/)
- [Testcontainers for .NET](https://dotnet.testcontainers.org/)

---

**Remember**: This is a library/framework project, not an application. Always consider:

- Public API surface area
- Breaking changes
- Backward compatibility
- Performance implications
- Thread safety
- Proper disposal of resources
