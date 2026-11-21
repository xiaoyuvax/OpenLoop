$p = 'D:\PUB\OpenLoop\src\ChatFlow\ChatFlow\ChatMessageRow.cs'
$txt = [System.IO.File]::ReadAllText($p)
$pattern = '(?s)// Gap sits at upper-left.*?in the middle\.\r?\n'
if ($txt -notmatch $pattern) { Write-Output 'NOT_FOUND'; exit 1 }
$comment = "            // 缺口在左上，箭头长在圆弧端头上（旧版三角形浮在圆环内部，看着像箭头在中间）。`r`n            // 260 度是 GDI+ 的起点角（0=三点钟方向、顺时针为正），箭头逆着扫向缺口。`r`n"
$out = [regex]::Replace($txt, $pattern, $comment, 1)
[System.IO.File]::WriteAllText($p, $out, (New-Object System.Text.UTF8Encoding($false)))
Write-Output 'REPLACED'
