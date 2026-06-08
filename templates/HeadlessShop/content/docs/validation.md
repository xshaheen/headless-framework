# Validation

Run these commands from the generated checkout:

```bash
dotnet restore HeadlessShop.slnx
dotnet test HeadlessShop.Tests.Architecture/HeadlessShop.Tests.Architecture.csproj
dotnet test HeadlessShop.Tests.Integration/HeadlessShop.Tests.Integration.csproj
```

The architecture tests enforce:

- no direct Catalog-to-Ordering or Ordering-to-Catalog references
- thin endpoint files
- Headless messaging abstractions instead of raw broker clients
- HTTP, Mediator, Messaging, and EF tenant posture configured together

The integration tests prove:

- product creation requires tenant context and permission
- product creation publishes `ProductCreated`
- Ordering consumes the event and accepts an order for the projected product
- tenant B cannot read tenant A product data, even with a spoof header
- OpenAPI/Scalar are Development-only
- fake tour authentication headers are rejected in Production

Do not add reusable secrets, tokens, passwords, or production credentials to generated files. Keep local fake identity values in tests and examples only.
