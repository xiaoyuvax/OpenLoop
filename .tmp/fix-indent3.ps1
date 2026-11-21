$p = 'D:\PUB\OpenLoop\src\ChatFlow\ChatFlow\ChatMessageRow.cs'
$l = [System.IO.File]::ReadAllLines($p)
$pad = ' ' * 24
for ($i = 0; $i -lt $l.Length; $i++) {
  if ($l[$i].StartsWith($pad) -and $l[$i].TrimStart().StartsWith('//')) {
    $l[$i] = '            ' + $l[$i].TrimStart()
  }
}
$nl = [char]13 + [string][char]10
[System.IO.File]::WriteAllText($p, ($l -join $nl) + $nl, (New-Object System.Text.UTF8Encoding($false)))
'OK'
