param([Parameter(Mandatory = $true)][string]$ModelPath)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'IndustrialYolo.Deployment.csproj'
$model = (Resolve-Path -LiteralPath $ModelPath).Path
if ((Get-FileHash -LiteralPath $model -Algorithm SHA256).Hash -ne 'C789161ED43C8269FCD4E67C67EEEB4E80C622DA2EB296A20BC6007BD18A0B7D') { throw 'Pinned model hash mismatch.' }
dotnet restore $project --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('bin/release-' + [Guid]::NewGuid().ToString('N'))))
$binRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'bin')) + [IO.Path]::DirectorySeparatorChar
if (-not $releaseRoot.StartsWith($binRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release path escaped example bin folder.' }
dotnet publish $project -c Release --self-contained false --no-restore -o $releaseRoot
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
[IO.Directory]::CreateDirectory((Join-Path $releaseRoot 'model')) | Out-Null
[IO.File]::Copy($model, (Join-Path $releaseRoot 'model/yolox_nano.onnx'), $false)
$published = Join-Path $releaseRoot 'IndustrialYolo.Deployment.exe'
$development = Join-Path $PSScriptRoot 'bin/Release/net10.0/win-x64/IndustrialYolo.Deployment.exe'
$profile = Join-Path $releaseRoot 'release-profile.json'
& $published --seal $releaseRoot 'local-cpu-v1'
if ($LASTEXITCODE -ne 0) { throw 'Release sealing failed.' }
$script:scenarioCount = 0
function Check-Report([string]$Executable, [string]$Mode, [string]$Suffix, [string]$ExpectedStatus, [string]$FailedStage = '') {
    $destination = $releaseRoot + '-' + $Suffix + '.json'
    & $Executable $Mode $profile $destination | Out-Host
    $expectedExit = if ($ExpectedStatus -eq 'Failed') { 3 } else { 0 }
    if ($LASTEXITCODE -ne $expectedExit) { throw "Unexpected exit for $Suffix" }
    $result = Get-Content -LiteralPath $destination -Raw | ConvertFrom-Json
    if ($result.status -ne $ExpectedStatus) { throw "Unexpected status for $Suffix" }
    if ($FailedStage -and -not ($result.stages | Where-Object { $_.name -eq $FailedStage -and $_.status -eq 'Failed' })) { throw "Wrong failed stage for $Suffix" }
    $script:scenarioCount++
    return $result
}
Push-Location $PSScriptRoot
try {
    # Commands execute from the source directory, NOT the publication directory.
    $inspection = Check-Report $development '--inspect' 'inventory' 'InventoryPassed'
    if (($inspection.stages | Where-Object name -eq 'CpuPipeline').status -ne 'NotRun') { throw 'Inventory mode unexpectedly claimed native execution.' }
    $wrongRoot = Check-Report $development '--probe' 'wrong-root' 'Failed' 'ExecutionRoot'
    if (($wrongRoot.stages | Where-Object name -eq 'CpuPipeline').status -ne 'NotRun') { throw 'Wrong-root native probe was not blocked.' }
    $probe = Check-Report $published '--probe' 'probe' 'ProbePassed'
    if ($probe.origins.Count -ne 2 -or $probe.smoke.outputValues -ne 301665 -or $probe.smoke.frozenReferenceSamples -ne 20 -or
        $probe.smoke.decodedRows -ne 3549 -or $probe.smoke.inputSha256 -ne '5F598DA750EAE012350FC7B4B49B154C609104EE2B65A9AA3AC5D51C7C469863') { throw 'Published smoke evidence mismatch.' }
    if ($probe.appBase.TrimEnd('\', '/') -eq $probe.workingDirectory.TrimEnd('\', '/')) { throw 'Different working-directory scenario not established.' }

    $settings = Join-Path $releaseRoot 'deployment-settings.json'
    $originalSettings = [IO.File]::ReadAllBytes($settings)
    try {
        [IO.File]::WriteAllBytes($settings, [byte[]]($originalSettings + [byte]32))
        $changed = Check-Report $published '--inspect' 'changed-settings' 'Failed' 'Inventory'
    } finally { [IO.File]::WriteAllBytes($settings, $originalSettings) }

    $manifest = Get-Content -LiteralPath $profile -Raw | ConvertFrom-Json
    $nativeRelative = ($manifest.files | Where-Object { [IO.Path]::GetFileName($_.path) -eq 'OpenCvSharpExtern.dll' }).path
    $native = [IO.Path]::GetFullPath((Join-Path $releaseRoot $nativeRelative))
    $hiddenNative = $native + '.held-for-owned-test'
    if (-not $native.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or [IO.File]::Exists($hiddenNative)) { throw 'Native test paths invalid.' }
    [IO.File]::Move($native, $hiddenNative, $false)
    try { $missing = Check-Report $published '--inspect' 'missing-native' 'Failed' 'Inventory' }
    finally { [IO.File]::Move($hiddenNative, $native, $false) }

    $inside = Join-Path $releaseRoot 'forbidden-report.json'
    & $published --inspect $profile $inside
    if ($LASTEXITCODE -ne 4 -or [IO.File]::Exists($inside)) { throw 'Sealed-directory report guard failed.' }
    $script:scenarioCount++
    $profileHash = (Get-FileHash -LiteralPath $profile -Algorithm SHA256).Hash
    & $published --seal $releaseRoot 'must-not-overwrite'
    if ($LASTEXITCODE -ne 4 -or (Get-FileHash -LiteralPath $profile -Algorithm SHA256).Hash -ne $profileHash) { throw 'Profile overwrite guard failed.' }
    $script:scenarioCount++
    $restored = Check-Report $published '--inspect' 'restored' 'InventoryPassed'
    Write-Output "PASS $script:scenarioCount actual publish/child-process/diagnostic scenarios. Local host only, not clean-machine or field acceptance."
    Write-Output "RELEASE_ROOT=$releaseRoot"
    Write-Output "PROBE_REPORT=$($releaseRoot + '-probe.json')"
} finally { Pop-Location }
