$ErrorActionPreference = 'Stop'

$projectFile = Join-Path $PSScriptRoot 'src\LoaSchedule.App\LoaSchedule.App.csproj'

dotnet publish $projectFile `
    -c Release `
    -p:PublishProfile=SingleFile `
    '-p:RestoreSources=https://api.nuget.org/v3/index.json'

if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

$publishedExe = Join-Path $PSScriptRoot 'artifacts\release\LoaSchedule.App.exe'
Write-Host "단일 실행 파일을 생성했습니다: $publishedExe"
