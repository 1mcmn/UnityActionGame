[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ResultPath,
    [Parameter(Mandatory = $true)][string]$StartedUtc,
    [Parameter(Mandatory = $true)][int]$EditorExitCode
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$resultExitCode = 3
try {
    if (-not (Test-Path -LiteralPath $ResultPath -PathType Leaf)) {
        throw "No result XML was produced for this run: $ResultPath"
    }
    $start = [DateTime]::Parse($StartedUtc, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    $file = Get-Item -LiteralPath $ResultPath
    if ($file.LastWriteTimeUtc -lt $start.AddSeconds(-2)) { throw 'Result XML predates this run.' }

    $xmlSettings = New-Object System.Xml.XmlReaderSettings
    $xmlSettings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $xmlSettings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($file.FullName, $xmlSettings)
    try {
        $xml = New-Object System.Xml.XmlDocument
        $xml.XmlResolver = $null
        $xml.Load($reader)
    } finally { $reader.Dispose() }
    $run = $xml.DocumentElement
    if ($null -eq $run -or $run.Name -ne 'test-run') { throw 'Expected an NUnit test-run result root.' }
    $counts = @{}
    foreach ($name in @('total', 'passed', 'failed', 'skipped', 'inconclusive')) {
        $value = 0
        if (-not [int]::TryParse($run.GetAttribute($name), [ref]$value) -or $value -lt 0) {
            throw "Invalid or missing NUnit count: $name"
        }
        $counts[$name] = $value
    }
    if ($counts.total -ne ($counts.passed + $counts.failed + $counts.skipped + $counts.inconclusive)) {
        throw 'NUnit result counts are inconsistent.'
    }
    Write-Host ('Result: total={0} passed={1} failed={2} skipped={3} inconclusive={4}' -f
        $counts.total, $counts.passed, $counts.failed, $counts.skipped, $counts.inconclusive)
    if ($counts.total -le 0 -or $counts.passed -le 0) { throw 'No passing tests were executed; this is not a successful test run.' }
    if ($run.GetAttribute('result') -ne 'Passed' -or $counts.failed -gt 0 -or $counts.inconclusive -gt 0) {
        $resultExitCode = 1
        Write-Host '[FAIL] NUnit reports unsuccessful tests. See this run result XML and editor log.'
    } else {
        $resultExitCode = 0
        if ($counts.skipped -gt 0) { Write-Host '[NOTE] Some tests were skipped; the summary above is not full coverage.' }
    }
} catch {
    Write-Host "[FAIL] $($_.Exception.Message)"
}

if ($EditorExitCode -ne 0) {
    Write-Host "[FAIL] Editor exit code: $EditorExitCode"
    exit $EditorExitCode
}
if ($resultExitCode -eq 0) { Write-Host '[OK] The editor exited successfully and this run XML reports passing tests.' }
exit $resultExitCode
