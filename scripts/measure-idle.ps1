param([int]$Seconds = 30)
$processes = @(Get-Process AutoHDRManager -ErrorAction Stop)
if ($processes.Count -ne 1) { throw 'Expected exactly one AutoHDRManager process.' }
$process = $processes[0]
$process.Refresh()
$startCpu = $process.TotalProcessorTime.TotalSeconds
$watch = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds $Seconds
$watch.Stop()
$process.Refresh()
[pscustomobject]@{
  PID=$process.Id; Seconds=$watch.Elapsed.TotalSeconds
  CpuPercentOfOneCore=[math]::Round(100*($process.TotalProcessorTime.TotalSeconds-$startCpu)/$watch.Elapsed.TotalSeconds,3)
  CpuPercentOfMachine=[math]::Round(100*($process.TotalProcessorTime.TotalSeconds-$startCpu)/$watch.Elapsed.TotalSeconds/[Environment]::ProcessorCount,3)
  WorkingSetMB=[math]::Round($process.WorkingSet64/1MB,2)
  PrivateMB=[math]::Round($process.PrivateMemorySize64/1MB,2)
  Handles=$process.HandleCount; Threads=$process.Threads.Count
}
