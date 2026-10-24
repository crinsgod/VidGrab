# Build VidGrab as a single Windows executable.
# Requires Windows + .NET Framework 4.8 (csc.exe ships with the OS).
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$build = Join-Path $root "build"
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $build, $dist | Out-Null

Write-Host "[1/4] Downloading yt-dlp..."
$ytdlp = Join-Path $build "yt-dlp.exe"
if (-not (Test-Path $ytdlp)) {
    Invoke-WebRequest -Uri "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" `
        -OutFile $ytdlp -UseBasicParsing
}

Write-Host "[2/4] Downloading ffmpeg..."
$ffmpeg = Join-Path $build "ffmpeg.exe"
if (-not (Test-Path $ffmpeg)) {
    $zip = Join-Path $build "ffmpeg.zip"
    Invoke-WebRequest -Uri "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip" `
        -OutFile $zip -UseBasicParsing
    $extract = Join-Path $build "ffmpeg"
    if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $extract -Force
    $exe = Get-ChildItem $extract -Recurse -Filter ffmpeg.exe | Select-Object -First 1
    Copy-Item $exe.FullName $ffmpeg -Force
    Remove-Item $zip -Force
}

Write-Host "[3/4] Compressing embedded binaries..."
Add-Type -AssemblyName System.IO.Compression
function Compress-File([string]$src, [string]$dst) {
    $in = [System.IO.File]::OpenRead($src)
    $out = [System.IO.File]::Create($dst)
    $deflate = New-Object System.IO.Compression.DeflateStream(
        $out, [System.IO.Compression.CompressionLevel]::Optimal)
    $in.CopyTo($deflate)
    $deflate.Dispose(); $out.Dispose(); $in.Dispose()
}
Compress-File $ytdlp (Join-Path $build "ytdlp.bin")
Compress-File $ffmpeg (Join-Path $build "ffmpeg.bin")

Write-Host "[4/4] Compiling..."
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe not found; install .NET Framework 4.8." }

$sources = Get-ChildItem (Join-Path $root "src") -Filter *.cs | ForEach-Object { $_.FullName }
$output = Join-Path $dist "VidGrab.exe"

& $csc /nologo /codepage:65001 /target:winexe /platform:anycpu "/out:$output" `
    /r:System.Web.Extensions.dll /r:System.Net.Http.dll /r:System.Drawing.dll `
    /r:System.Windows.Forms.dll /r:System.Core.dll `
    "/resource:$build\ytdlp.bin,ytdlp.bin" `
    "/resource:$build\ffmpeg.bin,ffmpeg.bin" `
    $sources

if ($LASTEXITCODE -ne 0) { throw "Compilation failed." }

$hash = (Get-FileHash $output -Algorithm SHA256).Hash
Write-Host ""
Write-Host "Built: $output"
Write-Host ("Size : {0:N1} MB" -f ((Get-Item $output).Length / 1MB))
Write-Host "SHA256: $hash"
Write-Host "Version: $Version (edit src/AssemblyInfo.cs to bump)"
