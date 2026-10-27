$ErrorActionPreference = 'Stop'
$hsRoot = Split-Path -Parent $PSScriptRoot
$hsBuild = Join-Path $hsRoot 'build'
New-Item -ItemType Directory -Path $hsBuild -Force | Out-Null
$hsZip = Join-Path $hsBuild 'nsis-3.12.zip'
$hsUrls = @('https://master.dl.sourceforge.net/project/nsis/NSIS%203/3.12/nsis-3.12.zip?viasf=1', 'https://downloads.sourceforge.net/project/nsis/NSIS%203/3.12/nsis-3.12.zip')
# SHA-256 published on the official SourceForge download page.
$hsExpected = '56581f90db321581c5381193d796fffcf2d24b2f8fed2160a6c6a3baa67f2c4f'
$hsVerified = (Test-Path -LiteralPath $hsZip) -and (Get-FileHash -LiteralPath $hsZip -Algorithm SHA256).Hash.ToLowerInvariant() -eq $hsExpected
foreach ($hsUrl in $hsUrls) {
    if ($hsVerified) { break }
    try {
        Invoke-WebRequest -Uri $hsUrl -OutFile $hsZip
        $hsVerified = (Get-FileHash -LiteralPath $hsZip -Algorithm SHA256).Hash.ToLowerInvariant() -eq $hsExpected
    } catch { Write-Warning $_.Exception.Message }
}
if (!$hsVerified) { throw 'NSIS 下载文件校验失败；不会运行下载内容。' }
Expand-Archive -LiteralPath $hsZip -DestinationPath $hsBuild -Force
Write-Output (Join-Path $hsBuild 'nsis-3.12\makensis.exe')
