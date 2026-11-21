$p = 'D:\PUB\OpenLoop\src\ChatFlow\ChatFlow\ChatMessageRow.cs'
$txt = [System.IO.File]::ReadAllText($p)
$nl = if ($txt.Contains("`r`n")) { "`r`n" } else { "`n" }
$old = @'
            // Gap sits at upper-left; the arrow head is attached to the arc end. The old
            // triangle floated inside the ring, so the arrow looked like it was in the middle.
'@
$new = @'
            // 缺口在左上；箭头画在圆弧的端头上（旧版三角形浮在圆环内部，看着像箭头长在中间）。
            // 260° 是 GDI+ 的起始角（0=三点钟方向、顺时针为正），逆着扫向缺口即箭头朝向。
'@
$old = $old.Replace("`r`n", "`n").Replace("`n", $nl)
$new = $new.Replace("`r`n", "`n").Replace("`n", $nl)
if (-not $txt.Contains($old)) { Write-Output 'NOT_FOUND'; exit 1 }
[System.IO.File]::WriteAllText($p, $txt.Replace($old, $new), (New-Object System.Text.UTF8Encoding($false)))
Write-Output 'REPLACED'
