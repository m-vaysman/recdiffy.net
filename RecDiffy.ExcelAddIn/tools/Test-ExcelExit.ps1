<#
.SYNOPSIS
    Checks that Excel leaves no EXCEL.EXE running after it closes with the RecDiffy add-in loaded (#239).

.DESCRIPTION
    Run this by hand on a Windows machine that has Excel. dotnet test and the GitHub workflows never run it.
    It finds Excel through its App Paths registry entry, and stops with a message if Excel isn't installed.

    It uses no COM. For each case, it:

    1. Notes the EXCEL.EXE processes already running, and never touches them.
    2. Starts a separate Excel with a copy of the packed add-in: EXCEL.EXE /x <add-in>.
    3. Lists what to do in that Excel. You do it, then close Excel yourself, the way a user would.
    4. Once that Excel has shown no window for 2 seconds, waits WaitSeconds (10 by default), then lists
       every EXCEL.EXE that started during the case and is still running. If Excel looks closed but the
       script keeps waiting, press Enter to start the wait yourself.

    A case passes when nothing is left running. When a case fails, the script asks before it ends the
    leftover process. It exits with 1 if any case failed or didn't run, and 0 otherwise.

    While it runs, don't start Excel yourself: a new Excel process counts as left behind. Open files from
    inside the test Excel with File > Open, not by double-clicking them.

    The cases come from issue #239:

    1. The add-in loaded but never used.
    2. The window opened and closed, then Excel closed.
    3. A range read and a run finished, with the window still open.
    4. A run in progress.
    5. A file dialog open.
    6. The add-in removed in the Add-ins dialog first.

    Cases 3 and 4 use sample CSV files that the script writes next to the add-in's copy, in
    %TEMP%\RecDiffy-exit-check\<date-time>.

.PARAMETER Case
    The cases to run, by number. All six by default.

.PARAMETER AddIn
    The packed add-in to load. By default, the Release build's packed add-in for Excel's bitness, under
    RecDiffy.ExcelAddIn\src\RecDiffy.ExcelAddIn\bin\Release\net10.0-windows\publish. The script loads a
    copy, so a leftover Excel can't lock the build output.

.PARAMETER WaitSeconds
    How long Excel has to exit after its last window closes. 10 by default.

.PARAMETER Rows
    How many rows case 4's sample CSV files have. 200000 by default. Raise it if the run finishes before
    you can close Excel.

.EXAMPLE
    dotnet build RecDiffy.ExcelAddIn/RecDiffy.ExcelAddIn.slnx -c Release
    .\RecDiffy.ExcelAddIn\tools\Test-ExcelExit.ps1

    Builds the add-in, then runs all six cases.

.EXAMPLE
    .\RecDiffy.ExcelAddIn\tools\Test-ExcelExit.ps1 -Case 1, 5 -WaitSeconds 20

    Runs cases 1 and 5, and gives Excel 20 seconds to exit.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\RecDiffy.ExcelAddIn\tools\Test-ExcelExit.ps1

    Runs all six cases where the execution policy blocks scripts.
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateRange(1, 6)]
    [int[]] $Case = @(1, 2, 3, 4, 5, 6),

    [string] $AddIn,

    [ValidateRange(1, 600)]
    [int] $WaitSeconds = 10,

    [ValidateRange(1000, 10000000)]
    [int] $Rows = 200000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-Excel {
    $keys = @(
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\excel.exe',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\excel.exe',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\excel.exe'
    )
    foreach ($key in $keys) {
        $entry = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
        if ($null -eq $entry) {
            continue
        }

        $default = $entry.PSObject.Properties['(default)']
        if ($null -eq $default -or [string]::IsNullOrWhiteSpace([string]$default.Value)) {
            continue
        }

        $path = ([string]$default.Value).Trim().Trim('"')
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return $path
        }
    }

    return $null
}

function Get-MachineType([string] $Path) {
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        $stream.Position = 0x3C
        $stream.Position = $reader.ReadInt32() + 4
        return [int]$reader.ReadUInt16()
    }
    finally {
        $stream.Dispose()
    }
}

