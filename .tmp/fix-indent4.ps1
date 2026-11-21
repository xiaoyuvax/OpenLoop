$p = 'D:\PUB\OpenLoop\src\ChatFlow\ChatFlow\ChatMessageRow.cs'
$t = [System.IO.File]::ReadAllText($p)
$nl = "`r`n"
$t = $t.Replace('float ux = sin, uy = -cos;   // tangent towards the gap (decreasing angle)', 'float ux = sin, uy = -cos;   // 端点切线：指向缺口（角度递减方向）')
$t = $t.Replace('float nx = -uy, ny = ux;     // perpendicular to the arrow base', 'float nx = -uy, ny = ux;     // 箭头底边的法线')
[System.IO.File]::WriteAllText($p, $t, (New-Object System.Text.UTF8Encoding($false)))
'OK'
