param([string]$Release='3.0.0',[string]$Short='3.0')

$ErrorActionPreference='Stop'
$root=Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -AssemblyName System.IO.Compression.FileSystem
& (Join-Path $root 'Build-Exe.ps1')

$exe=Join-Path $root '流光日程.exe'
if(-not (Test-Path $exe)){throw '找不到编译后的 流光日程.exe'}
Copy-Item $exe (Join-Path $root ('流光日程-v'+$Short+'.exe')) -Force

$dist=Join-Path $root ('dist\Lumi-v'+$Release)
if(Test-Path $dist){Remove-Item $dist -Recurse -Force}
New-Item -ItemType Directory -Path $dist | Out-Null

$packagedExe=Join-Path $dist ('流光日程-v'+$Short+'.exe')
Copy-Item $exe $packagedExe -Force
Copy-Item (Join-Path $root 'V3-使用指南.md') $dist -Force
Copy-Item (Join-Path $root '使用说明.txt') $dist -Force

$files=@($packagedExe,(Join-Path $dist 'V3-使用指南.md'),(Join-Path $dist '使用说明.txt'))
$lines=foreach($file in $files){
 $hash=(Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
 $hash+'  '+(Split-Path $file -Leaf)
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'),$lines,(New-Object System.Text.UTF8Encoding($false)))

$zip=Join-Path $root ('dist\Lumi-v'+$Release+'-Portable.zip')
if(Test-Path $zip){Remove-Item $zip -Force}
[IO.Compression.ZipFile]::CreateFromDirectory($dist,$zip,[IO.Compression.CompressionLevel]::Optimal,$false,[Text.Encoding]::UTF8)

Write-Output ('发布目录：'+$dist)
Write-Output ('便携包：'+$zip)
