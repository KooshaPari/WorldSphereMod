$log='C:\Users\koosh\AppData\Local\Temp\claude\C--Users-koosh\84e512ac-a41d-4320-9c5c-5398d1f4b504\tasks\bxmq0wbm3.output'
$last=0;$idle=0
while($true){
  if(Test-Path $log){$s=(Get-Item $log).Length; if($s -ne $last){$last=$s;$idle=0;Write-Host "log_size=$s"}else{$idle++}; if($idle -ge 60){Write-Host "log_idle_120s_size=$s";break}}
  else{Write-Host "no_log_yet"}
  Start-Sleep 2
}
Write-Host "===TAIL==="
Get-Content $log -Tail 200