function Write-SampleCsv([string] $Path, [int] $Count, [bool] $Changed) {
    $writer = New-Object System.IO.StreamWriter($Path, $false, (New-Object System.Text.UTF8Encoding($false)))
    try {
        $writer.WriteLine('Id,Amount')
        for ($n = 1; $n -le $Count; $n++) {
            $amount = [long]$n * 125
            if ($Changed -and $n % 97 -eq 0) {
                $amount += 1
            }
            $writer.WriteLine(('T{0:D7},{1}' -f $n, $amount))
        }
    }
    finally {
        $writer.Dispose()
    }
}

$windowsSource = @'
using System;
using System.Runtime.InteropServices;

namespace RecDiffyExitCheck
{
    public static class Windows
    {
        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out Rect rect);

        public static bool HasVisibleWindow(int processId)
        {
            bool found = false;
            EnumWindows(delegate (IntPtr window, IntPtr parameter)
            {
                uint owner;
                Rect rect;
                GetWindowThreadProcessId(window, out owner);
                if (owner == (uint)processId && IsWindowVisible(window) && GetWindowRect(window, out rect)
                    && rect.Right > rect.Left && rect.Bottom > rect.Top)
                {
                    found = true;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
'@

function Get-NewExcel([int[]] $Before) {
    @(Get-Process -Name EXCEL -ErrorAction SilentlyContinue | Where-Object { $Before -notcontains $_.Id })
}

function Test-WindowShown([System.Diagnostics.Process] $Process) {
    (-not $Process.HasExited) -and [RecDiffyExitCheck.Windows]::HasVisibleWindow($Process.Id)
}

function Test-EnterPressed {
    try {
        while ([Console]::KeyAvailable) {
            if ([Console]::ReadKey($true).Key -eq [ConsoleKey]::Enter) {
                return $true
            }
        }
    }
    catch [System.InvalidOperationException] {
    }

    return $false
}

function Wait-WindowShown([System.Diagnostics.Process] $Process) {
    while (-not $Process.HasExited) {
        if (Test-WindowShown $Process) {
            return $true
        }

        Start-Sleep -Milliseconds 250
    }

    return $false
}

function Wait-WindowGone([System.Diagnostics.Process] $Process) {
    $goneSince = $null
    while ($true) {
        if (Test-EnterPressed) {
            return [pscustomobject]@{ Since = [DateTime]::UtcNow; ByHand = $true }
        }

        if (Test-WindowShown $Process) {
            $goneSince = $null
        }
        elseif ($null -eq $goneSince) {
            $goneSince = [DateTime]::UtcNow
        }
        elseif (([DateTime]::UtcNow - $goneSince).TotalSeconds -ge 2) {
            return [pscustomobject]@{ Since = $goneSince; ByHand = $false }
        }

        Start-Sleep -Milliseconds 250
    }
}

function Wait-ExcelExit([int[]] $Before, [pscustomobject] $Gone, [System.Diagnostics.Process] $Process) {
    $deadline = $Gone.Since.AddSeconds($WaitSeconds)
    while ($true) {
        $left = @(Get-NewExcel $Before)
        if ($left.Count -eq 0) {
            return [pscustomobject]@{ Left = @(); Seconds = ([DateTime]::UtcNow - $Gone.Since).TotalSeconds }
        }

        if (-not $Gone.ByHand -and (Test-WindowShown $Process)) {
            return $null
        }

        if ([DateTime]::UtcNow -ge $deadline) {
            return [pscustomobject]@{ Left = $left; Seconds = $null }
        }

        Start-Sleep -Milliseconds 250
    }
}

function Format-Leftover([System.Diagnostics.Process] $Process) {
    if ([RecDiffyExitCheck.Windows]::HasVisibleWindow($Process.Id)) {
        return ('{0} (it still shows a window: was Excel closed?)' -f $Process.Id)
    }

    return ('{0} (no window)' -f $Process.Id)
}

function Invoke-ExitCase([int] $Number) {
    $definition = $cases[$Number]
    $before = @(Get-Process -Name EXCEL -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })

    Write-Host ''
    Write-Host ('Case {0}: {1}' -f $Number, $definition.Title) -ForegroundColor Cyan
    $excelProcess = Start-Process -FilePath $excel -ArgumentList @('/x', ('"{0}"' -f $xll)) -PassThru
    Write-Host ('  Started Excel, process {0}. If it shows a security notice, choose "Enable this add-in for this session only".' -f $excelProcess.Id)
    Write-Host '  In that Excel:'
    foreach ($step in $definition.Steps) {
        Write-Host ('  - {0}' -f $step)
    }
    Write-Host '  - Close Excel the way you normally would. If it asks to save, choose Don''t Save.'
    Write-Host '  The check goes on by itself once Excel shows no window. If Excel looks closed but this keeps waiting,'
    Write-Host '  press Enter. If Excel or the RecDiffy window won''t close, press Ctrl+C and note it as a failure.'

    if (-not (Wait-WindowShown $excelProcess)) {
        $detail = 'Excel exited before it showed a window, so nothing was checked.'
        Write-Host ('  NOT RUN: {0}' -f $detail) -ForegroundColor Yellow
        return [pscustomobject]@{ Case = $Number; Result = 'Not run'; Detail = $detail; Title = $definition.Title }
    }

    $null = Test-EnterPressed
    while ($true) {
        $gone = Wait-WindowGone $excelProcess
        Write-Host ('  Excel looks closed. Giving it {0} seconds to exit.' -f $WaitSeconds)
        $outcome = Wait-ExcelExit $before $gone $excelProcess
        if ($null -ne $outcome) {
            break
        }

        Write-Host '  Excel showed a window again. Waiting for it to close.'
    }

    if ($outcome.Left.Count -eq 0) {
        $detail = 'Excel exited {0:N1} seconds after it looked closed.' -f $outcome.Seconds
        Write-Host ('  PASS: {0}' -f $detail) -ForegroundColor Green
        return [pscustomobject]@{ Case = $Number; Result = 'Pass'; Detail = $detail; Title = $definition.Title }
    }

    $ids = ($outcome.Left | ForEach-Object { $_.Id }) -join ', '
    $leftovers = ($outcome.Left | ForEach-Object { Format-Leftover $_ }) -join ', '
    $detail = 'EXCEL.EXE still running {0} seconds after Excel looked closed: process {1}.' -f $WaitSeconds, $leftovers
    Write-Host ('  FAIL: {0}' -f $detail) -ForegroundColor Red
    $answer = Read-Host '  End the leftover Excel now? It started during this case. [y/N]'
    if ($answer -match '^[yY]') {
        $outcome.Left | Stop-Process -Force
        Write-Host '  Ended it.'
    }
    else {
        Write-Host ('  Left it running. To end it later: Stop-Process -Id {0}' -f $ids)
    }

    return [pscustomobject]@{ Case = $Number; Result = 'Fail'; Detail = $detail; Title = $definition.Title }
}

if ($env:GITHUB_ACTIONS -eq 'true' -or -not [string]::IsNullOrEmpty($env:CI)) {
    Write-Host 'This check starts Excel, so it runs by hand on a machine with Excel, never in CI. Nothing was checked.'
    exit 0
}

if ($PSVersionTable.PSEdition -eq 'Core' -and -not $IsWindows) {
    Write-Host 'This check needs Excel for Windows. Nothing was checked.'
    exit 0
}

$excel = Find-Excel
if ($null -eq $excel) {
    Write-Host 'Excel is not installed on this machine: there is no App Paths entry for excel.exe. Nothing was checked.'
    exit 0
}

if ($null -eq ('RecDiffyExitCheck.Windows' -as [type])) {
    Add-Type -TypeDefinition $windowsSource
}

$machine = Get-MachineType $excel
$bitness = switch ($machine) {
    0x8664 { '64-bit' }
    0x014C { '32-bit' }
    default { 'unknown' }
}

if ([string]::IsNullOrEmpty($AddIn)) {
    $packed = switch ($bitness) {
        '64-bit' { 'RecDiffy.ExcelAddIn-AddIn64-packed.xll' }
        '32-bit' { 'RecDiffy.ExcelAddIn-AddIn-packed.xll' }
        default { $null }
    }
    if ($null -eq $packed) {
        Write-Host ('Excel''s bitness is unknown (machine type 0x{0:X4}). Pass the packed add-in with -AddIn.' -f $machine)
        exit 1
    }

    $AddIn = Join-Path $PSScriptRoot (Join-Path '..\src\RecDiffy.ExcelAddIn\bin\Release\net10.0-windows\publish' $packed)
}

if (-not (Test-Path -LiteralPath $AddIn -PathType Leaf)) {
    Write-Host ('There is no add-in at {0}. Build it first:' -f $AddIn)
    Write-Host '  dotnet build RecDiffy.ExcelAddIn/RecDiffy.ExcelAddIn.slnx -c Release'
    exit 1
}

$runFolder = Join-Path ([System.IO.Path]::GetTempPath()) (Join-Path 'RecDiffy-exit-check' (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $runFolder -Force | Out-Null
$xll = Join-Path $runFolder (Split-Path -Leaf $AddIn)
Copy-Item -LiteralPath (Resolve-Path -LiteralPath $AddIn).ProviderPath -Destination $xll

$smallLeft = Join-Path $runFolder 'small-left.csv'
$smallRight = Join-Path $runFolder 'small-right.csv'
$largeLeft = Join-Path $runFolder 'large-left.csv'
$largeRight = Join-Path $runFolder 'large-right.csv'

$selected = @($Case | Sort-Object -Unique)
if ($selected -contains 3) {
    Write-SampleCsv $smallLeft 1000 $false
    Write-SampleCsv $smallRight 1000 $true
}
if ($selected -contains 4) {
    Write-Host ('Writing two sample CSV files of {0:N0} rows for case 4.' -f $Rows)
    Write-SampleCsv $largeLeft $Rows $false
    Write-SampleCsv $largeRight $Rows $true
}

$openWindow = 'On the RecDiffy tab, choose Reconcile datasets.'
$keyOnId = 'On the Rules tab, choose Pair same-named columns, and set the Id field''s role to Key.'
$cases = @{
    1 = @{
        Title = 'The add-in loaded but never used'
        Steps = @('Check that the RecDiffy tab is on the ribbon. Don''t open the RecDiffy window.')
    }
    2 = @{
        Title = 'The window opened and closed, then Excel closed'
        Steps = @($openWindow, 'Close the RecDiffy window.')
    }
    3 = @{
        Title = 'A range read and a run finished, with the window still open'
        Steps = @(
            ('Choose File > Open, and open {0}' -f $smallLeft),
            $openWindow,
            'On the Sources tab, for Left, choose Open workbook..., and select the data in small-left.csv, header row included.',
            ('For Right, choose CSV file..., and pick {0}' -f $smallRight),
            $keyOnId,
            'Choose Run reconciliation, and wait for the results. Leave the RecDiffy window open.'
        )
    }
    4 = @{
        Title = 'A run in progress'
        Steps = @(
            $openWindow,
            ('On the Sources tab, for Left, choose CSV file..., and pick {0}' -f $largeLeft),
            ('For Right, choose CSV file..., and pick {0}' -f $largeRight),
            $keyOnId,
            'Choose Run reconciliation, and close Excel while the run is still going. If the run finishes first, run this case again with a larger -Rows.'
        )
    }
    5 = @{
        Title = 'A file dialog open'
        Steps = @(
            $openWindow,
            'On the Sources tab, for Left, choose CSV file..., and leave the file dialog open.'
        )
    }
    6 = @{
        Title = 'The add-in removed in the Add-ins dialog first'
        Steps = @(
            ($openWindow + ' Leave the RecDiffy window open.'),
            'Choose File > Options > Add-ins. Under Manage, choose Excel Add-ins, then Go.',
            ('Clear the RecDiffy box, and choose OK. If RecDiffy isn''t listed, choose Browse..., pick {0}, choose OK, then clear its box and choose OK.' -f $xll),
            'Note whether the RecDiffy tab and window closed. Browse keeps RecDiffy in that list, unchecked, for later sessions.'
        )
    }
}

Write-Host ('Excel: {0} ({1})' -f $excel, $bitness)
Write-Host ('Add-in: {0}' -f $xll)
Write-Host 'Don''t start Excel yourself until the check finishes.'

$results = @(foreach ($number in $selected) { Invoke-ExitCase $number })

Write-Host ''
$results | Format-Table -Property Case, Result, Title, Detail -AutoSize -Wrap | Out-Host
Write-Host ('The add-in copy and sample files are in {0}. Delete that folder once no Excel uses them.' -f $runFolder)

if (@($results | Where-Object { $_.Result -ne 'Pass' }).Count -gt 0) {
    exit 1
}

exit 0
