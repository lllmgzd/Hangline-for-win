param(
    [switch]$SelfContained
)

# NOTE: keep this file ASCII-only.
# Windows PowerShell 5.1 reads .ps1 as ANSI unless the file has a UTF-8 BOM,
# so non-ASCII characters here can break parsing on a fresh machine.

$ErrorActionPreference = 'Stop'

# Only a dotnet that actually has an SDK can publish, so test each candidate
# instead of accepting the first one that merely exists.
function Test-DotnetSdk([string]$exe) {
    try {
        $sdks = & $exe --list-sdks 2>$null
        return ($LASTEXITCODE -eq 0) -and ($sdks -match '^\d+\.')
    } catch {
        return $false
    }
}

$candidates = @('dotnet')
if ($env:DOTNET_ROOT) { $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe') }
$candidates += 'C:\Program Files\dotnet\dotnet.exe'
# Non-standard install locations (for example an SDK unpacked outside Program Files).
$candidates += 'D:\dotnet\dotnet.exe'

$dotnet = $null
foreach ($candidate in $candidates) {
    if ($candidate -ne 'dotnet' -and -not (Test-Path $candidate)) { continue }
    if (Test-DotnetSdk $candidate) { $dotnet = $candidate; break }
}

if (-not $dotnet) {
    Write-Host 'No dotnet SDK found. Install .NET SDK 8 first: https://dotnet.microsoft.com/download'
    Write-Host 'If the SDK lives somewhere else, set DOTNET_ROOT and run this script again.'
    exit 1
}

$project = Join-Path $PSScriptRoot 'src\Hangline\Hangline.csproj'
$out = Join-Path $PSScriptRoot 'dist'

$buildArgs = @('publish', $project, '-c', 'Release', '-o', $out, '--nologo')

if ($SelfContained) {
    # Ship the runtime too, so the build runs on machines without .NET installed.
    $buildArgs += @('-r', 'win-x64', '--self-contained', 'true')
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

& $dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Built: $out\Hangline.exe"
