# Copilot Instructions

This is a modern headless .NET Core framework for building web/general applications

## Preferences

- Terminal commands to be compatible with powershell core
- Write an up to date code
- Ensure all code is null-safe use NRT and required keyword
- Use init instead of setters when possible

## Naming conventions

- Pascal case prefixed with underscore for private methods
- Camel case for local methods

## Testing

- Name test method with should_<feature>_<behavior>_when_<condition>
- Use the given-when-then pattern for writing tests.
- Testing Lib: xUnit, FluentAssertions, Bogus, NSubstitute and DeepCloner
