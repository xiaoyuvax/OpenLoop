$s='C:\Users\Xiaoyu\AppData\Roaming\OpenLoop\versions\shell\0.1.47-cc5ab5a9\OpenLoop.exe'
$pats=@('LoopCollabHost','CollabMonitor','CollabStart','AutoInsertBack','PendingAuto')
foreach($p in $pats){
  $n=(findstr /C:$p $s | Measure-Object).Count
  "$p = $n"
}