<#
.SYNOPSIS
Runs the RecDiffy capacity harness on the capacity host and publishes the run.

.DESCRIPTION
One run at a time, by hand, on COBBLER1 (issue #377, phase 6).

  1. Fetches the ref (default origin/dev) and resolves it to a commit.
  2. Checks that commit out clean into a worktree under WorkRoot (G:).
  3. Builds the capacity harness in Release, with warnings as errors.
  4. Refuses to run while the cobbler1 GitHub runner is busy, or while the host is busy.
  5. Runs the chosen sweep with the chosen GC mode, heap cap and per-case timeout.
  6. Fails the run if results.json, report.md or report.html is missing or empty.
  7. Commits the reports and logs to runs/<run-id>/ on the orphan branch capacity-results.
  8. Posts the Markdown summary to the issue with gh, linking the run folder.
  9. Leaves the run folder on G: and removes the temporary worktrees; on failure it keeps them.

No GitHub Actions workflow starts this. Nothing heavy runs on GitHub-hosted runners.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File RecDiffy.NET\capacity\run-capacity.ps1 -Ref origin/dev

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File RecDiffy.NET\capacity\run-capacity.ps1 -Ref origin/dev -Sweep concurrency -Levels auto -ConcurrencyRows 10000

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File RecDiffy.NET\capacity\run-capacity.ps1 -Ref origin/dev -Sizes 100000,1000000 -HeapCap 16GB -NoExcel -Repeats 3
#>
[CmdletBinding()]
param(
    [string]$Ref = 'origin/dev',
    [ValidateSet('sizes', 'concurrency')]
    [string]$Sweep = 'sizes',
    [string]$Sizes = '10000,100000',
    [string]$Levels = 'auto',
    [int]$ConcurrencyRows = 10000,
    [int]$Repeats = 1,
    [ValidateSet('workstation', 'server')]
    [string]$GcMode = 'workstation',
    [string]$HeapCap = '',
    [int]$CaseTimeoutSeconds = 1800,
    [switch]$NoExcel,
    [ValidateSet('jit', 'r2r', 'aot')]
    [string]$Flavor = 'jit',
    [ValidateSet('net10.0', 'net8.0')]
    [string]$Framework = 'net10.0',
    [string]$Runtime = 'win-x64',
    [string]$WorkRoot = 'G:\RecDiffyCapacity',
    [string]$Remote = 'origin',
    [string]$ResultsBranch = 'capacity-results',
    [string]$Issue = '377',
    [string]$RequireHost = 'COBBLER1',
    [int]$MaxBackgroundPercent = 10,
    [switch]$NoFetch,
    [switch]$SkipHostCheck,
    [switch]$SkipRunnerCheck,
    [switch]$SkipPush,
    [switch]$SkipComment,
    [switch]$KeepCheckout,
    [switch]$KeepResults
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:LogPath = $null

function Write-Log {
    param([string]$Message)
    $line = '[{0}] {1}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $line
    if ($script:LogPath) {
        Add-Content -LiteralPath $script:LogPath -Value $line -Encoding utf8
    }
}

function Fail {
    param([string]$Message)
    Write-Log "FAILED: $Message"
    throw $Message
}

function Invoke-Captured {
    param(
        [string]$File,
        [string[]]$Arguments,
        [string]$What,
        [string]$LogFile,
        [switch]$Echo
    )
    Write-Log "$What`: $File $($Arguments -join ' ')"
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $File @Arguments 2>&1
    }
    finally {
        $ErrorActionPreference = $previous
    }
    $code = $LASTEXITCODE
    if ($LogFile -and $output) {
        $output | Out-File -LiteralPath $LogFile -Append -Encoding utf8
    }
    if ($Echo -or $code -ne 0) {
        $output | ForEach-Object { Write-Log "  $_" }
    }
    return [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function Invoke-Tool {
    param(
        [string]$File,
        [string[]]$Arguments,
        [string]$What,
        [string]$LogFile
    )
    $result = Invoke-Captured -File $File -Arguments $Arguments -What $What -LogFile $LogFile
    if ($result.ExitCode -ne 0) {
        Fail "$What exited $($result.ExitCode)"
    }
    return $result.Output
}

function Invoke-Git {
    param([string[]]$Arguments, [string]$What)
    return Invoke-Tool -File 'git' -Arguments $Arguments -What $What
}

function Remove-Worktree {
    param([string]$Repo, [string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
        Invoke-Git -Arguments @('-C', $Repo, 'worktree', 'remove', '--force', $Path) -What 'remove worktree' | Out-Null
    }
    catch {
        Write-Log "worktree remove failed for $Path; deleting the folder"
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Remove-StaleResultsWorktree {
    param([string]$Repo, [string]$Branch)
    $lines = Invoke-Git -Arguments @('-C', $Repo, 'worktree', 'list', '--porcelain') -What 'list worktrees'
    $current = $null
    foreach ($line in $lines) {
        if ($line -like 'worktree *') {
            $current = $line.Substring(9).Trim()
        }
        elseif ($current -and $line -eq "branch refs/heads/$Branch") {
            Write-Log "removing the worktree that still holds $Branch`: $current"
            Remove-Worktree -Repo $Repo -Path $current
            $current = $null
        }
    }
}

function Test-Ref {
    param([string]$Repo, [string]$Name)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & git -C $Repo rev-parse --verify --quiet $Name 2>&1 | Out-Null
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    return ($code -eq 0)
}

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot '.git'))) {
    Fail "The script must live inside the repository; $repoRoot is not a checkout."
}

if (-not $SkipHostCheck -and $env:COMPUTERNAME -ne $RequireHost) {
    Fail "Run this on $RequireHost. This is $env:COMPUTERNAME. Pass -SkipHostCheck only to test the script itself."
}
$drive = Split-Path -Qualifier $WorkRoot
if (-not (Test-Path -LiteralPath ($drive + '\'))) {
    Fail "The work root drive $drive does not exist. Put WorkRoot on G: on COBBLER1."
}
if (-not (Test-Path -LiteralPath $WorkRoot)) {
    New-Item -ItemType Directory -Path $WorkRoot -Force | Out-Null
}

$capBytes = [uint64]0
if ($HeapCap) {
    if ($HeapCap -notmatch '^(?<size>\d+)(?<unit>GB|MB)$') {
        Fail '-HeapCap must look like 8GB, 16GB or 32GB.'
    }
    $capBytes = [uint64]$Matches['size']
    if ($Matches['unit'] -eq 'GB') { $capBytes = $capBytes * 1GB } else { $capBytes = $capBytes * 1MB }
}

$startedAt = Get-Date
if (-not $NoFetch) {
    Invoke-Git -Arguments @('-C', $repoRoot, 'fetch', $Remote, '--prune') -What 'fetch' | Out-Null
}
$commit = ((Invoke-Git -Arguments @('-C', $repoRoot, 'rev-parse', "$Ref^{commit}") -What 'resolve ref') -join "`n").Trim()
if ($commit.Length -lt 7) { Fail "Could not resolve $Ref to a commit." }
$short = $commit.Substring(0, 7)
$runId = '{0}_{1}' -f $startedAt.ToString('yyyy-MM-dd_HHmm'), $short
$runFolder = Join-Path $WorkRoot ('runs\' + $runId)
$checkout = Join-Path $WorkRoot ('work\' + $runId)
New-Item -ItemType Directory -Path $runFolder -Force | Out-Null
$script:LogPath = Join-Path $runFolder 'script.log'
Write-Log "run $runId for $Ref at $commit"
Write-Log "host $env:COMPUTERNAME, work root $WorkRoot"

$remoteUrl = ((Invoke-Git -Arguments @('-C', $repoRoot, 'remote', 'get-url', $Remote) -What 'resolve remote') -join "`n").Trim()
if ($remoteUrl -match 'github\.com[:/](?<slug>[^/]+/[^/]+?)(\.git)?$') {
    $slug = $Matches['slug']
}
else {
    Push-Location $repoRoot
    try {
        $slug = (& gh repo view --json nameWithOwner -q .nameWithOwner 2>&1 | Out-String).Trim()
    }
    finally {
        Pop-Location
    }
    if ($LASTEXITCODE -ne 0 -or -not $slug) { Fail 'gh repo view failed; run gh auth login on this host.' }
}
Write-Log "repository $slug"

if (-not $SkipRunnerCheck) {
    $runnersJson = Invoke-Tool -File 'gh' -Arguments @('api', "repos/$slug/actions/runners?per_page=100") -What 'list runners'
    $runners = ($runnersJson -join "`n") | ConvertFrom-Json
    $busy = @()
    if ($runners.PSObject.Properties.Name -contains 'runners') {
        $busy = @($runners.runners | Where-Object { $_.name -like '*cobbler1*' -and $_.busy -eq $true })
    }
    if ($busy.Count -gt 0) {
        Fail ("The COBBLER1 GitHub runner is busy ({0}). Wait for benchmarks.yml to finish; one run at a time on the box." -f (($busy | ForEach-Object { $_.name }) -join ', '))
    }
    Write-Log 'cobbler1 runner is idle'
}

if (-not $SkipHostCheck) {
    $samples = @()
    for ($index = 0; $index -lt 6; $index++) {
        try {
            $samples += (Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average
        }
        catch {
            Write-Log "background CPU sample failed: $($_.Exception.Message)"
        }
        if ($index -lt 5) { Start-Sleep -Seconds 5 }
    }
    if ($samples.Count -gt 0) {
        $average = ($samples | Measure-Object -Average).Average
        Write-Log ('background CPU average {0:N1}%' -f $average)
        if ($average -gt $MaxBackgroundPercent) {
            Fail ('background CPU is {0:N1}%; over the {1}% limit. Stop the other work and run again.' -f $average, $MaxBackgroundPercent)
        }
    }
}

$project = Join-Path $checkout 'RecDiffy.NET\capacity\RecDiffy.Capacity\RecDiffy.Capacity.csproj'
$buildLog = Join-Path $runFolder 'build.log'
$runLog = Join-Path $runFolder 'run.log'
$harness = $null
$resultsPath = Join-Path $WorkRoot ('branch\' + $runId)
$succeeded = $false

try {
    Remove-Worktree -Repo $repoRoot -Path $checkout
    Invoke-Git -Arguments @('-C', $repoRoot, 'worktree', 'add', '--detach', $checkout, $commit) -What 'checkout' | Out-Null

    Push-Location (Join-Path $checkout 'RecDiffy.NET')
    try {
        Invoke-Tool -File 'dotnet' -Arguments @('build', $project, '-c', 'Release', '-warnaserror', '-f', $Framework) -What 'build' -LogFile $buildLog | Out-Null
    }
    finally {
        Pop-Location
    }

    if ($Flavor -eq 'jit') {
        $harness = Join-Path $checkout ('RecDiffy.NET\capacity\RecDiffy.Capacity\bin\Release\' + $Framework + '\RecDiffy.Capacity.dll')
        if (-not (Test-Path -LiteralPath $harness)) { Fail "The built harness is missing: $harness" }
        $harnessFile = 'dotnet'
        $harnessPrefix = @($harness)
    }
    else {
        $publishFolder = Join-Path $WorkRoot ('publish\' + $runId + '-' + $Flavor)
        $publishArguments = @('publish', $project, '-c', 'Release', '-f', $Framework, '-r', $Runtime, '-p:NuGetAudit=false', '-o', $publishFolder)
        if ($Flavor -eq 'r2r') {
            $publishArguments += @('--self-contained', 'true', '-p:PublishReadyToRun=true')
        }
        else {
            $publishArguments += @('-p:PublishAot=true')
        }
        Invoke-Tool -File 'dotnet' -Arguments $publishArguments -What "$Flavor publish" -LogFile (Join-Path $runFolder ($Flavor + '.log')) | Out-Null
        $harness = Join-Path $publishFolder 'RecDiffy.Capacity.exe'
        if (-not (Test-Path -LiteralPath $harness)) { Fail "The $Flavor publish is missing: $harness" }
        $harnessFile = $harness
        $harnessPrefix = @()
    }

    $harnessArguments = @(
        '--sweep', $Sweep,
        '--out', $runFolder,
        '--seed', '377',
        '--repeats', [string]$Repeats,
        '--break-rate', '0.1',
        '--unmatched-rate', '0.05',
        '--commit', $commit,
        '--ref', $Ref
    )
    if ($Sweep -eq 'sizes') {
        $harnessArguments += @('--sizes', $Sizes, '--case-timeout-seconds', [string]$CaseTimeoutSeconds)
        if ($NoExcel) { $harnessArguments += @('--excel', 'none') }
    }
    else {
        $harnessArguments += @('--levels', $Levels, '--concurrency-rows', [string]$ConcurrencyRows)
    }

    $savedHeapCap = $env:DOTNET_GCHeapHardLimit
    $savedGcServer = $env:DOTNET_gcServer
    try {
        if ($capBytes -gt 0) {
            $env:DOTNET_GCHeapHardLimit = '0x{0:X}' -f $capBytes
            Write-Log "heap cap $HeapCap ($($env:DOTNET_GCHeapHardLimit))"
        }
        if ($GcMode -eq 'server') { $env:DOTNET_gcServer = '1' } else { $env:DOTNET_gcServer = '0' }

        Write-Log "run $($harness) $($harnessArguments -join ' ')"
        $harnessResult = Invoke-Captured -File $harnessFile -Arguments ($harnessPrefix + $harnessArguments) -What 'harness' -LogFile $runLog -Echo
        $harnessExit = $harnessResult.ExitCode
    }
    finally {
        $env:DOTNET_GCHeapHardLimit = $savedHeapCap
        $env:DOTNET_gcServer = $savedGcServer
    }

    $missing = @()
    foreach ($name in @('results.json', 'report.md', 'report.html')) {
        $path = Join-Path $runFolder $name
        if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            $missing += $name
        }
    }
    if ($missing.Count -gt 0) {
        Fail ("The run produced no report: missing or empty {0}. A run without its report is a failed run." -f ($missing -join ', '))
    }

    $status = if ($harnessExit -eq 0) { 'ok' } else { "failed (harness exit $harnessExit)" }
    Write-Log "reports written; status $status"

    Remove-Worktree -Repo $repoRoot -Path $resultsPath
    Remove-StaleResultsWorktree -Repo $repoRoot -Branch $ResultsBranch

    $localBranch = Test-Ref -Repo $repoRoot -Name "refs/heads/$ResultsBranch"
    if (-not $localBranch) {
        try {
            Invoke-Git -Arguments @('-C', $repoRoot, 'fetch', $Remote, $ResultsBranch) -What 'fetch results branch' | Out-Null
        }
        catch {
            Write-Log 'no results branch on the remote yet'
        }
    }

    if ($localBranch) {
        Invoke-Git -Arguments @('-C', $repoRoot, 'worktree', 'add', '--detach', $resultsPath, $ResultsBranch) -What 'results worktree' | Out-Null
        Invoke-Git -Arguments @('-C', $resultsPath, 'checkout', '-B', $ResultsBranch) -What 'results branch' | Out-Null
    }
    elseif (Test-Ref -Repo $repoRoot -Name "refs/remotes/$Remote/$ResultsBranch") {
        Invoke-Git -Arguments @('-C', $repoRoot, 'worktree', 'add', '--detach', $resultsPath, "$Remote/$ResultsBranch") -What 'results worktree' | Out-Null
        Invoke-Git -Arguments @('-C', $resultsPath, 'checkout', '-B', $ResultsBranch) -What 'results branch' | Out-Null
    }
    else {
        Invoke-Git -Arguments @('-C', $repoRoot, 'worktree', 'add', '--detach', $resultsPath, $commit) -What 'results worktree' | Out-Null
        Invoke-Git -Arguments @('-C', $resultsPath, 'checkout', '--orphan', $ResultsBranch) -What 'orphan results branch' | Out-Null
        Invoke-Git -Arguments @('-C', $resultsPath, 'rm', '-rf', '.') -What 'clear results branch' | Out-Null
    }

    $target = Join-Path $resultsPath ('runs\' + $runId)
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $runFolder 'results.json') -Destination $target
    Copy-Item -LiteralPath (Join-Path $runFolder 'report.md') -Destination $target
    Copy-Item -LiteralPath (Join-Path $runFolder 'report.html') -Destination $target
    Get-ChildItem -LiteralPath $runFolder -Filter '*.log' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $target }

    Invoke-Git -Arguments @('-C', $resultsPath, 'add', ('runs/' + $runId)) -What 'stage run' | Out-Null
    $message = 'Capacity run {0} ({1}) at {2}' -f $runId, $short, $commit
    Invoke-Git -Arguments @('-C', $resultsPath, 'commit', '-m', $message) -What 'commit run' | Out-Null

    if (-not $SkipPush) {
        Invoke-Git -Arguments @('-C', $resultsPath, 'push', $Remote, $ResultsBranch) -What 'push results' | Out-Null
        Write-Log "pushed $ResultsBranch"
    }

    $link = 'https://github.com/{0}/tree/{1}/runs/{2}' -f $slug, $ResultsBranch, $runId
    if (-not $SkipComment) {
        $bodyPath = Join-Path $runFolder 'comment.md'
        $body = New-Object System.Collections.Generic.List[string]
        $body.Add(('### Capacity run `{0}`' -f $runId))
        $body.Add('')
        $body.Add(('- Commit: `{0}` (`{1}`)' -f $commit, $Ref))
        $osCaption = 'unknown OS'
        try { $osCaption = (Get-CimInstance Win32_OperatingSystem).Caption } catch { }
        $body.Add(('- Host: `{0}`, {1}' -f $env:COMPUTERNAME, $osCaption))
        $body.Add(('- Settings: sweep `{0}`, GC `{1}`, heap cap `{2}`, flavor `{3}`, framework `{4}`, repeats {5}' -f $Sweep, $GcMode, $(if ($HeapCap) { $HeapCap } else { 'none' }), $Flavor, $Framework, $Repeats))
        $body.Add(('- Status: {0}' -f $status))
        $body.Add('')
        $body.Add((Get-Content -LiteralPath (Join-Path $runFolder 'report.md') -Raw))
        $body.Add('')
        $body.Add(('Full run folder: {0}' -f $link))
        $body | Set-Content -LiteralPath $bodyPath -Encoding utf8
        Invoke-Tool -File 'gh' -Arguments @('issue', 'comment', $Issue, '--repo', $slug, '--body-file', $bodyPath) -What 'comment' | Out-Null
        Write-Log "posted to issue #$Issue"
    }

    Write-Log "run folder $runFolder"
    Write-Log "results branch folder $link"
    if ($harnessExit -ne 0) {
        Fail "The harness exited $harnessExit; the run folder and report are kept for inspection."
    }
    $succeeded = $true
}
finally {
    if ($succeeded) {
        if (-not $KeepCheckout) { Remove-Worktree -Repo $repoRoot -Path $checkout }
        if (-not $KeepResults -and -not $SkipPush) { Remove-Worktree -Repo $repoRoot -Path $resultsPath }
        try { Invoke-Git -Arguments @('-C', $repoRoot, 'worktree', 'prune') -What 'prune worktrees' | Out-Null } catch { }
    }
    else {
        Write-Log "kept for inspection: checkout $checkout, results worktree $resultsPath"
    }
}
