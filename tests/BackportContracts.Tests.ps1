$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
function Assert-Contains([string]$relativePath, [string]$pattern, [string]$message) {
    $path = Join-Path $root $relativePath
    $content = Get-Content -LiteralPath $path -Raw
    if (-not $content.Contains($pattern)) { throw "$message ($relativePath missing '$pattern')" }
}

# AWS safety contracts introduced by v9.2.2.
Assert-Contains 'src/AFR.HostIntegration/AwsHideableDialogPatcherCore.cs' 'ApplyInstallOrUpdateOverride' 'AWS patcher must support safe install/update override'
Assert-Contains 'src/AFR.HostIntegration/AwsHideableDialogPatcherCore.cs' 'GetSuppressionState' 'AWS patcher must expose suppression-state detection'
Assert-Contains 'src/AFR.Deployer/Services/AwsHideableDialogPatcher.cs' 'ApplyInstallOrUpdateOverride' 'Deployer wrapper must expose install/update override'

# Runtime lifecycle contracts. Local RuntimeAutoCadPlatform and AFRSELFTEST remain mandatory.
Assert-Contains 'src/AutoCAD/AFR.AutoCAD/Hosting/AppInitializer.cs' 'PluginInitializationResult' 'Initializer must return structured lifecycle state'
Assert-Contains 'src/AutoCAD/AFR.AutoCAD/Hosting/PluginEntryBase.cs' 'ShouldSkipRuntimeStartup' 'Plugin entry must use structured startup decision'
Assert-Contains 'src/AutoCAD/AFR.AutoCAD/Hosting/PluginEntryBase.cs' 'versionYear < 2018' 'AWS suppression warning must skip unsupported legacy AutoCAD versions'
Assert-Contains 'src/AutoCAD/AFR.AutoCAD/Hosting/RuntimeAutoCadPlatform.cs' 'RuntimeAutoCadPlatform' 'Merged-DLL runtime platform must be preserved'
Assert-Contains 'src/AFR.Core/Constants/CommandNames.cs' 'AFRSELFTEST' 'Local self-test command must be preserved'

# UI/data-binding lifecycle contracts.
Assert-Contains 'src/AFR.Deployer/Services/RegistryChangeWatcher.cs' 'LibraryImport' 'Registry watcher must use source-generated Win32 interop'
Assert-Contains 'src/AFR.UI/UiCommand.cs' 'RaiseCanExecuteChanged' 'UI command must expose explicit CanExecute refresh'

Write-Output 'Backport contract checks passed.'


