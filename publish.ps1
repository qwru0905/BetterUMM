<#
.SYNOPSIS
    Publishes BetterUMM as a self-contained single-file executable (.NET runtime included).
#>
param(
    [string]$Profile = "win-x64"
)

$ProjectPath = Join-Path $PSScriptRoot "BetterUMM\BetterUMM.csproj"

dotnet publish $ProjectPath -c Release -p:PublishProfile=$Profile

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed (exit code $LASTEXITCODE)"
    exit $LASTEXITCODE
}

$OutputDir = Join-Path $PSScriptRoot "BetterUMM\bin\Publish\$Profile"
Write-Host "`nPublished to: $OutputDir" -ForegroundColor Green
