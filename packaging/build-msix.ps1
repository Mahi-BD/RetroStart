<#
.SYNOPSIS
    Builds a Winly Start MSIX package from the self-contained publish output.

.DESCRIPTION
    Microsoft Store policy 10.2.9 rejects an unsigned .exe, but the Store code-signs MSIX packages
    for free. This script wraps the same self-contained build the Inno Setup installer ships.

    For LOCAL TESTING it also creates a self-signed certificate and signs the package, because
    Windows will not install an unsigned MSIX. That certificate has to be trusted on the test
    machine first (see -InstallCert). For the STORE, submit the *unsigned* package produced with
    -NoSign: Partner Center re-signs it with your publisher identity.

.EXAMPLE
    # test build, signed with a throwaway certificate
    .\build-msix.ps1

.EXAMPLE
    # trust the test certificate on this machine (needs an elevated shell), then install
    .\build-msix.ps1 -InstallCert
    Add-AppxPackage ..\dist\WinlyStart-1.5.1.msix

.EXAMPLE
    # package for Partner Center (Store signs it)
    .\build-msix.ps1 -NoSign -Publisher 'CN=<your Partner Center publisher id>'
#>
[CmdletBinding()]
param(
    [string]$Version = '1.5.1',
    [string]$Publisher = 'CN=WinlyStartDev',
    [switch]$NoSign,
    [switch]$InstallCert
)

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $root 'out\msix'
$dist    = Join-Path $root 'dist'
$msix    = Join-Path $dist "WinlyStart-$Version.msix"

function Find-SdkTool([string]$name) {
    $tool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*\x64\*' } |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $tool) { throw "$name not found - install the Windows SDK." }
    $tool.FullName
}

# ---------------------------------------------------------------- publish
Write-Host '==> publishing self-contained build'
dotnet publish (Join-Path $root 'src\WinlyStart') -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $payload
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

# ---------------------------------------------------------------- lay out the package
Write-Host '==> laying out package'
Copy-Item (Join-Path $PSScriptRoot 'Assets') $payload -Recurse -Force

# Version in an MSIX identity is always four parts and the revision must be 0.
$v = [version]$Version
$appxVersion = '{0}.{1}.{2}.0' -f $v.Major, $v.Minor, $v.Build
$manifest = Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
$manifest = $manifest -replace 'Version="[\d\.]+"', "Version=`"$appxVersion`""
$manifest = $manifest -replace 'Publisher="CN=[^"]+"', "Publisher=`"$Publisher`""
Set-Content (Join-Path $payload 'AppxManifest.xml') $manifest -Encoding UTF8

# The publish drops a .pdb and the deps/runtimeconfig files next to the single-file exe; MSIX
# rejects nothing here, but shipping them just inflates the package.
Get-ChildItem $payload -Include *.pdb, *.xml -Exclude AppxManifest.xml -File -Recurse |
    Where-Object { $_.Name -ne 'AppxManifest.xml' } | Remove-Item -Force -ErrorAction SilentlyContinue

# ---------------------------------------------------------------- pack
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$makeappx = Find-SdkTool 'makeappx.exe'
Write-Host "==> packing with $makeappx"
& $makeappx pack /o /d $payload /p $msix
if ($LASTEXITCODE -ne 0) { throw 'makeappx failed' }

# ---------------------------------------------------------------- sign (local testing only)
if ($NoSign) {
    Write-Host "==> built UNSIGNED: $msix"
    Write-Host '    Submit this to Partner Center; the Store signs it.'
    return
}

$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Publisher } | Select-Object -First 1
if (-not $cert) {
    Write-Host "==> creating a self-signed test certificate for $Publisher"
    $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher `
        -KeyUsage DigitalSignature -FriendlyName 'Winly Start test signing' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}

if ($InstallCert) {
    # Windows only installs an MSIX whose signer it trusts. Needs an elevated shell.
    $tmp = Join-Path $env:TEMP 'winlystart-test.cer'
    Export-Certificate -Cert $cert -FilePath $tmp | Out-Null
    Import-Certificate -FilePath $tmp -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    Write-Host '==> test certificate trusted in LocalMachine\TrustedPeople'
}

$signtool = Find-SdkTool 'signtool.exe'
& $signtool sign /fd SHA256 /a /s My /n ($Publisher -replace '^CN=', '') $msix
if ($LASTEXITCODE -ne 0) { throw 'signtool failed' }

Write-Host ''
Write-Host "==> built and test-signed: $msix"
Write-Host '    Install with:  Add-AppxPackage ' -NoNewline; Write-Host $msix
