param(
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$DataDirectory
)
$ErrorActionPreference = "Stop"
$releasePath = [IO.Path]::GetFullPath($Destination)
$dataPath = [IO.Path]::GetFullPath($DataDirectory)
if (-not [IO.Path]::IsPathFullyQualified($Destination) -or -not [IO.Path]::IsPathFullyQualified($DataDirectory)) { throw "Use absolute release and data paths." }
if (Test-Path -LiteralPath $releasePath) { throw "Release destination already exists. Publish a new version directory." }
$releasePrefix = $releasePath.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$dataPrefix = $dataPath.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($dataPath.StartsWith($releasePrefix,[StringComparison]::OrdinalIgnoreCase) -or $releasePath.StartsWith($dataPrefix,[StringComparison]::OrdinalIgnoreCase) -or $releasePath -eq $dataPath) { throw "Keep release and data directories separate." }
$courseRoot = Split-Path $PSScriptRoot -Parent
$frontend = Join-Path $courseRoot "lesson-02"
$backend = Join-Path $courseRoot "lesson-03"
& npm.cmd run build:management --prefix $frontend
if ($LASTEXITCODE -ne 0) { throw "Frontend build failed." }
New-Item -ItemType Directory -Path $releasePath -ErrorAction Stop | Out-Null
New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
$pending = Join-Path $releasePath "RELEASE-INCOMPLETE"
Set-Content -LiteralPath $pending -Value "Do not run until publish completes."
& dotnet publish $backend --configuration Release --output $releasePath --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed; keep the incomplete marker and use a new release destination." }
Copy-Item -LiteralPath (Join-Path $frontend ".local/dist") -Destination (Join-Path $releasePath "wwwroot") -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Run-Trace.ps1") -Destination $releasePath
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Caddyfile.example") -Destination $releasePath
$config = @{ TraceDb = (Join-Path $dataPath "trace.db"); Management = @{ Enabled = $true; SecureCookies = $true; KeyDirectory = (Join-Path $dataPath "keys") }; Live = @{ Enabled = $true; Assets = (Join-Path $releasePath "wwwroot") } }
$config | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releasePath "appsettings.Production.json") -Encoding utf8
# Check physical headers, including copied runtime scripts, on hosts with transparent encryption.
$textFiles = Get-ChildItem -LiteralPath $releasePath -Recurse -File | Where-Object { $_.Extension -in @(".ps1",".json",".js",".css",".html",".example") }
foreach ($file in $textFiles) {
    $dump = & certutil -dump $file.FullName 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Cannot inspect physical header: $($file.FullName)" }
    if (($dump | Out-String) -match "%TSD-Header") { throw "Encrypted published text: $($file.FullName). Preserve content and restore plaintext before use." }
}
$inventory = Get-ChildItem -LiteralPath $releasePath -Recurse -File | Where-Object { $_.Name -notin @("release-manifest.json","RELEASE-INCOMPLETE") } | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($releasePath,$_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
@{ createdUtc = [DateTime]::UtcNow.ToString("O"); files = @($inventory) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releasePath "release-manifest.json") -Encoding utf8
Remove-Item -LiteralPath $pending
Write-Output "Release ready: $releasePath"
Write-Output "Data directory: $dataPath"
