# Copilot Instructions

This is a modern headless .NET Core framework for building web/general applications

## Project Overview

This is **cs-framework**, a modern, headless .NET framework designed for building web applications and services. It provides composable NuGet packages for common functionality like APIs, databases, messaging, caching, and more.

**Key characteristics:**
- Unopinionated and modular design - use only what you need
- Supports .NET 9.0 with preview language features enabled
- Uses centralized package management via Directory.Packages.props
- Extensive test coverage with both unit and integration tests

## Architecture

The framework follows a modular architecture with clear separation of concerns:

### Source Structure (`src/`)
- **Framework.Base** - Core abstractions and utilities
- **Framework.Api*** - Web API related packages (MVC, Minimal APIs, validation, etc.)
- **Framework.Database*** - Database providers (SQL Server, PostgreSQL, SQLite)
- **Framework.Orm*** - ORM integrations (Entity Framework, Dapper, Couchbase)
- **Framework.Messaging*** - Message queue abstractions and implementations
- **Framework.Caching*** - Caching abstractions and providers
- **Framework.Blobs*** - File storage abstractions (Azure, AWS, FileSystem, etc.)
- **Framework.Emails*** - Email service providers
- **Framework.Sms*** - SMS service providers
- **Framework.Testing** - Testing utilities and helpers

### Test Structure (`tests/`)
- Unit tests: `*.Tests.Unit` or `*.Tests.Units`
- Integration tests: `*.Tests.Integration` or `*.Tests.Integrations`
- Test harnesses: `*.Tests.Harness` for shared test infrastructure

## Development Commands

### Build System
The project uses NUKE build system. Use these commands:

```bash
# Build the solution
.\build.ps1 Compile

# Run all tests
.\build.ps1 Test

# Create NuGet packages
.\build.ps1 Pack

# Clean artifacts and build outputs
.\build.ps1 Clean
```

For cross-platform development:
```bash
# On Linux/macOS
./build.sh Compile
./build.sh Test
```

### Common .NET Commands

```bash
# Build solution
dotnet build

# Run specific test project
dotnet test tests/Framework.Base.Tests.Unit

# Restore packages
dotnet restore

# Format code (requires dotnet-csharpier tool)
dotnet csharpier .

# Entity Framework migrations (when applicable)
dotnet ef migrations add <MigrationName> --project <ProjectPath>
```

### Tools
Available via `dotnet tool restore`:
- **csharpier** - Code formatter
- **dotnet-ef** - Entity Framework CLI
- **minver** - Automatic versioning
- **husky** - Git hooks

## Code Standards

### C# Conventions (from .github/copilot-instructions.md)
- Use nullable reference types (NRT) and `required` keyword
- Prefer `init` over setters when possible
- Pascal case with underscore prefix for private methods (`_PrivateMethod`)
- Camel case for local methods (`localMethod`)

### Testing Conventions
- Test method names: `should_<feature>_<behavior>_when_<condition>`
- Use Given-When-Then pattern
- Testing stack: xUnit, FluentAssertions, Bogus, NSubstitute, DeepCloner
- Test containers available for integration tests (SQL Server, PostgreSQL, Redis, etc.)

### Package Management
- **NEVER** include version numbers in project references
- All package versions are managed centrally in `Directory.Packages.props`
- Use `ManagePackageVersionsCentrally` for consistent dependency management

## Important Files

- **Directory.Packages.props** - Central package version management
- **Directory.Build.props** - Common MSBuild properties
- **global.json** - .NET SDK version (9.0.0 with preview features)
- **framework.sln** - Main solution file
- **.editorconfig** - Code formatting rules
- **build/Build.cs** - NUKE build script

## Integration Testing

The framework includes extensive integration test infrastructure:
- **Testcontainers** for database and service dependencies
- **Test harnesses** in `*.Tests.Harness` projects for shared setup
- Docker Compose configurations for complex integration scenarios
- Name test method with should_<feature>_<behavior>_when_<condition>
- Use the given-when-then pattern for writing tests.
- Testing Lib: xUnit, FluentAssertions, Bogus, NSubstitute and DeepCloner
- 
When running integration tests, ensure Docker is available for container-based dependencies.

## Preferences

- Terminal commands to be compatible with powershell core
- Write an up to date code
- Ensure all code is null-safe use NRT and required keyword
- Use init instead of setters when possible

## Naming conventions

- Pascal case prefixed with underscore for private methods
- Camel case for local methods
