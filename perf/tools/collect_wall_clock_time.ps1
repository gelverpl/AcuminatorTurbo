param(
	[ValidateSet("Baseline", "Subject")]
	[string]$Repo
)

. .\shared.ps1

if ($Repo) {
	$runs = @($runs | Where-Object { $_[0] -eq $Repo })
}

$reportDirName = Create-Report-Directory "sw"
$iterations = 5

$timeReportPath = Join-Path -Path $reportDirName -ChildPath "time_report.txt"
"Wall-clock time report" | Out-File -FilePath $timeReportPath

$sw = [System.Diagnostics.Stopwatch]::new()
foreach($target in $targets){
	$projectName = $target[0]
	$projectPath = $target[1]

	# Warm-up: one unmeasured pass per runner to prime JIT and OS file cache.
	foreach($run in $runs){
		$runName = $run[0]
		$runnerPath = $run[1]
		$warmupReportFile = Join-Path -Path $reportDirName -ChildPath "warmup-$runName-$projectName.txt"
		& $runnerPath $projectPath --format json -g d -f $warmupReportFile
	}

	$totalTicks = @{}
	foreach($run in $runs){ $totalTicks[$run[0]] = [long]0 }

	for($i = 1; $i -le $iterations; $i++){
		# Alternate order across iterations to neutralize OS file-cache bias between runners.
		$order = if ($i % 2 -eq 1) { 0..($runs.Length - 1) } else { ($runs.Length - 1)..0 }

		$reportFiles = [System.Collections.Generic.List[string]]::new()
		foreach($idx in $order){
			$runName = $runs[$idx][0]
			$runnerPath = $runs[$idx][1]

			$reportFile = Join-Path -Path $reportDirName -ChildPath "report-$runName-$projectName-iter$i.txt"
			$sw.Restart()
			& $runnerPath $projectPath --format json -g d -f $reportFile
			$sw.Stop()
			$reportFiles.Add($reportFile)
			$totalTicks[$runName] += $sw.Elapsed.Ticks
			"$projectName $runName iter$i $sw" >> $timeReportPath
		}

		if ($reportFiles.Count -ge 2 -and (Get-FileHash $reportFiles[0]).Hash -ne (Get-FileHash $reportFiles[1]).Hash) {
			throw "Report files aren't equal: " + $reportFiles[0] + " " + $reportFiles[1]
		}
	}

	foreach($run in $runs){
		$runName = $run[0]
		$avg = [TimeSpan]::FromTicks([long]($totalTicks[$runName] / $iterations))
		"$projectName $runName average $avg" >> $timeReportPath
	}
}
