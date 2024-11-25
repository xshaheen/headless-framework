# How to publish packages

```powershell
./build.ps1 pack --configuration Release 
# OR
nuke pack --configuration Release
cd ./artifacts/packages-results
dotnet nuget add source https://nuget.pkg.github.com/xshaheen/index.json --name GitHub --username xshaheen --password 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
dotnet nuget push .\*.nupkg --source GitHub --skip-duplicate --api-key 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
# OR
Get-ChildItem .\ -Filter *.nupkg |
    Where-Object { !$_.Name.Contains('preview') } |
    ForEach-Object {
        Write-Host "Dotnet NuGet Push: $($_.Name)"
        dotnet nuget push --source GitHub --skip-duplicate --api-key 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 $_
    }
```
