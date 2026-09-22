$ErrorActionPreference = 'Stop'
$path = 'E:\Unity\My project\开题答辩_极简版.pptx'
$ppt = New-Object -ComObject PowerPoint.Application
$pres = $ppt.Presentations.Open($path, $true, $false, $false)
"slides = " + $pres.Slides.Count
foreach($s in $pres.Slides){
  foreach($sh in $s.Shapes){
    if($sh.HasTextFrame -ne -1){ continue }
    $t = $sh.TextFrame2.TextRange.Text
    if([string]::IsNullOrWhiteSpace($t)){ continue }
    $lines = $sh.TextFrame2.TextRange.Lines().Count
    $bh = [math]::Round($sh.TextFrame2.TextRange.BoundHeight, 1)
    $bw = [math]::Round($sh.TextFrame2.TextRange.BoundWidth, 1)
    $overH = if($bh -gt ($sh.Height - 1)){ 'OVERFLOW-H' } else { 'ok' }
    $overW = if($bw -gt ($sh.Width - 1)){ 'OVERFLOW-W' } else { 'ok' }
    $prev = $t -replace "`r", ' / '
    if($prev.Length -gt 46){ $prev = $prev.Substring(0, 46) + '...' }
    "S{0} | {1,-12} | x={2,4} y={3,4} w={4,4} h={5,4} | lines={6} | bH={7,5} bW={8,6} | {9} {10} | {11}" -f `
      $s.SlideIndex, $sh.Name, [int]$sh.Left, [int]$sh.Top, [int]$sh.Width, [int]$sh.Height, $lines, $bh, $bw, $overH, $overW, $prev
  }
}
$pres.Close()
$ppt.Quit()
