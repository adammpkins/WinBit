#requires -version 5.1
<#
.SYNOPSIS
  Builds a signed MSIX installer for WinBit using a self-signed cert.

.DESCRIPTION
  Creates a self-signed code-signing certificate for 'CN=adamm' (matches
  Package.appxmanifest Publisher), imports it into the current user's personal
  store so MSBuild's MSIX tooling can use it, signs the Release build with the
  thumbprint, and reports the output path + install commands.

.EXAMPLE
  pwsh -File tools/build-msix.ps1
#>

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64',
    [string]$Subject = 'CN=adamm',
    [string]$PfxPassword = 'winbit'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null

$pfxPath = Join-Path $dist 'winbit-signing.pfx'
$cerPath = Join-Path $dist 'winbit-signing.cer'
$secure = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText

# Generate + export pfx/cer if they don't exist yet.
if (-not (Test-Path $pfxPath)) {
    Write-Host "Generating self-signed cert ($Subject)..."
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -KeyUsage DigitalSignature `
        -FriendlyName 'WinBit Dev Sideload' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $secure | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
    Remove-Item -Path "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Force
    Write-Host "  pfx: $pfxPath"
    Write-Host "  cer: $cerPath"
}

# Import the pfx into the user's personal store so MSBuild can sign via
# thumbprint - /p:PackageCertificateKeyFile with a password often fails under
# the MSIX build task.
$imported = Import-PfxCertificate -FilePath $pfxPath -Password $secure -CertStoreLocation 'Cert:\CurrentUser\My'
$thumbprint = $imported.Thumbprint
Write-Host "Imported cert thumbprint: $thumbprint"

try {
    $vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vsWhere)) {
        throw "vswhere.exe not found - is Visual Studio installed?"
    }
    $msbuild = & $vsWhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) {
        throw "MSBuild not found via vswhere."
    }
    Write-Host "Using MSBuild: $msbuild"

    $csproj = Join-Path $root 'WinBit.csproj'

    # Wipe any prior AppPackages output so we pick up a fresh msix.
    $appPackages = Join-Path $root 'AppPackages'
    if (Test-Path $appPackages) { Remove-Item -Recurse -Force $appPackages }

    & $msbuild $csproj `
        "/p:Configuration=$Configuration" `
        "/p:Platform=$Platform" `
        "/p:RuntimeIdentifier=win-$Platform" `
        '/p:GenerateAppxPackageOnBuild=true' `
        '/p:UapAppxPackageBuildMode=SideloadOnly' `
        '/p:AppxBundle=Never' `
        '/p:AppxPackageSigningEnabled=true' `
        "/p:PackageCertificateThumbprint=$thumbprint" `
        '/nologo' `
        '/v:m'

    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed (exit $LASTEXITCODE)."
    }

    $msix = Get-ChildItem -Path $appPackages -Recurse -Filter '*.msix' | Select-Object -First 1
    if (-not $msix) {
        throw "No .msix produced under $appPackages."
    }

    # Copy into dist/ for a tidy single output location.
    $finalMsix = Join-Path $dist $msix.Name
    Copy-Item -Path $msix.FullName -Destination $finalMsix -Force

    Write-Host ""
    Write-Host "Built: $finalMsix"
    Write-Host "Cert:  $cerPath"
    Write-Host ""
    Write-Host "To install on this machine:"
    Write-Host "  1) (admin, once) Import-Certificate -FilePath '$cerPath' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'"
    Write-Host "  2) Add-AppxPackage -Path '$finalMsix'"
}
finally {
    # Always remove the imported cert when done; the pfx stays on disk for reuse.
    Remove-Item -Path "Cert:\CurrentUser\My\$thumbprint" -Force -ErrorAction SilentlyContinue
}
