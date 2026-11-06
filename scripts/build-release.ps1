param([switch]$SkipTests, [switch]$Headless)
. (Join-Path $PSScriptRoot 'environment.ps1')
Push-Location $ProjectRoot
try {
    if (!$SkipTests) {
        & $Dotnet test tests\AutoHDRManager.Tests -c Release --logger 'trx;LogFileName=unit.trx' --results-directory artifacts\tests
        if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
        $testArguments = @('test', 'tests\AutoHDRManager.Windows.Tests', '-c', 'Release', '--logger', 'trx;LogFileName=windows.trx', '--results-directory', 'artifacts\tests')
        $filters = @()
        if ($Headless) { $filters += 'FullyQualifiedName!~ActiveDisplayCanBeReadWithoutWrites' }
        if ($filters.Count) { $testArguments += @('--filter', ($filters -join '&')) }
        & $Dotnet @testArguments
        if ($LASTEXITCODE -ne 0) { throw 'Windows integration tests failed.' }
    }
    $publish = Join-Path $ProjectRoot ('artifacts\publish-' + [guid]::NewGuid().ToString('N'))
    & $Dotnet publish src\AutoHDRManager.App -c Release -r win-x64 -p:Platform=x64 -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $files = @(Get-ChildItem -LiteralPath $publish -File -Recurse)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'AutoHDRManager.exe') {
        $files | Select-Object FullName
        throw 'Single-file validation failed: publish output must contain only AutoHDRManager.exe.'
    }
    $dist = Join-Path $ProjectRoot 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    Copy-Item -LiteralPath $files[0].FullName -Destination (Join-Path $dist 'AutoHDRManager.exe') -Force
    Get-FileHash -LiteralPath (Join-Path $dist 'AutoHDRManager.exe') -Algorithm SHA256
} finally { Pop-Location }
