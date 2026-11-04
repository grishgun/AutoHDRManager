param([string]$Destination)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$Destination) { $Destination = Join-Path $projectRoot ('artifacts\github-source-1.2.3-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$exportRoot = [IO.Path]::GetFullPath($Destination)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (!$exportRoot.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Export destination must be inside workspace artifacts.' }
if (Test-Path -LiteralPath $exportRoot) { throw 'Use a new destination; existing exports are never overwritten.' }
New-Item -ItemType Directory -Path $exportRoot | Out-Null
$rootNames = @('README.md','README.ru.md','CHANGELOG.md','CONTRIBUTING.md','SECURITY.md','THIRD-PARTY-NOTICES.md','BINARY-TERMS.md','LICENSE','.gitignore','AutoHDRManager.sln','Directory.Build.props','global.json')
$docNames = @('ARCHITECTURE.md','BRANDING.md','BUILD-ENVIRONMENT.md','DEPENDENCIES.md','HDR-COMPATIBILITY.md','TASKBAR.md','TASKBAR-GROUPING.md','TESTING.md','PUBLIC-RELEASE-1.2.3.md')
$files = @($rootNames | ForEach-Object { Get-Item -LiteralPath (Join-Path $projectRoot $_) })
$files += @($docNames | ForEach-Object { Get-Item -LiteralPath (Join-Path $projectRoot ('docs\' + $_)) })
foreach ($name in @('src','tests','scripts','.github','docs\licenses','docs\screenshots\1.2.3')) {
    $files += Get-ChildItem -LiteralPath (Join-Path $projectRoot $name) -File -Recurse -Force | Where-Object {
        $_.FullName -notmatch '\\(bin|obj|TestResults|Covers|Logs|Launcher|Shortcuts)\\' -and
        $_.Name -notmatch '^(settings|games|session)\.json|^cover-key\.bin$' -and
        $_.Extension -in @('.cs','.csproj','.xaml','.resx','.manifest','.png','.ico','.md','.txt','.ps1','.yml','.yaml')
    }
}
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($projectRoot, $file.FullName)
    $target = Join-Path $exportRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
$manifest = $files | ForEach-Object { [pscustomobject]@{ Path=[IO.Path]::GetRelativePath($projectRoot,$_.FullName); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } }
$manifest | ConvertTo-Json | Set-Content -LiteralPath ($exportRoot + '-manifest.json')
Write-Output $exportRoot
Write-Output ('Files: ' + $files.Count)
Write-Output 'Allowlisted source export only. Scan files/history and inspect screenshots before publishing. Release binaries are separate assets.'
