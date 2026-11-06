param([string]$Executable = 'dist/AutoHDRManager.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = (Resolve-Path -LiteralPath (Join-Path $projectRoot $Executable)).Path
$testRoot = Join-Path $projectRoot ('artifacts/portable-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$extractRoot = Join-Path $testRoot 'extracted'
function New-SmokeProcess {
    param([string[]]$Arguments, [switch]$Output)
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $Output.IsPresent
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.Environment['PATH'] = $env:SystemRoot + '\System32'
    $start.Environment['DOTNET_ROOT'] = Join-Path $testRoot 'no-dotnet'
    $start.Environment['DOTNET_ROOT_X64'] = Join-Path $testRoot 'no-dotnet'
    $start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $start.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $extractRoot
    return [Diagnostics.Process]::Start($start)
}
$results = foreach ($file in Get-ChildItem (Join-Path $projectRoot 'tests/AutoHDRManager.Windows.Tests/Fixtures/Video') -File | Where-Object Extension -in '.mkv','.mp4') {
    $worker = New-SmokeProcess -Arguments @('--video-probe', $file.FullName) -Output
    try {
        $outputTask = $worker.StandardOutput.ReadToEndAsync()
        if (!$worker.WaitForExit(20000)) { $worker.Kill(); throw 'Metadata worker timed out' }
        $result = $outputTask.GetAwaiter().GetResult() | ConvertFrom-Json
        $expected = switch ($file.BaseName) { 'hdr10' { 2 } 'hlg' { 3 } 'sdr' { 1 } default { throw 'Unexpected fixture' } }
        if ($worker.ExitCode -ne 0 -or $result.Kind -ne $expected) { throw ('Metadata check failed: ' + $file.Name) }
        [pscustomobject]@{Fixture=$file.Name;Kind=$result.Kind;ExitCode=$worker.ExitCode}
    } finally { $worker.Dispose() }
}
$native = @(Get-ChildItem $extractRoot -Recurse -File -Filter MediaInfo.dll)
if ($native.Count -ne 1 -or (Get-FileHash $native[0].FullName).Hash -ne '8AA4687977E8624DC78504D83C51BEC9FC6CA6A1DF01444FB51D0B61F4C8B29C') { throw 'MediaInfo bundle hash mismatch' }
$files = @(Get-ChildItem $extractRoot -Recurse -File)
foreach ($notice in @('License.html','Embedded-components.txt','ZenLib.txt','TinyXML2.txt','zlib.txt')) {
    if (!($files | Where-Object { $_.Name -eq $notice -and $_.FullName -match 'Licenses[\\/]MediaInfo[\\/]' })) { throw "Missing notice: $notice" }
}
if ($files | Where-Object Name -match '^(settings|games|session)\.json|\.jsonl$|\.mkv$|\.mp4$|^ffmpeg|^ffprobe') { throw 'Unexpected user/test content in bundle' }
$config = Get-Content ($files | Where-Object Name -eq 'AutoHDRManager.runtimeconfig.json').FullName -Raw | ConvertFrom-Json
if (!$config.runtimeOptions.includedFrameworks -or $config.runtimeOptions.frameworks) { throw 'Expected self-contained runtime configuration' }
$profile = Join-Path $testRoot 'profile'
New-Item -ItemType Directory -Path $profile | Out-Null
@{Version=1;Data=@{Monitoring=$false;CloseToTray=$false;Notifications=$false;Language='en-US'}} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $profile 'settings.json')
$gui = New-SmokeProcess -Arguments @('--safe','--ui-test-root',$profile)
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do { Start-Sleep -Milliseconds 250; $gui.Refresh() } while (!$gui.HasExited -and $gui.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($gui.HasExited -or $gui.MainWindowHandle -eq 0) { throw 'Portable WinUI startup failed' }
    if (!$gui.CloseMainWindow() -or !$gui.WaitForExit(15000)) { throw 'Safe UI did not exit normally' }
    if ($gui.ExitCode -ne 0) { throw 'Safe UI failed on exit' }
} finally { if (!$gui.HasExited) { $gui.Kill() }; $gui.Dispose() }
$events = Get-Content (Join-Path $profile 'Logs/current.jsonl') | ForEach-Object { $_ | ConvertFrom-Json }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
if (($events | Where-Object Event -eq 'ApplicationStarted').Details.Version -ne $version) { throw 'Diagnostic build version mismatch' }
if ($events | Where-Object Event -eq 'HdrWriteAttempt') { throw 'Safe UI attempted an HDR write' }
$results | ConvertTo-Json | Set-Content (Join-Path $testRoot 'metadata-results.json')
Write-Output "Portable smoke PASS: six media fixtures, WinUI startup/exit, bundled runtime, MediaInfo and notices. Version $version"
