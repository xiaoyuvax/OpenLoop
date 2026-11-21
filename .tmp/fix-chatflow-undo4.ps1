$p = Join-Path $PSScriptRoot '..\src\ChatFlow\ChatFlow\ChatMessageRow.cs'
$p = [System.IO.Path]::GetFullPath($p)
$txt = [System.IO.File]::ReadAllText($p)
$comment = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'comment.txt'))
$pattern = '(?s)// Gap sits at upper-left.*?middle\.\r?\n'
if ($txt -notmatch $pattern) { Write-Output 'NOT_FOUND'; exit 1 }
$out = [regex]::Replace($txt, $pattern, { param($m) $comment }, 1)
[System.IO.File]::WriteAllText($p, $out, (New-Object System.Text.UTF8Encoding($false)))
Write-Output 'REPLACED'
