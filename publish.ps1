<#
.SYNOPSIS
    Publishes BetterUMM as a self-contained single-file executable (.NET runtime included).
.PARAMETER Profile
    Publish profile name (e.g. win-x64, linux-x64, osx-x64, osx-arm64).
    Pass "all" to build every profile under BetterUMM\Properties\PublishProfiles in one run.
#>
param(
    [string]$Profile = "win-x64"
)

$ProjectPath = Join-Path $PSScriptRoot "BetterUMM\BetterUMM.csproj"
$ProfilesDir = Join-Path $PSScriptRoot "BetterUMM\Properties\PublishProfiles"

function Publish-Profile([string]$Name) {
    Write-Host "`n=== Publishing profile: $Name ===" -ForegroundColor Cyan

    dotnet publish $ProjectPath -c Release -p:PublishProfile=$Name

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish failed for profile '$Name' (exit code $LASTEXITCODE)"
        exit $LASTEXITCODE
    }

    $OutputDir = Join-Path $PSScriptRoot "BetterUMM\bin\Publish\$Name"
    Write-Host "Published to: $OutputDir" -ForegroundColor Green
}

if ($Profile -eq "all") {
    $Profiles = Get-ChildItem -Path $ProfilesDir -Filter "*.pubxml" | ForEach-Object { $_.BaseName }

    if (-not $Profiles) {
        Write-Error "No publish profiles found in $ProfilesDir"
        exit 1
    }

    foreach ($p in $Profiles) {
        Publish-Profile $p
    }

    Write-Host "`nAll profiles published: $($Profiles -join ', ')" -ForegroundColor Green
}
else {
    Publish-Profile $Profile
}
