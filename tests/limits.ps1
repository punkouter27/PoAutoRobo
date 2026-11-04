# Fails when a kind of test passes its cap, so the suite stays small enough to read and quick to run. One test may
# cover several cases of one behaviour; a new one past the cap means folding it into one already there.
# Run after a build: pwsh tests/limits.ps1 [-Configuration Release]
param([string]$Configuration = "Debug")

$caps = [ordered]@{
    "Unit"        = @{ Filter = "Category!=Integration&Category!=Live"; Cap = 100 }
    "Integration" = @{ Filter = "Category=Integration"; Cap = 50 }  # run FFmpeg and Edge for real
    "Live"        = @{ Filter = "Category=Live"; Cap = 25 }         # call real services; the app has no API of its own
}

$solution = Join-Path $PSScriptRoot "..\PoAutoRobo.slnx"
$over = $false
foreach ($kind in $caps.Keys) {
    $listed = dotnet test $solution --configuration $Configuration --no-build --list-tests --filter $caps[$kind].Filter 2>&1
    if ($LASTEXITCODE -ne 0) { $listed; throw "Could not list the $kind tests. Build first." }
    $count = @($listed | Where-Object { $_ -match '^\s{4}\S' }).Count
    $cap = $caps[$kind].Cap
    Write-Output ("{0,-12} {1,3} of {2}" -f $kind, $count, $cap)
    if ($count -gt $cap) { $over = $true }
}
if ($over) { Write-Error "Too many tests: fold the new ones into tests already there."; exit 1 }
