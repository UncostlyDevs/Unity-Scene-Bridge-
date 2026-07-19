# SceneBridge stress suite - RUN 1: API fuzz, throughput, integrity, batch.
# Prereqs: Unity open with the project, bridge answering /ping. Run on a scene you can dirty.
# NOTE: run BEFORE run2. ASCII-only file (PowerShell 5.1 reads BOM-less UTF-8 as ANSI).
$B="http://localhost:8787"
$pass=0;$fail=0;$results=@()
function T($name,$ok,$detail){ $script:results += [pscustomobject]@{test=$name;ok=$ok;detail=$detail}; if($ok){$script:pass++}else{$script:fail++}; Write-Host ("  " + $(if($ok){"PASS"}else{"FAIL"}) + "  " + $name + $(if($detail){" - " + $detail}else{""})) }
function Post($path,$body){ try { (Invoke-WebRequest -Uri ($B+$path) -Method Post -Body $body -ContentType "text/plain" -TimeoutSec 15 -UseBasicParsing).Content } catch { "EXC:" + $_.Exception.Message } }
function GetRaw($path){ try { (Invoke-WebRequest -Uri ($B+$path) -TimeoutSec 15 -UseBasicParsing).Content } catch { "EXC:" + $_.Exception.Message } }

Write-Host "=== SETUP ==="
$ping = GetRaw "/ping"
T "bridge alive" ($ping -like '*"ok":true*') $ping.Substring(0,[Math]::Min(60,$ping.Length))
$pre = (GetRaw "/scene?light=1") | ConvertFrom-Json
Write-Host ("  scene before: " + $pre.nodes.Count + " nodes")
$spawn = Post "/spawn_demo" "{}"
T "spawn stress rig" ($spawn -like '*spawned*') $spawn
Start-Sleep -Milliseconds 400
$j = (GetRaw "/scene?light=1") | ConvertFrom-Json
$rig = @{}
$j.nodes | Where-Object { $_.path -like 'SB_TestRig*' } | ForEach-Object { $rig[$_.name] = $_.id }
T "rig nodes present" ($rig.Count -ge 7) ("rig nodes: " + $rig.Count)

Write-Host "`n=== API FUZZ (bridge must answer everything, crash nothing) ==="
$r = GetRaw "/no_such_route"
T "unknown route -> error" ($r -like '*error*' -or $r -like '*404*') $r
$r = Post "/apply" "this is not json {{{"
T "malformed json -> error" ($r -like '*error*') $r
$r = Post "/apply" "{}"
T "empty body -> error" ($r -like '*error*') $r
$r = Post "/apply" '{"edits":[]}'
T "empty edits -> applied:0" ($r -like '*"applied":0*') $r
$r = Post "/apply" '{"edits":[{"id":999999999,"position":[0,0,0]}]}'
T "unknown id -> missing list" ($r.Contains('"missing":[999999999]')) $r
$r = Post "/apply" ('{"edits":[{"id":' + $rig['Chassis'] + ',"position":[NaN,0,0]}]}')
T "NaN token answered (channel must be dropped, see integrity below)" ($r -notlike 'EXC:*') $r
$j = (GetRaw "/scene?light=1") | ConvertFrom-Json
$ch = $j.nodes | Where-Object { $_.id -eq $rig['Chassis'] }
$finite = ($ch.l.p[0].ToString() -notmatch 'NaN') -and ($ch.l.p[1].ToString() -notmatch 'NaN')
T "NaN did NOT poison the transform" $finite ("pos=[" + ($ch.l.p -join ',') + "]")
$r = Post "/marker" '{"parentId":123456789,"name":"Bogus","type":"Point","worldPosition":[0,0,0]}'
T "marker bogus parent -> error (no phantom)" ($r -like '*error*') $r
$r = Post "/rename" '{"id":1,"newName":""}'
T "rename empty -> error" ($r -like '*error*') $r
$r = Post "/delete" '{"id":999999999}'
T "delete unknown id -> error" ($r -like '*error*') $r
$ping2 = GetRaw "/ping"
T "bridge still alive after fuzz" ($ping2 -like '*"ok":true*') ""

