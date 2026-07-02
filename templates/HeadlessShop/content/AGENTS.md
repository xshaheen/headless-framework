# HeadlessShop Agent Rules

This generated app is a Headless Framework capability tour. Keep edits aligned with the modular boundary:

- Put shared integration contracts in `HeadlessShop.Contracts`.
- Keep Catalog behavior inside `HeadlessShop.Catalog.*` and Ordering behavior inside `HeadlessShop.Ordering.*`.
- Do not reference one module's internals from another module.
- Keep Minimal API endpoints thin. Endpoints translate HTTP input to Mediator commands or queries.
- Use Headless messaging abstractions for cross-module events. Do not add raw broker clients to modules.
- Use `ICurrentTenant` after authentication establishes the tenant context. Do not trust caller-supplied tenant IDs in request bodies.
- `FakeTourAuthenticationHandler` only honors headers in Development/Test unless `HeadlessShop:AllowFakeTourAuth` is explicitly enabled. Replace it before production use.
- Configure `HeadlessShop:Encryption:*` and `HeadlessShop:Hashing:DefaultSalt` outside Development.
- Keep OpenAPI/Scalar anonymous only in Development.

Validation:

```bash
dotnet restore HeadlessShop.slnx
dotnet test HeadlessShop.Tests.Architecture/HeadlessShop.Tests.Architecture.csproj
dotnet test HeadlessShop.Tests.Integration/HeadlessShop.Tests.Integration.csproj
```

When adding behavior, follow `docs/recipes/add-command.md` and run the validation commands above.
