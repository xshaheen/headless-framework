---
domain: Templates
packages: Headless.Templates.HeadlessShop
---

# Templates

## Agent Instructions

- Use `dotnet new headless-shop -n <Name>` when a consumer asks for a runnable Headless modular shop tour.
- Treat the generated app as a capability tour, not a generic Clean Architecture starter.
- Read the generated `AGENTS.md` before editing generated output.
- Keep Catalog and Ordering isolated. Shared integration events belong in `<Name>.Contracts`.
- Keep endpoint files thin and route behavior through Mediator commands/queries.
- Use Headless messaging abstractions for cross-module events; do not add raw broker clients.
- Keep fake authentication local/test-only. Tenant context must resolve from authenticated claims, not request-body tenant IDs.
- Keep OpenAPI/Scalar anonymous only in Development unless the consumer adds their own authenticated operational boundary.

## Validation

Source maintainers validate the template package with:

```bash
./tools/validate-headless-shop-template.sh
```

Generated apps validate themselves with:

```bash
dotnet restore <Name>.slnx
dotnet test <Name>.Tests.Architecture/<Name>.Tests.Architecture.csproj
dotnet test <Name>.Tests.Integration/<Name>.Tests.Integration.csproj
```

The source repo gate packs the template, installs it into an isolated .NET CLI home, generates `TrailStore`, validates generated guardrail files, restores, builds, and runs generated architecture/integration tests.
