param(
	[ValidateSet("Baseline", "Subject")]
	[string]$Repo,
	[ValidateSet("GCOnly", "Sampled")]
	[string]$Type = "GCOnly"
)

. .\shared.ps1

if ($Repo) {
	$runs = @($runs | Where-Object { $_[0] -eq $Repo })
}

function Collect {
	param (
		[ValidateNotNullOrEmpty()]
		[string]$prefix,
		[bool]$simple
	)
	$reportDirName = Create-Report-Directory $prefix

	foreach($target in $targets){
		$projectName = $target[0]
		$projectPath = $target[1]

		foreach($run in $runs){
			$runName = $run[0]
			$runnerPath = $run[1]
			$runnerFileName = [io.path]::GetFileName($runnerPath)
			# Run
			$reportPath = Join-Path -Path $reportDirName -ChildPath $projectName$runName.etl
			if($simple) {
				& $perfViewPath /DataFile:$reportPath /GCCollectOnly /Zip:false /Merge:false /OnlyProviders:"*Microsoft-Windows-DotNETRuntime:@ProcessNameFilter=$runnerFileName" /NoView /AcceptEULA run $runnerPath $projectPath --format json -g d | Out-Null
			} else {
				& $perfViewPath /DataFile:$reportPath /GCOnly /DotNetAllocSampled /Zip:false /Merge:false /OnlyProviders:"*Microsoft-Windows-DotNETRuntime:@ProcessNameFilter=$runnerFileName" /NoView /AcceptEULA run $runnerPath $projectPath --format json -g d | Out-Null
			}
			
		}
	}
}

Collect "gc_$Type" ($Type -eq "GCOnly")