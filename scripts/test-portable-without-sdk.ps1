# Only runs on an ephemeral GitHub-hosted Windows runner. Never use on a user's PC.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'Requires an ephemeral GitHub-hosted runner' }
. (Join-Path $PSScriptRoot 'environment.ps1')
$dotnetRoots = @($env:ProgramFiles, [Environment]::GetEnvironmentVariable('ProgramFiles(x86)'), (Join-Path $ProjectRoot '.tools')) | Where-Object { $_ } | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $_ 'dotnet')).TrimEnd('\') }
$allowedParents = @($dotnetRoots) + @($dotnetRoots | ForEach-Object { Join-Path $_ 'x64' })
$roots = @($allowedParents | ForEach-Object { Join-Path $_ 'sdk' } | Where-Object { Test-Path -LiteralPath $_ })
if (!$roots.Count) { throw 'Expected installed SDKs before isolation' }
$sdkRoot = Split-Path $Dotnet -Parent
if ($sdkRoot -notin $allowedParents) { throw 'SDK path is outside the explicit temporary isolation roots' }
$renamed = @()
try {
    foreach ($root in $roots) {
        $resolved = (Resolve-Path -LiteralPath $root).Path
        $parent = Split-Path $resolved -Parent
        if ($parent -notin $allowedParents -or (Split-Path $resolved -Leaf) -ne 'sdk') { throw 'Unsafe isolation path' }
        $newName = 'sdk-portable-smoke-' + [guid]::NewGuid().ToString('N')
        $destination = [IO.Path]::GetFullPath((Join-Path $parent $newName))
        if ((Split-Path $destination -Parent) -ne $parent) { throw 'Unsafe isolation destination' }
        Rename-Item -LiteralPath $resolved -NewName $newName
        $renamed += [pscustomobject]@{Original=$resolved;Temporary=$destination}
    }
    if ($roots | Where-Object { Test-Path -LiteralPath $_ }) { throw 'An SDK directory is still available' }
    $visibleSdks = & $Dotnet --list-sdks
    if ($LASTEXITCODE -ne 0 -or $visibleSdks) { throw 'SDK enumeration should be empty during isolation' }
    Write-Output 'SDK enumeration is empty; installed SDK directories are temporarily unavailable.'
    & (Join-Path $PSScriptRoot 'test-portable.ps1')
} finally {
    foreach ($entry in $renamed) { Rename-Item -LiteralPath $entry.Temporary -NewName (Split-Path $entry.Original -Leaf) }
}
