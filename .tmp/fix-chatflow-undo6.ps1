$p = 'D:\PUB\OpenLoop\src\ChatFlow\ChatFlow\ChatMessageRow.cs'
$commentPath = 'D:\PUB\OpenLoop\src\openloop\.tmp\comment.txt'
$txt = [System.IO.File]::ReadAllText($p)
$pattern = '(?s)// Gap sits at upper-left.*?in the middle\.\r?\n'
if ($txt -notmatch $pattern) { Write-Output 'NOT_FOUND'; exit 1 }
$comment = [System.IO.File]::ReadAllText($commentPath)
$out = [regex]::Replace($txt, $pattern, { param($m) $comment }, 1)
[System.IO.File]::WriteAllText($p, $out, (New-Object System.Text.UTF8Encoding($false)))
Write-Output 'REPLACED'
