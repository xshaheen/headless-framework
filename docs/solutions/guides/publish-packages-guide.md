# How to publish packages

CI generates an SBOM for the packed artifacts at `artifacts/packages-results/_manifest/...` after `dotnet pack`. The checked-in repo no longer includes local `build.ps1`/`nuke` pack entrypoints, so the current local flow is `dotnet pack` followed by manual package pushes.

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

If you need to generate the SBOM locally too:

```powershell
dotnet tool restore
$version = dotnet tool run minver
Invoke-WebRequest -Uri "https://github.com/microsoft/sbom-tool/releases/latest/download/sbom-tool-win-x64.exe" -OutFile "sbom-tool.exe"
.\sbom-tool.exe generate -b .\artifacts\packages-results -bc . -pn headless-framework -pv $version -ps "Mahmoud Shaheen" -nsb "https://github.com/xshaheen/headless-framework"
```
