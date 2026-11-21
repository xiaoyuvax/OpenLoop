$p = 'D:\PUB\OpenLoop\src\ChatFlow\ChatFlow\ChatMessageRow.cs'
$txt = [System.IO.File]::ReadAllText($p)
$out = [regex]::Replace($txt, '(?m)^ {24}(//)', '            $1')
[System.IO.File]::WriteAllText($p, $out, (New-Object System.Text.UTF8Encoding($false)))
Write-Output 'OK'
