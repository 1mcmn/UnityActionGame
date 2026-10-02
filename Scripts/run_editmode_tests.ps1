[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProjectRoot,
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

try {
    $project = (Resolve-Path -LiteralPath $ProjectRoot).Path.TrimEnd('\', '/')
    $versionFile = Join-Path $project 'ProjectSettings\ProjectVersion.txt'
    $versionText = Get-Content -LiteralPath $versionFile -Raw
    $versionMatch = [regex]::Match($versionText, '(?m)^m_EditorVersion:\s*([\w.]+)\s*$')
    if (-not $versionMatch.Success) { throw 'ProjectVersion.txt has no valid m_EditorVersion.' }
    $version = $versionMatch.Groups[1].Value

    $editor = $env:EDITMODE_EDITOR
    if ([string]::IsNullOrWhiteSpace($editor)) {
        $editor = Join-Path $env:ProgramFiles "Tuanjie\Hub\Editor\$version\Editor\Tuanjie.exe"
    }
    if (-not (Test-Path -LiteralPath $editor -PathType Leaf) -or
        [IO.Path]::GetExtension($editor) -ine '.exe') {
        throw "Matching editor not found: $editor. Set EDITMODE_EDITOR to Tuanjie.exe for $version."
    }
    $editor = (Resolve-Path -LiteralPath $editor).Path
    $resultChecker = Join-Path $PSScriptRoot 'check_editmode_results.ps1'
    if (-not (Test-Path -LiteralPath $resultChecker -PathType Leaf)) {
        throw "Missing result checker: $resultChecker"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $project 'Assets\Tests\EditMode\EditModeTests.asmdef'))) {
        throw 'The project EditModeTests assembly definition is missing.'
    }

    # Detect a normal Hub/editor launch, including paths with spaces or apostrophes.
    $editorProcesses = @(Get-CimInstance Win32_Process -Filter "Name='Tuanjie.exe' OR Name='Unity.exe'")
    foreach ($editorProcess in $editorProcesses) {
        $commandLine = [string]$editorProcess.CommandLine
        $match = [regex]::Match($commandLine, '(?i)(?:^|\s)-projectPath\s+(?:"([^"]+)"|(\S+))')
        if (-not $match.Success) { continue }
        $openPath = $match.Groups[1].Value
        if (-not $openPath) { $openPath = $match.Groups[2].Value }
        try { $openPath = [IO.Path]::GetFullPath($openPath).TrimEnd('\', '/') } catch { continue }
        if ($openPath -ieq $project) {
            Write-Host "[FAIL] This project is open in editor process $($editorProcess.ProcessId). Save and close it before batch testing."
            exit 4
        }
    }
    # A leftover unlocked lockfile is harmless. Never delete it or terminate an editor.
    $lockFile = Join-Path $project 'Temp\UnityLockfile'
    if (Test-Path -LiteralPath $lockFile -PathType Leaf) {
        try {
            $lockStream = [IO.File]::Open($lockFile, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
            $lockStream.Dispose()
        } catch {
            Write-Host '[FAIL] The project lock is occupied or cannot be checked. Save and close its editor, then retry.'
            exit 4
        }
    }

    Write-Host "Project: $project"
    Write-Host "Required editor version: $version"
    Write-Host "Editor: $editor"
    if ($env:EDITMODE_EDITOR) {
        Write-Host '[NOTE] EDITMODE_EDITOR override is active; use the same version as ProjectVersion.txt.'
    }
    if ($CheckOnly) {
        Write-Host '[OK] Setup checks passed. No editor was started and no tests were run.'
        exit 0
    }

    # Every invocation gets a fresh directory: historical XML cannot turn a failed run green.
    $runName = 'EditMode-{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), [guid]::NewGuid().ToString('N')
    $runDirectory = Join-Path (Join-Path $project 'TestResults') $runName
    New-Item -ItemType Directory -Path $runDirectory | Out-Null
    $resultFile = Join-Path $runDirectory 'results.xml'
    $logFile = Join-Path $runDirectory 'editor.log'
    $startedUtc = [DateTime]::UtcNow.ToString('o')
    Write-Host "Results: $resultFile"
    Write-Host "Editor log: $logFile"
    Write-Host 'Running project EditModeTests; initial import may take several minutes.'

    # Official UTF 1.1 invocation; do not add -quit, which can interrupt the test run.
    # https://docs.unity3d.com/Packages/com.unity.test-framework@1.1/manual/reference-command-line.html
    # Windows paths cannot contain quotes; normalized paths below have no trailing slash.
    $editorArguments = '-batchmode -nographics -projectPath "{0}" -runTests -testPlatform EditMode -assemblyNames EditModeTests -testResults "{1}" -logFile "{2}"' -f $project, $resultFile, $logFile
    $editorRun = Start-Process -FilePath $editor -ArgumentList $editorArguments -Wait -PassThru -WindowStyle Hidden
    $editorExitCode = $editorRun.ExitCode
    & $resultChecker -ResultPath $resultFile -StartedUtc $startedUtc -EditorExitCode $editorExitCode
    exit $LASTEXITCODE
} catch {
    Write-Host "[FAIL] $($_.Exception.Message)"
    exit 2
}
