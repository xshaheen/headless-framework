# How to publish packages

CI relies on `Headless.NET.Sdk` to enable `GenerateSBOM=true` on GitHub Actions. The checked-in repo no longer includes local `build.ps1`/`nuke` pack entrypoints, so the current local flow is `dotnet pack` followed by manual package pushes.

Symbols are embedded in the assemblies (`DebugType=embedded` in `src/Directory.Build.props`), so no `.snupkg` symbol packages are produced or pushed.

```powershell
dotnet pack --configuration Release --output ./artifacts/packages-results
cd ./artifacts/packages-results
dotnet nuget remove source github.com 2>$null
dotnet nuget add source https://nuget.pkg.github.com/xshaheen/index.json --name github.com --username xshaheen --password $env:GITHUB_TOKEN --store-password-in-clear-text

dotnet nuget push .\*.nupkg --source github.com --skip-duplicate --api-key $env:GITHUB_TOKEN
# OR
Get-ChildItem .\ -Filter *.nupkg |
    ForEach-Object {
        Write-Host "Dotnet NuGet Push: $($_.Name)"
        dotnet nuget push --source github.com --skip-duplicate --api-key $env:GITHUB_TOKEN $_
    }
```

If you need to generate the SBOM locally too, opt into the same SDK behavior:

```powershell
dotnet pack --configuration Release --output ./artifacts/packages-results /p:GenerateSBOM=true
```
