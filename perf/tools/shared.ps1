$localPaths = Join-Path $PSScriptRoot "paths.local.ps1"
if (-not (Test-Path $localPaths)) {
    throw "paths.local.ps1 not found at $localPaths. Copy paths.local.ps1.template to paths.local.ps1 in the same directory and fill in your machine-specific paths."
}
. $localPaths

$runnerRelativePath = "\src\Acuminator\Acuminator.Runner.NetFramework\bin\Release\net48\Acuminator.Runner.NetFramework.exe"

$baselineRunnerPath = "$baselineRepoPath$runnerRelativePath"
$subjectRunnerPath  = "$subjectRepoPath$runnerRelativePath"

$targets = @(
    @("PX.Objects.SV", (Join-Path $acumaticaSolutionPath "PX.Objects.SV\PX.Objects.SV.csproj")),
    @("PX.Objects.AM", (Join-Path $acumaticaSolutionPath "PX.Objects.AM\PX.Objects.AM.csproj")),
    @("PX.Objects", (Join-Path $acumaticaSolutionPath "PX.Objects\PX.Objects.csproj"))
)

$runs = @(
    @("Baseline", $baselineRunnerPath),
    @("Subject", $subjectRunnerPath)
)

function Create-Report-Directory {
    param (
        [ValidateNotNullOrEmpty()]
        [string]$prefix
    )
    $currentDateTime = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $reportDirName = "${prefix}_$currentDateTime"
    return New-Item -ItemType Directory -Name $reportDirName
}
