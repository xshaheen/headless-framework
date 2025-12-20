# How to publish packages

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
