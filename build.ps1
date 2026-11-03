# Run from an ordinary Windows PowerShell session. This script never starts GUI tests.
[CmdletBinding()]
param(
    [switch]$VerifyChecksums,
    [switch]$RefreshChecksums,
    [string]$PackageRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrEmpty($PackageRoot)) { $PackageRoot = $PSScriptRoot }

function Get-PackageFiles([string]$Root) {
    $names = [Collections.Generic.List[string]]::new()
    $readme = if (Test-Path -LiteralPath (Join-Path $Root 'README.md') -PathType Leaf) { 'README.md' } else { 'README_TR.md' }
    foreach ($relative in @($readme, 'build.ps1', 'release/win-x64/KlasorGecis.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $relative) -PathType Leaf)) {
            throw "Paket dosyasi bulunamadi: $relative"
        }
        $names.Add($relative)
    }
    foreach ($relative in @('LICENSE', '.gitignore', 'THIRD-PARTY-NOTICES.md', 'global.json', '.github/workflows/build.yml')) {
        if (Test-Path -LiteralPath (Join-Path $Root $relative) -PathType Leaf) { $names.Add($relative) }
    }
    foreach ($directory in @('src', 'tests', 'docs', 'installer', 'licenses')) {
        $path = Join-Path $Root $directory
        if (-not (Test-Path -LiteralPath $path -PathType Container)) { continue }
        Get-ChildItem -LiteralPath $path -Recurse -File | ForEach-Object {
            $relative = $_.FullName.Substring($Root.TrimEnd('\', '/').Length + 1).Replace('\', '/')
            if ($relative -notmatch '/(bin|obj)/') { $names.Add($relative) }
        }
    }
    $verification = Join-Path $Root 'verification'
    if (Test-Path -LiteralPath $verification) {
        Get-ChildItem -LiteralPath $verification -File -Filter 'feature-*' |
            Where-Object { $_.Extension -in @('.json', '.png') } |
            ForEach-Object { $names.Add('verification/' + $_.Name) }
    }
    return @($names | Sort-Object -Unique)
}

function Update-PackageChecksums([string]$Root, [bool]$Verify) {
    $Root = [IO.Path]::GetFullPath($Root)
    $manifest = Join-Path $Root 'SHA256SUMS.txt'
    $files = @(Get-PackageFiles $Root)
    if ($Verify) {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($line in Get-Content -LiteralPath $manifest -Encoding UTF8) {
            if ($line -notmatch '^([a-fA-F0-9]{64})  (.+)$') { throw 'Gecersiz SHA256SUMS satiri.' }
            $expected = $Matches[1]; $relative = $Matches[2]
            if ($relative -cnotin $files -or -not $seen.Add($relative)) { throw "Gecersiz/tekrarli paket yolu: $relative" }
            $actual = (Get-FileHash -LiteralPath (Join-Path $Root $relative) -Algorithm SHA256).Hash
            if ($actual -ne $expected) { throw "SHA-256 uyusmazligi: $relative" }
        }
        if ($seen.Count -ne $files.Count) { throw 'SHA256SUMS paket dosyalarinin tamamini kapsamiyor.' }
        Write-Host "SHA-256 dogrulandi: $($files.Count) dosya."
    }
    else {
        $lines = foreach ($relative in $files) {
            $hash = (Get-FileHash -LiteralPath (Join-Path $Root $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relative"
        }
        $temporary = $manifest + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        try {
            [IO.File]::WriteAllLines($temporary, [string[]]$lines, [Text.UTF8Encoding]::new($false))
            Move-Item -LiteralPath $temporary -Destination $manifest -Force
        }
        finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
        Write-Host "SHA256SUMS guncellendi: $($files.Count) dosya."
    }
}

if ($VerifyChecksums -and $RefreshChecksums) { throw 'Tek bir ozet islemi secin.' }
if ($VerifyChecksums -or $RefreshChecksums) {
    Update-PackageChecksums $PackageRoot $VerifyChecksums.IsPresent
    return
}
if ([IO.Path]::GetFullPath($PackageRoot) -ne [IO.Path]::GetFullPath($PSScriptRoot)) {
    throw 'PackageRoot yalnizca ozet islemleriyle kullanilir.'
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Bu proje Windows uzerinde derlenmelidir.'
}

$dotnetCommand = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw '.NET 10 SDK bulunamadi. SDK kurulduktan sonra yeni bir PowerShell penceresi acin.'
}
$dotnetPath = $dotnetCommand.Source
$testsProject = Join-Path $PSScriptRoot 'tests\KlasorGecis.Tests\KlasorGecis.Tests.csproj'
$appProject = Join-Path $PSScriptRoot 'src\KlasorGecis\KlasorGecis.csproj'
$publishDirectory = Join-Path $PSScriptRoot 'release\win-x64'

Push-Location -LiteralPath $PSScriptRoot
try {
    Write-Host 'Kullanilan .NET SDK:'
    & $dotnetPath --version
    if ($LASTEXITCODE -ne 0) { throw 'SDK surumu okunamadi.' }

    Write-Host 'Uygulama ve test projesi yeniden derleniyor...'
    & $dotnetPath build $testsProject -c Release --no-incremental
    if ($LASTEXITCODE -ne 0) { throw "Derleme basarisiz. Cikis kodu: $LASTEXITCODE" }

    Write-Host 'Pencere acmayan kural ve regresyon kontrolleri calistiriliyor...'
    & $dotnetPath run --project $testsProject -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Kural kontrolleri basarisiz. Cikis kodu: $LASTEXITCODE" }

    & (Join-Path $PSScriptRoot 'tests\ChecksumTests.ps1')

    Write-Host 'Windows x64 icin calisma zamani dahil tasinabilir EXE hazirlaniyor...'
    & $dotnetPath publish $appProject -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw "Yayinlama basarisiz. Cikis kodu: $LASTEXITCODE" }

    # Notices are reviewed for this runtime; fail closed on SDK/runtime updates.
    $assets = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\KlasorGecis\obj\project.assets.json') -Raw | ConvertFrom-Json
    foreach ($framework in $assets.project.frameworks.PSObject.Properties.Value) {
        foreach ($dependency in $framework.downloadDependencies) {
            if ($dependency.name -in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64') -and
                $dependency.version -ne '[10.0.8, 10.0.8]') {
                throw 'Runtime changed: review licenses and update the 10.0.8 notice guard before distributing.'
            }
        }
    }
    $exePath = Join-Path $publishDirectory 'KlasorGecis.exe'
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        throw 'Yayinlama sona erdi ancak beklenen EXE bulunamadi.'
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE'), (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishDirectory -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $publishDirectory -Recurse -Force
    $installerNotices = Join-Path $publishDirectory 'licenses\installer'
    [IO.Directory]::CreateDirectory($installerNotices) | Out-Null
    foreach ($name in @('LICENSE-InnoSetup.txt', 'README.md', 'ChineseSimplified.isl', 'Hindi.islu')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('installer\Languages\' + $name)) -Destination $installerNotices -Force
    }
    $hash = Get-FileHash -LiteralPath $exePath -Algorithm SHA256
    Update-PackageChecksums $PSScriptRoot $false
    Write-Host ''
    Write-Host "Hazir: $exePath"
    Write-Host "SHA256: $($hash.Hash)"
    Write-Host 'Canli pencere testleri otomatik baslatilmadi; README dosyasindaki komutlari kullanabilirsiniz.'
}
finally {
    Pop-Location
}
