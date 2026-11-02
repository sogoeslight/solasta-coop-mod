param(
    [string]$ManagedPath = $env:SOLASTA_MANAGED,
    [string]$DotnetPath = "",
    [string]$GccPath = ""
)

$ErrorActionPreference = "Stop"
$project = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $defaultSteam = Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common'
    $candidates = @(
        (Join-Path $defaultSteam 'Slasta_COTM\Solasta_Data\Managed'),
        (Join-Path $defaultSteam 'Solasta_COTM\Solasta_Data\Managed')
    )
    $ManagedPath = $candidates | Where-Object { Test-Path (Join-Path $_ 'Assembly-CSharp.dll') } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($ManagedPath) -or -not (Test-Path (Join-Path $ManagedPath 'Assembly-CSharp.dll'))) {
    throw 'Cannot find game assemblies. Pass -ManagedPath "X:\SteamLibrary\steamapps\common\Solasta_COTM\Solasta_Data\Managed" or set SOLASTA_MANAGED.'
}

if ([string]::IsNullOrWhiteSpace($DotnetPath)) {
    $DotnetPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
}
if ([string]::IsNullOrWhiteSpace($GccPath)) {
    $GccPath = 'C:\msys64\mingw64\bin\gcc.exe'
}
if (-not (Test-Path $DotnetPath)) { throw ".NET SDK executable missing: $DotnetPath" }
if (-not (Test-Path $GccPath)) { throw "MinGW GCC executable missing: $GccPath" }

$compiler = Get-ChildItem (Join-Path $env:ProgramFiles 'dotnet\sdk\8.*\Roslyn\bincore\csc.dll') -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $compiler) { throw 'C# compiler missing, install .NET SDK 8.' }
if (-not (Test-Path (Join-Path $project 'source\build.py'))) { throw 'source\build.py missing.' }

$env:PATH = "$(Split-Path -Parent $GccPath);$env:PATH"
$py = Get-Command py -ErrorAction SilentlyContinue
if ($py) {
    & $py.Source -3 (Join-Path $project 'source\build.py') --managed $ManagedPath --dotnet $DotnetPath --csc $compiler --cc $GccPath
} else {
    $python = Get-Command python -ErrorAction Stop
    & $python.Source (Join-Path $project 'source\build.py') --managed $ManagedPath --dotnet $DotnetPath --csc $compiler --cc $GccPath
}
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

$dll = Join-Path $project 'build\winhttp.dll'
if (-not (Test-Path $dll)) { throw 'Build finished without generating build\winhttp.dll.' }
Write-Host 'Built Solasta Coop Mod:' -ForegroundColor Green
Get-Item $dll | Select-Object FullName, Length, LastWriteTime
