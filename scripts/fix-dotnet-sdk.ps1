# fix-dotnet-sdk.ps1 - self-elevating repair for the incomplete .NET SDK 10.0.302
# Run from a normal (non-elevated) PowerShell:  .\fix-dotnet-sdk.ps1
# It elevates itself, creates the two missing workload SDK resolver folders, and
# prints the result log back into this window.

$ErrorActionPreference = 'Stop'
$logPath = Join-Path $PSScriptRoot 'sdk-fix.log'

function Write-Log([string]$Message) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $line
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Requesting administrator elevation (click Yes on the UAC prompt)...'
    Start-Process pwsh -Verb RunAs -Wait -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath) -ErrorAction Stop
    Write-Host ''
    if (Test-Path -LiteralPath $logPath) {
        Write-Host '--- sdk-fix.log (last 25 lines) ---'
        Get-Content -LiteralPath $logPath -Tail 25
    } else {
        Write-Host 'No log file found - the elevated run may not have started.'
    }
    exit 0
}

try {
    Add-Content -LiteralPath $logPath -Value '' -Encoding UTF8
    Write-Log 'fix-dotnet-sdk.ps1 starting (elevated).'

    $sdkVersion = '10.0.302'
    $sdksRoot = Join-Path $env:ProgramFiles "dotnet\sdk\$sdkVersion\Sdks"
    if (-not (Test-Path -LiteralPath $sdksRoot)) {
        Write-Log "ERROR: SDK Sdks folder not found: $sdksRoot"
        exit 1
    }

    $placeholder = '<Project><!-- Placeholder restored by fix-dotnet-sdk.ps1. A proper SDK repair replaces this with the real Microsoft file. --></Project>'

    $locators = @(
        'Microsoft.NET.SDK.WorkloadAutoImportPropsLocator',
        'Microsoft.NET.SDK.WorkloadManifestTargetsLocator'
    )

    foreach ($name in $locators) {
        $sdkDir = Join-Path $sdksRoot $name
        $sdk = Join-Path $sdkDir 'Sdk'
        New-Item -ItemType Directory -Force -Path $sdk | Out-Null
        Set-Content -LiteralPath (Join-Path $sdk 'Sdk.props') -Value $placeholder -Encoding UTF8
        Set-Content -LiteralPath (Join-Path $sdk 'Sdk.targets') -Value $placeholder -Encoding UTF8
        Write-Log "Created: $sdk\Sdk.props and Sdk.targets"
    }

    # The import files MUST live inside the Sdk subfolder (see MSB4019 resolution path).
    Set-Content -LiteralPath (Join-Path $sdksRoot 'Microsoft.NET.SDK.WorkloadAutoImportPropsLocator\Sdk\AutoImport.props') -Value $placeholder -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $sdksRoot 'Microsoft.NET.SDK.WorkloadManifestTargetsLocator\Sdk\WorkloadManifest.targets') -Value $placeholder -Encoding UTF8
    Write-Log 'Created workload locator import files inside each Sdk folder.'

    # Remove the earlier misplaced copies (wrong location) if present.
    Remove-Item -LiteralPath (Join-Path $sdksRoot 'Microsoft.NET.SDK.WorkloadAutoImportPropsLocator\AutoImport.props') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $sdksRoot 'Microsoft.NET.SDK.WorkloadManifestTargetsLocator\WorkloadManifest.targets') -ErrorAction SilentlyContinue
    Write-Log 'Removed misplaced locator files from the locator root (if any).'

    $checks = @(
        (Join-Path $sdksRoot 'Microsoft.NET.SDK.WorkloadAutoImportPropsLocator\Sdk\AutoImport.props'),
        (Join-Path $sdksRoot 'Microsoft.NET.SDK.WorkloadManifestTargetsLocator\Sdk\WorkloadManifest.targets'),
        (Join-Path $sdksRoot 'Microsoft.NET.SDK.WorkloadAutoImportPropsLocator\Sdk\Sdk.props'),
        (Join-Path $sdksRoot 'Microsoft.NET.SDK.WorkloadManifestTargetsLocator\Sdk\Sdk.targets')
    )
    $missing = $checks | Where-Object { -not (Test-Path -LiteralPath $_) }
    if ($missing.Count -eq 0) {
        Write-Log 'SUCCESS: workload SDK resolver folders and import files are in place.'
    } else {
        Write-Log "WARNING: verification failed - missing: $($missing -join '; ')"
    }
} catch {
    Write-Log "FAILED: $($_.Exception.Message)"
    exit 1
}
