# How to publish packages

CI relies on `Headless.NET.Sdk` to enable `GenerateSBOM=true` on GitHub Actions. The checked-in repo no longer includes local `build.ps1`/`nuke` pack entrypoints, so the current local flow is `dotnet pack` followed by manual package pushes.

```powershell
dotnet pack --configuration Release --include-symbols --output ./artifacts/packages-results
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

# Push symbols
Get-ChildItem .\ -Filter *.snupkg |
    ForEach-Object {
        Write-Host "Dotnet NuGet Push: $($_.Name)"
        dotnet nuget push --source github.com --skip-duplicate --api-key $env:GITHUB_TOKEN $_
    }
```

If you need to generate the SBOM locally too, opt into the same SDK behavior:

```powershell
dotnet pack --configuration Release --include-symbols --output ./artifacts/packages-results /p:GenerateSBOM=true
```
