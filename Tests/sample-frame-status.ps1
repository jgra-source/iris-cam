# Samples the running Iris app without opening a viewer or touching camera pixels.
# Owners use this to compare receive rate, frame freshness and app CPU before/after a change.
# Frame age starts at C# receipt; this does not measure camera-to-screen latency.
param([ValidateRange(3, 120)][int]$Samples = 21)

$ErrorActionPreference = 'Stop'
$readings = @()
$clock = [Diagnostics.Stopwatch]::StartNew()

# Use actual CPU samples as the cadence, making only one status request per second.
Get-Counter -Counter '\Process(Iris)\% Processor Time' -SampleInterval 1 -MaxSamples $Samples | ForEach-Object {
    $status = curl.exe --insecure --silent --show-error --max-time 5 https://localhost:9443/frame-status | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Iris frame-status request failed.' }
    $readings += [pscustomobject]@{
        Seconds = $clock.Elapsed.TotalSeconds
        Cpu = $_.CounterSamples[0].CookedValue
        Status = $status
    }
}

# Compare counter differences; published includes placeholders, not just new phone frames.
$first = $readings[0]
$last = $readings[-1]
$duration = $last.Seconds - $first.Seconds
# A just-started receiver has no WebRTC stats yet; do not turn missing data into zeroes.
$sourceFirst = if ($first.Status.source) { $first.Status.source | ConvertFrom-Json } else { $null }
$sourceLast = if ($last.Status.source) { $last.Status.source | ConvertFrom-Json } else { $null }
if ($last.Status.sequence -lt $first.Status.sequence -or ($sourceFirst -and $sourceLast -and $sourceLast.framesDecoded -lt $sourceFirst.framesDecoded)) {
    throw 'Iris or its peer restarted during the sample; do not compare these counters.'
}
$ages = @($readings | Where-Object { $_.Status.ageSeconds -ge 0 } | ForEach-Object { $_.Status.ageSeconds } | Sort-Object)

# These values describe this interval only; CPU excludes WebView2 and the camera driver.
[pscustomobject]@{
    SampleCount = $readings.Count
    DurationSeconds = [math]::Round($duration, 2)
    AllLive = (@($readings | Where-Object { -not $_.Status.live }).Count -eq 0)
    ReceivedFps = [math]::Round(($last.Status.sequence - $first.Status.sequence) / $duration, 2)
    PublishedFps = [math]::Round(($last.Status.published - $first.Status.published) / $duration, 2)
    SourceFpsLast = $sourceLast.sourceFps
    DecodedFramesDelta = if ($sourceFirst -and $sourceLast) { $sourceLast.framesDecoded - $sourceFirst.framesDecoded } else { $null }
    DecoderDropsDelta = if ($sourceFirst -and $sourceLast) { $sourceLast.framesDropped - $sourceFirst.framesDropped } else { $null }
    LocalFrameAgeMedianMs = if ($ages.Count) { $ages[[int]($ages.Count / 2)] * 1000 } else { $null }
    LocalFrameAgeP95Ms = if ($ages.Count) { $ages[[int][math]::Ceiling($ages.Count * 0.95) - 1] * 1000 } else { $null }
    LocalFrameAgeMaxMs = if ($ages.Count) { $ages[-1] * 1000 } else { $null }
    IrisCpuPercentOfOneCore = [math]::Round(($readings.Cpu | Measure-Object -Average).Average, 1)
} | Format-List