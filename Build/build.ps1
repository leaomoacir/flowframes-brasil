<#
  Build do Flow Frames Brasil: compila, monta a pasta de distribuicao e gera o instalador.

  Uso:   powershell -ExecutionPolicy Bypass -File Build\build.ps1 [-Version 1.0.0] [-SkipInstaller]
  Saida: Build\out\FlowFramesBrasil-Setup-<versao>.exe  (e Build\stage = versao "portatil")

  Requisitos: .NET SDK (dotnet) ou Visual Studio/Build Tools, e Inno Setup 6 (para o instalador).
#>
param(
    [string]$Version = "",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$repo   = Resolve-Path (Join-Path $PSScriptRoot "..")
$proj   = Join-Path $repo "CodeLegacy\Flowframes.csproj"
$projDir= Join-Path $repo "CodeLegacy"
$tools  = Join-Path $PSScriptRoot ".tools"
$stage  = Join-Path $PSScriptRoot "stage"
$exeName= "FlowFramesBrasil.exe"

if (-not $Version) { $Version = (Get-Content (Join-Path $repo "version.txt") -TotalCount 1).Trim() }
Write-Host "== Flow Frames Brasil $Version ==" -ForegroundColor Cyan

New-Item -ItemType Directory -Force $tools | Out-Null

# --- 1) nuget.exe + restauracao de pacotes -----------------------------------------------
$nuget = Get-Command nuget.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (-not $nuget) {
    $nuget = Join-Path $tools "nuget.exe"
    if (-not (Test-Path $nuget)) {
        Write-Host "Baixando nuget.exe..."
        Invoke-WebRequest "https://dist.nuget.org/win-x86-commandline/v6.9.1/nuget.exe" -OutFile $nuget
    }
}
Write-Host "Restaurando pacotes NuGet..."
& $nuget restore (Join-Path $projDir "packages.config") -PackagesDirectory (Join-Path $projDir "packages") -NonInteractive | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Falha ao restaurar pacotes NuGet." }

# --- 2) Compilacao ------------------------------------------------------------------------
$msbuild = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
}

Remove-Item (Join-Path $projDir "bin\RelGui") -Recurse -Force -ErrorAction SilentlyContinue

if ($msbuild) {
    Write-Host "Compilando com MSBuild do Visual Studio..."
    & $msbuild $proj /p:Configuration=Release /p:Platform=x64 /v:m /nologo
    if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao." }
}
else {
    Write-Host "Visual Studio nao encontrado: compilando com 'dotnet msbuild' + reference assemblies..."
    $refs = Join-Path $tools "refs"
    $refAsm = Join-Path $refs "Microsoft.NETFramework.ReferenceAssemblies.net472.1.0.3"
    if (-not (Test-Path $refAsm)) {
        & $nuget install Microsoft.NETFramework.ReferenceAssemblies.net472 -Version 1.0.3 -OutputDirectory $refs -NonInteractive | Out-Null
        & $nuget install System.Resources.Extensions -Version 8.0.0 -OutputDirectory $refs -NonInteractive | Out-Null
    }
    $fw  = Join-Path $refAsm "build\.NETFramework\v4.7.2"
    $sma = Get-ChildItem "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Management.Automation" -Recurse -Filter System.Management.Automation.dll | Select-Object -First 1 -ExpandProperty FullName
    $sre = Join-Path $refs "System.Resources.Extensions.8.0.0\lib\net462\System.Resources.Extensions.dll"

    $props = Join-Path $tools "extra.props"
    @"
<Project>
  <PropertyGroup><GenerateResourceUsePreserializedResources>true</GenerateResourceUsePreserializedResources></PropertyGroup>
  <ItemGroup>
    <Reference Include="System.Resources.Extensions"><HintPath>$sre</HintPath></Reference>
    <Reference Include="System.Management.Automation"><HintPath>$sma</HintPath></Reference>
    <Reference Include="$fw\Facades\*.dll" />
  </ItemGroup>
</Project>
"@ | Set-Content $props -Encoding UTF8

    & dotnet msbuild $proj -p:Configuration=Release -p:Platform=x64 "-p:FrameworkPathOverride=$fw" "-p:CustomBeforeMicrosoftCommonTargets=$props" -v:m -nologo
    if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao." }
}

$bin = Join-Path $projDir "bin\RelGui"
if (-not (Test-Path (Join-Path $bin $exeName))) { throw "$exeName nao foi gerado em $bin" }

# --- 3) Pasta de distribuicao -------------------------------------------------------------
Write-Host "Montando pasta de distribuicao..."
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $stage "FlowframesData\pkgs") | Out-Null

Get-ChildItem $bin -File | Where-Object { $_.Extension -notin ".pdb", ".xml" } | Copy-Item -Destination $stage

# O .NET exige este redirect porque o MSBuild do SDK grava System.Resources.Extensions 4.0.0.0 nos recursos.
$cfg = Join-Path $stage "$exeName.config"
$cfgText = Get-Content $cfg -Raw
if ($cfgText -notmatch "System.Resources.Extensions") {
    $redir = '<dependentAssembly><assemblyIdentity name="System.Resources.Extensions" publicKeyToken="cc7b13ffcd2ddd51" culture="neutral" /><bindingRedirect oldVersion="0.0.0.0-8.0.0.0" newVersion="8.0.0.0" /></dependentAssembly>'
    $cfgText = $cfgText -replace "</assemblyBinding>", ($redir + "</assemblyBinding>")
}
$cfgText = $cfgText -replace '0\.0\.0\.0-4\.1\.4\.0" newVersion="4\.1\.4\.0', '0.0.0.0-4.1.6.0" newVersion="4.1.6.0'
Set-Content $cfg $cfgText -Encoding UTF8

foreach ($p in "av","dain-ncnn","licenses","rife-cuda","rife-ncnn","flavr-cuda","xvfi-cuda","rife-ncnn-vs") {
    Copy-Item (Join-Path $repo "Pkgs\$p") (Join-Path $stage "FlowframesData\pkgs\$p") -Recurse
}
Set-Content (Join-Path $stage "FlowframesData\ver.ini") $Version -Encoding ASCII
Copy-Item (Join-Path $repo "LICENSE") (Join-Path $stage "LICENSE.txt")

$mb = [math]::Round(((Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))
Write-Host "Pasta pronta: $stage ($mb MB)" -ForegroundColor Green

# --- 4) Instalador ------------------------------------------------------------------------
if ($SkipInstaller) { return }

$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (-not $iscc) {
    foreach ($c in "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") {
        if (Test-Path $c) { $iscc = $c; break }
    }
}
if (-not $iscc) { throw "Inno Setup 6 nao encontrado. Instale com: winget install JRSoftware.InnoSetup" }

Write-Host "Gerando instalador (pode levar alguns minutos)..."
& $iscc "/DAppVersion=$Version" "/DStageDir=$stage" (Join-Path $repo "Installer\FlowFramesBrasil.iss") | Select-Object -Last 4
if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar o instalador." }

$setup = Join-Path $PSScriptRoot "out\FlowFramesBrasil-Setup-$Version.exe"
Write-Host ("Instalador: {0} ({1} MB)" -f $setup, [math]::Round((Get-Item $setup).Length / 1MB)) -ForegroundColor Green