Write-Host "`n=== THROUGHPUT (100 rapid applies; PS client adds ~50ms overhead vs browser fetch) ==="
$chassis = $rig['Chassis']
$times=@()
for($i=0;$i -lt 100;$i++){
  $y = 0.9 + (($i % 10) * 0.05)
  $t0=[DateTime]::Now
  $null = Post "/apply" ('{"edits":[{"id":' + $chassis + ',"position":[0,' + $y + ',0]}]}')
  $times += ([DateTime]::Now - $t0).TotalMilliseconds
}
$sorted = $times | Sort-Object
$p50=[Math]::Round($sorted[49]);$p95=[Math]::Round($sorted[94]);$max=[Math]::Round($sorted[99])
T "100 applies completed" $true ("p50=" + $p50 + "ms p95=" + $p95 + "ms max=" + $max + "ms")
T "p50 under 250ms" ($p50 -lt 250) ("p50=" + $p50 + "ms")
$null = Post "/apply" ('{"edits":[{"id":' + $chassis + ',"position":[0,0.9,0]}]}')
Start-Sleep -Milliseconds 300
$j = (GetRaw "/scene?light=1") | ConvertFrom-Json
$ch = $j.nodes | Where-Object { $_.id -eq $chassis }
T "final value correct after storm" ([Math]::Abs($ch.l.p[1]-0.9) -lt 0.01) ("y=" + $ch.l.p[1])

Write-Host "`n=== INTEGRITY (20 random multi-axis round-trips) ==="
$rand = New-Object System.Random(42)
$names = @('Chassis','Cab','Wheel_FL','Wheel_RR')
$mismatch=0
for($i=0;$i -lt 20;$i++){
  $n = $names[$i % 4]; $id = $rig[$n]
  $px=[Math]::Round(($rand.NextDouble()*10-5),3); $py=[Math]::Round(($rand.NextDouble()*4+0.2),3); $pz=[Math]::Round(($rand.NextDouble()*10-5),3)
  $rx=[Math]::Round(($rand.NextDouble()*80-40),2); $ry=[Math]::Round(($rand.NextDouble()*340-170),2); $rz=[Math]::Round(($rand.NextDouble()*80-40),2)
  $null = Post "/apply" ('{"edits":[{"id":' + $id + ',"position":[' + $px + ',' + $py + ',' + $pz + '],"rotationEuler":[' + $rx + ',' + $ry + ',' + $rz + ']}]}')
  Start-Sleep -Milliseconds 120
  $j = (GetRaw "/scene?light=1") | ConvertFrom-Json
  $o = $j.nodes | Where-Object { $_.id -eq $id }
  $pOK = ([Math]::Abs($o.t.p[0]-$px) -lt 0.02) -and ([Math]::Abs($o.t.p[1]-$py) -lt 0.02) -and ([Math]::Abs($o.t.p[2]-$pz) -lt 0.02)
  if(-not $pOK){ $mismatch++; Write-Host ("    mismatch " + $n + ": sent [" + $px + "," + $py + "," + $pz + "] got [" + ($o.t.p -join ',') + "]") }
}
T "20 random transforms exact" ($mismatch -eq 0) ("mismatches: " + $mismatch)

Write-Host "`n=== BIG BATCH (one apply, 120 edits) ==="
$edits=@()
for($i=0;$i -lt 120;$i++){ $n=$names[$i % 4]; $edits += ('{"id":' + $rig[$n] + ',"position":[' + (($i%5)*0.5) + ',1,0]}') }
$r = Post "/apply" ('{"edits":[' + ($edits -join ',') + ']}')
T "120-edit batch applied" ($r -like '*"applied":120*') $r

Write-Host "`n=== SUMMARY RUN 1 ==="
Write-Host ("PASS: " + $pass + "   FAIL: " + $fail)
$results | Where-Object { -not $_.ok } | ForEach-Object { Write-Host ("  FAILED: " + $_.test + " - " + $_.detail) }
Write-Host "NOTE: run 2 next (tools/stress-run2.ps1). It cleans up the rig at the end."
