$ErrorActionPreference = 'Stop'
$menuRoot = $PSScriptRoot
$menuUrl = 'http://127.0.0.1:4173'
function Test-MenuPreview {
    $menuResponse = $null
    $menuReader = $null
    try {
        $menuRequest = [Net.HttpWebRequest]::Create($menuUrl)
        $menuRequest.Proxy = $null
        $menuRequest.Timeout = 1200
        $menuResponse = $menuRequest.GetResponse()
        $menuReader = [IO.StreamReader]::new($menuResponse.GetResponseStream())
        return $menuReader.ReadToEnd().Contains('INTERBLADE')
    } catch { return $false }
    finally { if ($menuReader) { $menuReader.Dispose() }; if ($menuResponse) { $menuResponse.Dispose() } }
}
if (Test-MenuPreview) { Start-Process $menuUrl; exit 0 }

$menuNode = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
if (-not (Test-Path -LiteralPath $menuNode)) {
    $menuNode = (Get-Command node -ErrorAction Stop).Source
}
$menuVite = Join-Path $menuRoot 'node_modules\vite\bin\vite.js'
if (-not (Test-Path -LiteralPath $menuVite)) { throw '缺少网页依赖。请在此目录执行 pnpm install --frozen-lockfile --ignore-scripts。' }
$menuProcess = Start-Process -FilePath $menuNode -ArgumentList @(('"' + $menuVite + '"'),'--host','127.0.0.1','--port','4173','--strictPort') -WorkingDirectory $menuRoot -WindowStyle Hidden -PassThru
for ($menuAttempt = 0; $menuAttempt -lt 20; $menuAttempt++) {
    Start-Sleep -Milliseconds 250
    if ($menuProcess.HasExited) { throw '预览服务未启动。请检查 4173 端口，或手动运行 pnpm dev。' }
    if (Test-MenuPreview) { Start-Process $menuUrl; exit 0 }
}
throw '预览服务启动超时。请在此目录手动运行 pnpm dev。'
