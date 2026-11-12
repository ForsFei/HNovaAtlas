param(
    [ValidateSet("Serve","Migrate","Bootstrap","Backup","RevokeSessions")][string]$Mode = "Serve",
    [ValidateRange(1024,65535)][int]$Port = 43111,
    [string]$BackupPath
)
$ErrorActionPreference = "Stop"
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot "RELEASE-INCOMPLETE")) { throw "Incomplete release." }
$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot "appsettings.Production.json") -Raw | ConvertFrom-Json
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot "release-manifest.json") -Raw | ConvertFrom-Json
foreach ($item in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $item.path))
    $prefix = $PSScriptRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw "Manifest path escapes release." }
    if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.sha256) { throw "Release integrity mismatch: $($item.path)" }
}
$argsList = @((Join-Path $PSScriptRoot "QualityTrace.Storage.dll"), "--contentRoot", $PSScriptRoot, "--TraceDb", $config.TraceDb, "--Management:Enabled", "true", "--Management:KeyDirectory", $config.Management.KeyDirectory)
switch ($Mode) {
    "Migrate" { $argsList += @("--Management:Migrate","true") }
    "Bootstrap" { $argsList += @("--Management:Bootstrap","true") }
    "RevokeSessions" { $argsList += @("--Management:RevokeSessions","true") }
    "Backup" {
        if (-not $BackupPath -or -not [IO.Path]::IsPathFullyQualified($BackupPath)) { throw "Backup needs a new absolute BackupPath." }
        $argsList += @("--Backup",$BackupPath)
    }
    "Serve" {
        $argsList += @("--urls","http://127.0.0.1:$Port","--Live:Enabled","true","--Live:Assets",(Join-Path $PSScriptRoot "wwwroot"))
    }
}
$previousAspnetEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousDotnetEnvironment = $env:DOTNET_ENVIRONMENT
try {
    $env:ASPNETCORE_ENVIRONMENT = "Production"
    $env:DOTNET_ENVIRONMENT = "Production"
    & dotnet @argsList
    $runExitCode = $LASTEXITCODE
} finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousAspnetEnvironment
    $env:DOTNET_ENVIRONMENT = $previousDotnetEnvironment
}
exit $runExitCode
