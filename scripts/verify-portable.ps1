param([int]$ObserveSeconds = 20)
. (Join-Path $PSScriptRoot 'environment.ps1')
$exe = Join-Path $ProjectRoot 'dist\AutoHDRManager.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Build dist/AutoHDRManager.exe first.' }
if (Get-Process AutoHDRManager -ErrorAction SilentlyContinue) { throw 'Exit the existing application before portable verification.' }
$destination = Join-Path $ProjectRoot ('artifacts\portable-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $destination | Out-Null
Copy-Item -LiteralPath $exe -Destination $destination
$process = Start-Process -FilePath (Join-Path $destination 'AutoHDRManager.exe') -ArgumentList @('--safe', '--ui-test-root', ('"' + (Join-Path $destination 'Profile') + '"')) -WorkingDirectory $destination -PassThru
Start-Sleep -Seconds $ObserveSeconds
$process.Refresh()
if ($process.HasExited) { throw "Portable application exited: $($process.ExitCode)" }
[pscustomobject]@{Result='RUNNING'; PID=$process.Id; Directory=$destination; Files=@(Get-ChildItem -LiteralPath $destination -File).Count; WorkingSetMB=[math]::Round($process.WorkingSet64/1MB,1); Handles=$process.HandleCount}
Write-Host 'Inspect the visible UI, then use its Exit button. This is a same-machine smoke test, not a clean-VM runtime-independence test.'

