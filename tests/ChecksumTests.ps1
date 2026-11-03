[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskFixture = Join-Path ([IO.Path]::GetTempPath()) ('KlasorGecis-hash-' + [Guid]::NewGuid().ToString('N'))
$taskScript = Join-Path (Split-Path $PSScriptRoot -Parent) 'build.ps1'
$taskShell = (Get-Process -Id $PID).Path
$taskSourceName = 'src\' + [char]0x00f6 + 'rnek.cs'
$taskActiveLog = $null

function Invoke-Checksum([string]$Mode, [bool]$ShouldPass) {
    # PS5 turns native stderr into ErrorRecords. Negative test cases must reach
    # the exit-code assertion rather than aborting the parent harness early.
    $ErrorActionPreference = 'Continue'
    $output = & $taskShell -NoProfile -File $taskScript $Mode -PackageRoot $taskFixture 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if (($code -eq 0) -ne $ShouldPass) { throw "Ozet regresyonu: $Mode exit=$code; $output" }
}

try {
    [IO.Directory]::CreateDirectory((Join-Path $taskFixture 'release\win-x64')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $taskFixture 'src')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $taskFixture 'release\win-x64\KlasorGecis.exe'), 'synthetic executable fixture')
    [IO.File]::WriteAllText((Join-Path $taskFixture 'README_TR.md'), 'sentetik belge')
    [IO.File]::WriteAllText((Join-Path $taskFixture $taskSourceName), 'sentetik kaynak')
    Copy-Item -LiteralPath $taskScript -Destination (Join-Path $taskFixture 'build.ps1')
    [IO.Directory]::CreateDirectory((Join-Path $taskFixture 'verification')) | Out-Null
    $taskActiveLog = [IO.File]::Open((Join-Path $taskFixture 'verification\feature-active-log.txt'),
        [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    Invoke-Checksum '-RefreshChecksums' $true
    Invoke-Checksum '-VerifyChecksums' $true
    $taskDefaultOutput = & $taskShell -NoProfile -File (Join-Path $taskFixture 'build.ps1') -VerifyChecksums 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Default package root failed: $taskDefaultOutput" }
    # Public checkouts use README.md; legacy local packages remain supported.
    Move-Item -LiteralPath (Join-Path $taskFixture 'README_TR.md') -Destination (Join-Path $taskFixture 'README.md')
    Invoke-Checksum '-RefreshChecksums' $true
    Invoke-Checksum '-VerifyChecksums' $true
    [IO.File]::AppendAllText((Join-Path $taskFixture 'README.md'), 'changed')
    Invoke-Checksum '-VerifyChecksums' $false
    Invoke-Checksum '-RefreshChecksums' $true
    [IO.File]::AppendAllText((Join-Path $taskFixture 'release\win-x64\KlasorGecis.exe'), 'changed')
    Invoke-Checksum '-VerifyChecksums' $false
    Invoke-Checksum '-RefreshChecksums' $true
    Invoke-Checksum '-VerifyChecksums' $true
    [IO.File]::AppendAllText((Join-Path $taskFixture $taskSourceName), 'changed')
    Invoke-Checksum '-VerifyChecksums' $false
    Invoke-Checksum '-RefreshChecksums' $true
    [IO.File]::AppendAllText((Join-Path $taskFixture 'SHA256SUMS.txt'), ('0' * 64) + '  ../outside.txt')
    Invoke-Checksum '-VerifyChecksums' $false
    Write-Host 'PASS legacy/public README checksums, modified README/EXE/source detection, refresh and invalid path rejection'
}
finally {
    if ($null -ne $taskActiveLog) { $taskActiveLog.Dispose() }
    $taskResolved = [IO.Path]::GetFullPath($taskFixture)
    $taskTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $taskResolved.StartsWith($taskTempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $taskResolved) { Remove-Item -LiteralPath $taskResolved -Recurse -Force }
}
exit 0
