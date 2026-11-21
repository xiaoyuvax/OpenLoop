$ErrorActionPreference='Stop'
$root=$PWD.Path
$p=Join-Path $root 'src/OpenLoop.Service/Worker/WorkerSessionHost.cs'
Write-Output $p
$t=[IO.File]::ReadAllText($p,[Text.Encoding]::UTF8)
$lines=New-Object System.Collections.Generic.List[string]
foreach($l in ($t -split "`r`n")){ $lines.Add($l) }

function Blocks([string]$path){
  $map=@{}; $cur=$null; $buf=New-Object System.Collections.Generic.List[string]
  foreach($l in [IO.File]::ReadAllLines($path,[Text.Encoding]::UTF8)){
    if($l.StartsWith('@@')){
      if($cur){ $map[$cur]=$buf.ToArray() }
      $cur=$l.Substring(2); $buf=New-Object System.Collections.Generic.List[string]; continue
    }
    $buf.Add($l)
  }
  if($cur){ $map[$cur]=$buf.ToArray() }
  return $map
}

$b=Blocks (Join-Path $root '.tmp/host-insert.txt')

function FindLine([string]$needle){
  for($i=0;$i -lt $lines.Count;$i++){ if($lines[$i].Trim() -eq $needle.Trim()){ return $i } }
  throw "anchor miss: $needle"
}
function AsList([string[]]$x){ return [System.Collections.Generic.List[string]]::new([string[]]$x) }

$i=FindLine 'public List<string> Pending { get; set; } = [];'
$lines.InsertRange($i+1,(AsList $b['A']))

$i=FindLine 'public bool Immediate { get; set; }'
$lines.RemoveAt($i)
$lines.InsertRange($i,(AsList $b['B']))

$i=FindLine 'Pending = session.Pending.Select(p => p.Text).ToList(),'
$lines.InsertRange($i+1,(AsList $b['C']))

$i=FindLine '/// <summary>停止当前轮，并暂停自动续跑（队列保留）。</summary>'
$block=AsList $b['D']
$block.Add('        /// <summary>停止当前轮，并暂停自动续跑（队列保留）。</summary>')
$lines.RemoveAt($i)
$lines.InsertRange($i,$block)

[IO.File]::WriteAllText($p, ($lines -join "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ('patched, lines=' + $lines.Count)
