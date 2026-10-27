param([switch]$Installer, [switch]$Test, [string]$NsisPath)
$ErrorActionPreference = 'Stop'
$hsRoot = $PSScriptRoot
$hsBuild = Join-Path $hsRoot 'build'
$hsDist = Join-Path $hsRoot 'dist'
$hsCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $hsCompiler)) { throw '需要 Windows .NET Framework 4.5 或更新版本的 C# 编译器。' }
New-Item -ItemType Directory -Path $hsBuild,$hsDist -Force | Out-Null
$hsReferences = @('/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Runtime.Serialization.dll')
$hsSources = @((Join-Path $hsRoot 'src\Core.cs'), (Join-Path $hsRoot 'src\App.cs'))
$hsCommon = @('/nologo','/optimize+','/platform:anycpu','/utf8output') + $hsReferences
& $hsCompiler @hsCommon /target:exe /main:HearthstoneRank.IconBuilder "/out:$hsBuild\IconBuilder.exe" @hsSources (Join-Path $hsRoot 'tools\IconBuilder.cs')
if ($LASTEXITCODE -ne 0) { throw '图标工具编译失败。' }
& "$hsBuild\IconBuilder.exe" "$hsBuild\app.ico"
if ($LASTEXITCODE -ne 0) { throw '图标生成失败。' }
& $hsCompiler @hsCommon /target:winexe "/out:$hsDist\HearthstoneRank.exe" "/win32icon:$hsBuild\app.ico" "/win32manifest:$hsRoot\src\app.manifest" @hsSources
if ($LASTEXITCODE -ne 0) { throw '应用编译失败。' }
Copy-Item -LiteralPath "$hsRoot\src\app.config" -Destination "$hsDist\HearthstoneRank.exe.config"
Copy-Item -LiteralPath "$hsRoot\LICENSE" -Destination "$hsDist\LICENSE.txt"
Copy-Item -LiteralPath "$hsRoot\README.md" -Destination "$hsDist\HearthstoneRank-README.md"
if ($Test) {
    & $hsCompiler @hsCommon /target:exe "/out:$hsBuild\CoreTests.exe" (Join-Path $hsRoot 'src\Core.cs') (Join-Path $hsRoot 'tests\CoreTests.cs')
    if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
    & "$hsBuild\CoreTests.exe"
    if ($LASTEXITCODE -ne 0) { throw '核心测试失败。' }
}
Compress-Archive -LiteralPath "$hsDist\HearthstoneRank.exe","$hsDist\HearthstoneRank.exe.config","$hsDist\LICENSE.txt","$hsDist\HearthstoneRank-README.md" -DestinationPath "$hsDist\HearthstoneRank-1.0.0-portable.zip" -Force
if ($Installer) {
    if (!$NsisPath) {
        $hsNsisCandidates = @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe", "$hsBuild\nsis-3.12\makensis.exe")
        $NsisPath = $hsNsisCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (!$NsisPath) { throw '请先运行 tools\Get-Nsis.ps1，或通过 -NsisPath 指定 makensis.exe。' }
    & $NsisPath /V2 "$hsRoot\installer.nsi"
    if ($LASTEXITCODE -ne 0) { throw '安装包构建失败。' }
}
$hsReleaseFiles = Get-ChildItem -LiteralPath $hsDist -File | Where-Object { $_.Name -eq 'HearthstoneRank-1.0.0-Setup.exe' -or $_.Extension -eq '.zip' }
$hsReleaseFiles | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name } | Set-Content -LiteralPath "$hsDist\SHA256SUMS.txt" -Encoding ascii
Get-ChildItem -LiteralPath $hsDist -File | Select-Object Name,Length
