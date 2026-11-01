$ErrorActionPreference = "Stop"

$project = Split-Path -Parent $MyInvocation.MyCommand.Path
$managed = "B:\Games\Steam\steamapps\common\Slasta_COTM\Solasta_Data\Managed"
$dotnet = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
$gcc = "C:\msys64\mingw64\bin\gcc.exe"
$compiler = Get-ChildItem (Join-Path $env:ProgramFiles "dotnet\sdk\8.*\Roslyn\bincore\csc.dll") -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName

if (-not (Test-Path (Join-Path $project "source\build.py"))) { throw "source/build.py not found" }
if (-not (Test-Path (Join-Path $managed "Assembly-CSharp.dll"))) { throw "Game Managed libraries not found: $managed" }
if (-not (Test-Path $dotnet)) { throw "dotnet.exe missing" }
if (-not $compiler -or -not (Test-Path $compiler)) { throw "C# compiler missing, install .NET SDK 8" }
if (-not (Test-Path $gcc)) { throw "GCC missing, install MSYS2 mingw-w64-x86_64-gcc" }

$env:PATH = "C:\msys64\mingw64\bin;$env:PATH"
& py -3 (Join-Path $project "source\build.py") --managed $managed --dotnet $dotnet --csc $compiler --cc $gcc
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }

$dll = Join-Path $project "build\winhttp.dll"
if (-not (Test-Path $dll)) { throw "winhttp.dll was not generated" }
Write-Host "Built Solasta Coop View 0.1.15:" -ForegroundColor Green
Get-Item $dll | Select-Object FullName, Length, LastWriteTime
