# Only runs on an ephemeral GitHub-hosted Windows runner. Never use on a user's PC.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'Requires an ephemeral GitHub-hosted runner' }
. (Join-Path $PSScriptRoot 'environment.ps1')
& $Dotnet build-server shutdown
if ($LASTEXITCODE -ne 0) { throw 'Build servers did not stop' }
$allowedParents = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, (Join-Path $ProjectRoot '.tools')) | Where-Object { $_ } | ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\') }
$roots = @($allowedParents | ForEach-Object { Join-Path $_ 'dotnet' } | Where-Object { Test-Path -LiteralPath $_ })
$sdkRoot = Split-Path $Dotnet -Parent
if (!($roots | Where-Object { $sdkRoot -eq $_ -or $sdkRoot.StartsWith($_ + '\', [StringComparison]::OrdinalIgnoreCase) })) { throw 'SDK path is outside the explicit temporary isolation roots' }
$renamed = @()
try {
    foreach ($root in $roots) {
        $resolved = (Resolve-Path -LiteralPath $root).Path
        $parent = Split-Path $resolved -Parent
        if ($parent -notin $allowedParents -or (Split-Path $resolved -Leaf) -ne 'dotnet') { throw 'Unsafe isolation path' }
        $newName = 'dotnet-portable-smoke-' + [guid]::NewGuid().ToString('N')
        $destination = [IO.Path]::GetFullPath((Join-Path $parent $newName))
        if ((Split-Path $destination -Parent) -ne $parent) { throw 'Unsafe isolation destination' }
        Rename-Item -LiteralPath $resolved -NewName $newName
        $renamed += [pscustomobject]@{Original=$resolved;Temporary=$destination}
    }
    if ($roots | Where-Object { Test-Path -LiteralPath $_ }) { throw 'A global SDK/runtime root is still available' }
    Write-Output 'Installed SDK/runtime roots are temporarily unavailable for this portable test.'
    & (Join-Path $PSScriptRoot 'test-portable.ps1')
} finally {
    foreach ($entry in $renamed) { Rename-Item -LiteralPath $entry.Temporary -NewName (Split-Path $entry.Original -Leaf) }
}
