$p = 'D:\PUB\OpenLoop\src\ChatFlow\ChatFlow\ChatMessageRow.cs'
$txt = [System.IO.File]::ReadAllText($p)
$pattern = '(?s)// Gap sits at upper-left.*?middle\.'
$replacement = "// 缺口在左上，箭头长在圆弧端头上（旧版三角形浮在圆环内部，看着像箭头在中间）。`r`n            // 260° 是 GDI+ 的起始角（0=三点钟、顺时针为正），逆着扫向缺口即箭头朝向。"
if ($txt -notmatch $pattern) { Write-Output 'NOT_FOUND'; exit 1 }
$out = [regex]::Replace($txt, $pattern, $replacement, 1)
[System.IO.File]::WriteAllText($p, $out, (New-Object System.Text.UTF8Encoding($false)))
Write-Output 'REPLACED'
