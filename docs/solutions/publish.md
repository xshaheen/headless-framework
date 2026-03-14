# How to publish packages

CI generates an SBOM for the packed artifacts at `artifacts/packages-results/_manifest/...` after `dotnet pack`. Local publishing only needs to push the `.nupkg` and `.snupkg` files.

```powershell
./build.ps1 pack --configuration Release
# OR
nuke pack --configuration Release
cd ./artifacts/packages-results
dotnet nuget add source https://nuget.pkg.github.com/xshaheen/index.json --name GitHub --username xshaheen --password $env:GITHUB_TOKEN

dotnet nuget push .\*.nupkg --source GitHub --skip-duplicate --api-key $env:GITHUB_TOKEN
# OR
Get-ChildItem .\ -Filter *.nupkg |
    ForEach-Object {
        Write-Host "Dotnet NuGet Push: $($_.Name)"
        dotnet nuget push --source GitHub --skip-duplicate --api-key $env:GITHUB_TOKEN $_
    }

# Push symbols
Get-ChildItem .\ -Filter *.snupkg |
    ForEach-Object {
        Write-Host "Dotnet NuGet Push: $($_.Name)"
        dotnet nuget push --source GitHub --skip-duplicate --api-key $env:GITHUB_TOKEN $_
    }
```

If you need to generate the SBOM locally too:

```powershell
dotnet tool restore
$version = dotnet tool run minver
Invoke-WebRequest -Uri "https://github.com/microsoft/sbom-tool/releases/latest/download/sbom-tool-win-x64.exe" -OutFile "sbom-tool.exe"
.\sbom-tool.exe generate -b .\artifacts\packages-results -bc . -pn headless-framework -pv $version -ps "Mahmoud Shaheen" -nsb "https://github.com/xshaheen/headless-framework"
```
