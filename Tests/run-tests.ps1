<#
.SYNOPSIS
    Builds DMWLT4 (Release), compiles the MwlTest harness against it and runs it.
    Exit code 0 = all checks passed.
#>
param(
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$sln  = Join-Path $repo 'DMWL4\MWL4.sln'
$out  = Join-Path $PSScriptRoot 'bin'

# Locate MSBuild (and the Roslyn compiler next to it) via vswhere.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found. Install Visual Studio or Build Tools.' }
$csc = Join-Path (Split-Path $msbuild -Parent) 'Roslyn\csc.exe'

Write-Host '== Building DMWLT4 (Release)'
& $msbuild $sln /restore /p:Configuration=Release /t:Rebuild /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

# Run the harness next to the app's DLLs, with the app's binding redirects.
if (Test-Path $out) { Remove-Item $out -Recurse -Force -Confirm:$false }
Copy-Item (Join-Path $repo 'DMWL4\bin\Release') $out -Recurse
Copy-Item (Join-Path $out 'DMWLT4.exe.config') (Join-Path $out 'MwlTest.exe.config')

Write-Host '== Compiling MwlTest'
$ref = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
$refs = @(
    "$ref\mscorlib.dll", "$ref\System.dll", "$ref\System.Core.dll", "$ref\System.Configuration.dll",
    "$ref\System.Xaml.dll", "$ref\WindowsBase.dll", "$ref\PresentationCore.dll", "$ref\PresentationFramework.dll",
    "$ref\Facades\netstandard.dll", "$ref\Facades\System.Runtime.dll", "$ref\Facades\System.Threading.Tasks.dll",
    "$ref\Facades\System.Collections.dll", "$ref\System.Net.Http.dll",
    "$out\DMWLT4.exe", "$out\libs\fo-dicom.core.dll", "$out\libs\Microsoft.Extensions.Logging.Abstractions.dll",
    "$out\libs\Microsoft.Bcl.AsyncInterfaces.dll", "$out\libs\System.Threading.Tasks.Extensions.dll",
    "$out\libs\System.ValueTuple.dll"
) | ForEach-Object { "/r:$_" }
& $csc /nologo /noconfig /nostdlib+ /nowarn:1701 /langversion:latest /target:exe `
    "/out:$out\MwlTest.exe" @refs (Join-Path $PSScriptRoot 'MwlTest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compiling MwlTest failed.' }

Write-Host '== Running tests'
$userConfig = Join-Path $env:LOCALAPPDATA 'DMWLT4\user.config'
$hashBefore = (Get-FileHash $userConfig -ErrorAction SilentlyContinue).Hash

$p = Start-Process -FilePath "$out\MwlTest.exe" -WorkingDirectory $out -NoNewWindow -PassThru
$null = $p.Handle  # needed so ExitCode is available afterwards
if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $p.Id -Force -Confirm:$false
    throw "Tests timed out after $TimeoutSeconds s."
}

if ($hashBefore -ne (Get-FileHash $userConfig -ErrorAction SilentlyContinue).Hash) {
    Write-Warning "$userConfig was modified by the test run."
}

exit $p.ExitCode
