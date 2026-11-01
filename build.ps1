param([switch]$Preparation)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$release = Get-Content -LiteralPath (Join-Path $taskRoot 'release-lock.json') -Raw | ConvertFrom-Json
if (-not $release.reviewed -and -not $Preparation) {
    throw 'Falta preparar el mod oficial con tools/seal_release.py. -Preparation solo compila una vista previa bloqueada.'
}
dotnet run --project (Join-Path $taskRoot 'tests/Checks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas.' }
py -m unittest discover -s (Join-Path $taskRoot 'tests') -p 'test_*.py'
if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas de requisitos.' }
dotnet publish (Join-Path $taskRoot 'src/ExtremeInstaller.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $taskRoot 'dist-auto')
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación.' }
Get-FileHash -LiteralPath (Join-Path $taskRoot 'dist-auto/InstalarExtremo.exe') -Algorithm SHA256 | Format-List | Out-File -LiteralPath (Join-Path $taskRoot 'dist-auto/SHA256.txt')
