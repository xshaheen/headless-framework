# Recipe: Add a Command

Use this recipe when adding behavior to one module.

1. Add the command and response in the module application project.

   Example target: `HeadlessShop.Catalog.Application`.

2. Add the handler beside the command.

   The handler owns business decisions and may use that module's persistence. It must not reference another module's internals.

3. Add a validator when the command accepts external input.

   Register the validator in the module setup file, for example `HeadlessShop.Catalog.Module/CatalogModule.cs`.

4. Add or update the endpoint in the module API project.

   The endpoint should call `ISender.Send(...)` and should not use a DbContext, repository, or domain aggregate directly.

5. If another module needs to react, publish a contract from `HeadlessShop.Contracts` through Headless messaging.

   Do not reference another module's application, domain, infrastructure, API, or module project.

6. Run validation.

   ```bash
   dotnet test HeadlessShop.Tests.Architecture/HeadlessShop.Tests.Architecture.csproj
   dotnet test HeadlessShop.Tests.Integration/HeadlessShop.Tests.Integration.csproj
   ```

Expected result: the new command stays inside its module, endpoint logic remains thin, and validation still passes.
