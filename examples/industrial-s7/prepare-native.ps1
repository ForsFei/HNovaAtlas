$ErrorActionPreference = 'Stop'
if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows) -or
    [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
    throw '本机网络示例仅准备 Windows x64 测试依赖；其他平台可运行 --offline-check。'
}
$s7NativeDirectory = Join-Path $PSScriptRoot '.local'
New-Item -ItemType Directory -Path $s7NativeDirectory -Force | Out-Null
$s7NativePath = Join-Path $s7NativeDirectory 'snap7.dll'
$s7ExpectedHash = '17336585C8F5EBE55D6DB85152BFD64D5456F6C30B26AC862980AF85F725072D'
$s7NativeUrl = 'https://raw.githubusercontent.com/S7NetPlus/s7netplus/f1ae0ea084e712b59e414de6aaee7d196244a239/S7.Net.UnitTest/runtimes/win-x64/native/snap7.dll'
if (-not (Test-Path -LiteralPath $s7NativePath)) {
    Invoke-WebRequest -Uri $s7NativeUrl -OutFile $s7NativePath -TimeoutSec 60
}
if ((Get-FileHash -LiteralPath $s7NativePath -Algorithm SHA256).Hash -ne $s7ExpectedHash) {
    throw "测试 DLL 哈希不匹配，拒绝继续。请检查该文件：$s7NativePath"
}
Invoke-WebRequest -Uri 'https://www.gnu.org/licenses/lgpl-3.0.txt' -OutFile (Join-Path $s7NativeDirectory 'lgpl-3.0.txt') -TimeoutSec 60
Invoke-WebRequest -Uri 'https://www.gnu.org/licenses/gpl-3.0.txt' -OutFile (Join-Path $s7NativeDirectory 'gpl-3.0.txt') -TimeoutSec 60
Write-Output 'Snap7 测试依赖与许可证已准备，SHA256 已核对。未安装服务、未修改 PLC。'
