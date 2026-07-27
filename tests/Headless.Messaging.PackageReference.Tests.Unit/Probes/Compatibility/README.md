# Messaging package-family compatibility probes

These projects are isolated from the repository's central package versions and inherited sources.

- `PreviousAllOld` pins every Messaging package shipped by tag `0.11.0` and compiles the former `IOutboxBus` API. Its GitHub Packages credential is supplied only through `GITHUB_PACKAGES_TOKEN`.
- `NewAllNew` pins every currently packable Messaging package to `MessagingPackageVersion`, resolves `Headless.*` exclusively from `artifacts/packages-results`, and compiles the final verb/lane/delivery API.
- `SelectedMixed` directly pins `Headless.Messaging.Core` `0.11.0` beside the locally packed `Headless.Messaging.Redis`. Its two-feed mapping is intentionally narrow evidence for this selected graph; its observed diagnostic must not be generalized to every mixed graph.

The selected mixed restore is expected to fail with `NU1605`: the local Redis package requires the matching new Core version while the application directly pins Core `0.11.0`. This proves only that selected downgrade boundary. The all-new lockfile is an ephemeral exact-artifact receipt because MinVer changes the preview version at each commit; the committed all-old lock remains stable at `0.11.0`.

Run from the repository root after `make pack`:

```bash
GITHUB_PACKAGES_TOKEN="$(gh auth token)" dotnet build \
  tests/Headless.Messaging.PackageReference.Tests.Unit/Probes/Compatibility/PreviousAllOld/PreviousAllOld.csproj \
  --configfile tests/Headless.Messaging.PackageReference.Tests.Unit/Probes/Compatibility/PreviousAllOld/NuGet.config

version="$(tr -d '\n' < artifacts/packages-results/package-version.txt)"
dotnet build \
  tests/Headless.Messaging.PackageReference.Tests.Unit/Probes/Compatibility/NewAllNew/NewAllNew.csproj \
  --configfile tests/Headless.Messaging.PackageReference.Tests.Unit/Probes/Compatibility/NewAllNew/NuGet.config \
  -p:MessagingPackageVersion="$version"

GITHUB_PACKAGES_TOKEN="$(gh auth token)" dotnet restore \
  tests/Headless.Messaging.PackageReference.Tests.Unit/Probes/Compatibility/SelectedMixed/SelectedMixed.csproj \
  --configfile tests/Headless.Messaging.PackageReference.Tests.Unit/Probes/Compatibility/SelectedMixed/NuGet.config \
  -p:MessagingPackageVersion="$version"
```

Do not commit credentials, temporary package caches, or raw authenticated restore logs.
