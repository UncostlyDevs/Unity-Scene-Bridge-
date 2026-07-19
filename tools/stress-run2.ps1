# SceneBridge stress suite - RUN 2: marker storm, spawn/delete lifecycle, bone drift, reload drill.
# Run AFTER stress-run1.ps1 (expects its SB_TestRig). Cleans up everything at the end.
# ORDERING MATTERS: the reload drill is LAST - /refresh compiles ASYNCHRONOUSLY and the
# domain-reload blackout can land 30-90s later, killing any test still running (learned 2026-07-16).
$B="http://localhost:8787"
$pass=0;$fail=0;$results=@()
function T($name,$ok,$detail){ $script:results += [pscustomobject]@{test=$name;ok=$ok;detail=$detail}; if($ok){$script:pass++}else{$script:fail++}; Write-Host ("  " + $(if($ok){"PASS"}else{"FAIL"}) + "  " + $name + $(if($detail){" - " + $detail}else{""})) }
function Post($path,$body){ try { (Invoke-WebRequest -Uri ($B+$path) -Method Post -Body $body -ContentType "text/plain" -TimeoutSec 15 -UseBasicParsing).Content } catch { "EXC:" + $_.Exception.Message } }
function GetRaw($path){ try { (Invoke-WebRequest -Uri ($B+$path) -TimeoutSec 15 -UseBasicParsing).Content } catch { "EXC:" + $_.Exception.Message } }

$j = (GetRaw "/scene?light=1") | ConvertFrom-Json
$chassis = ($j.nodes | Where-Object { $_.path -eq 'SB_TestRig/Chassis' } | Select-Object -First 1).id
if(-not $chassis){ Write-Host "No SB_TestRig - run stress-run1.ps1 first."; exit 1 }
$preNodes = $j.nodes.Count
$preMarkers = ((GetRaw "/markers") | ConvertFrom-Json).anchors.Count
Write-Host ("baseline: " + $preNodes + " nodes, " + $preMarkers + " live markers")

Write-Host "`n=== MARKER STORM (12 rapid mixed-type creates) ==="
$made=0
$types=@('Point','Axis','Plane','Volume','Path')
for($i=0;$i -lt 12;$i++){
  $ty=$types[$i % 5]
  $body = '{"parentId":' + $chassis + ',"name":"ST_' + $ty + '_' + $i + '","type":"' + $ty + '","visual":true,"worldPosition":[' + ($i*0.3) + ',1.5,0],"worldRotation":[0,0,0,1]'
  if($ty -eq 'Volume'){ $body += ',"volumeShape":"box","halfExtents":[0.4,0.4,0.4]' }
  if($ty -eq 'Path'){ $body += ',"knots":[0,1.5,0,1,1.5,0,1,1.5,1]' }
  $body += '}'
  $r = Post "/marker" $body
  if($r -like '*created*'){ $made++ } else { Write-Host ("    create failed: " + $r) }
}
T "12 markers created rapidly" ($made -eq 12) ("created: " + $made)
Start-Sleep -Milliseconds 400
$mk = (GetRaw "/markers") | ConvertFrom-Json
T "live marker feed grew by 12" ($mk.anchors.Count -eq ($preMarkers+12)) ("now: " + $mk.anchors.Count)
# markers add MORE nodes than markers: AIAnchors group + knot children for paths. Measure, don't assume.
$jm = (GetRaw "/scene?light=1") | ConvertFrom-Json
$markerNodesAdded = $jm.nodes.Count - $preNodes
Write-Host ("  (marker storm added " + $markerNodesAdded + " scene nodes: anchors groups + markers + path knots)")

Write-Host "`n=== SPAWN + DELETE LIFECYCLE (3 pickups) ==="
$ids=@()
for($i=0;$i -lt 3;$i++){
  $r = Post "/spawn_prefab" '{"path":"Assets/Low Poly Vehicle Pack Stylized/Prefabs/New Desgined Models/Pick Up_21.prefab","position":[20,0,20]}'
  if($r -match '"id":(-?\d+)'){ $ids += [int]$Matches[1] }
}
T "3 spawns returned ids" ($ids.Count -eq 3) ($ids -join ', ')
T "spawn ids distinct" (($ids | Select-Object -Unique).Count -eq 3) ""
Start-Sleep -Milliseconds 400
$full = (GetRaw "/scene") | ConvertFrom-Json
$light = (GetRaw "/scene?light=1") | ConvertFrom-Json
T "light and full node counts EQUAL" ($full.nodes.Count -eq $light.nodes.Count) ("full=" + $full.nodes.Count + " light=" + $light.nodes.Count)
$del=0
foreach($id in $ids){ $r = Post "/delete" ('{"id":' + $id + '}'); if($r -like '*deleted*'){ $del++ } else { Write-Host ("    delete failed: " + $r) } }
T "3 deletes succeeded" ($del -eq 3) ""
Start-Sleep -Milliseconds 400
$j3 = (GetRaw "/scene?light=1") | ConvertFrom-Json
T "node count restored after deletes" ($j3.nodes.Count -eq ($preNodes+$markerNodesAdded)) ("now=" + $j3.nodes.Count)

Write-Host "`n=== BONE DRIFT SWEEP (8 bones, random rots, verify zero scale drift) ==="
$j5 = (GetRaw "/scene?light=1") | ConvertFrom-Json
$bones = $j5.nodes | Where-Object { $_.path -like '*Rig/B-root*' -and ($_.name -match 'B-(spine|chest|neck|upperArm|forearm|thigh|shin|head)') } | Select-Object -First 8
if($bones.Count -lt 4){
  T "bone sweep skipped (no rigged character in scene)" $true ""
}else{
  $snap=@{}
  foreach($b in $bones){ $snap[[int]$b.id] = @([float]$b.t.r[0],[float]$b.t.r[1],[float]$b.t.r[2]) }
  $rand = New-Object System.Random(7)
  $sweepOK=$true
  foreach($b in $bones){
    $rx=[Math]::Round($rand.NextDouble()*60-30,1);$ry=[Math]::Round($rand.NextDouble()*60-30,1);$rz=[Math]::Round($rand.NextDouble()*60-30,1)
    $r = Post "/apply" ('{"edits":[{"id":' + $b.id + ',"rotationEuler":[' + $rx + ',' + $ry + ',' + $rz + ']}]}')
    if($r -like 'EXC:*'){ $sweepOK=$false }
  }
  T "8 bone rotations applied" $sweepOK ""
  Start-Sleep -Milliseconds 500
  $j6raw = GetRaw "/scene?light=1"
  if($j6raw -like 'EXC:*'){ T "scene readable after sweep" $false $j6raw }
  else{
    $j6 = $j6raw | ConvertFrom-Json
    $drift=0
    $allBones = $j6.nodes | Where-Object { $_.path -like '*Rig/B-*' }
    foreach($b in $allBones){ if(([Math]::Abs($b.l.s[0]-1) -gt 0.001) -or ([Math]::Abs($b.l.s[1]-1) -gt 0.001) -or ([Math]::Abs($b.l.s[2]-1) -gt 0.001)){ $drift++; Write-Host ("    scale drift on " + $b.name + ": [" + ($b.l.s -join ',') + "]") } }
    T "ZERO scale drift across entire skeleton" ($drift -eq 0) ("checked " + $allBones.Count + " bones")
  }
  $restoreEdits=@()
  foreach($b in $bones){ $r0=$snap[[int]$b.id]; $restoreEdits += ('{"id":' + $b.id + ',"rotationEuler":[' + $r0[0] + ',' + $r0[1] + ',' + $r0[2] + ']}') }
  $r = Post "/apply" ('{"edits":[' + ($restoreEdits -join ',') + ']}')
  T "skeleton restored in one batch" ($r -like '*applied*') $r
}

Write-Host "`n=== CLEANUP (destroy ALL stress rigs by id, verify orphan filtering) ==="
$jc = (GetRaw "/scene?light=1") | ConvertFrom-Json
$rigs = $jc.nodes | Where-Object { $_.path -eq 'SB_TestRig' }
foreach($rg in $rigs){ $r = Post "/delete" ('{"id":' + $rg.id + '}'); Write-Host ("  deleted rig id " + $rg.id + ": " + $r) }
Start-Sleep -Milliseconds 500
$mk3raw = GetRaw "/markers"
$mk3 = $mk3raw | ConvertFrom-Json
T "orphaned stress markers filtered from live feed" ($mk3.anchors.Count -le $preMarkers) ("live markers: " + $mk3.anchors.Count)
$j7 = (GetRaw "/scene?light=1") | ConvertFrom-Json
Write-Host ("final scene: " + $j7.nodes.Count + " nodes")

Write-Host "`n=== DOMAIN RELOAD DRILL (LAST on purpose - blackout is ASYNC) ==="
Write-Host "  POSTing /refresh; a compile+reload may start up to ~60s later and black out"
Write-Host "  the bridge for 10-90s. PASS = bridge is answering again within 3 minutes."
$null = Post "/refresh" "{}"
$sawDown=$false;$recovered=$false
for($i=0;$i -lt 90;$i++){
  Start-Sleep -Seconds 2
  $p = GetRaw "/ping"
  if($p -like 'EXC:*'){ $sawDown=$true }
  elseif($p -like '*"ok":true*'){ if($sawDown -or $i -gt 30){ $recovered=$true; break } elseif($i -eq 89){ $recovered=$true } }
}
if(-not $sawDown){ Write-Host "  (no blackout observed - nothing needed recompiling)" }
$p = GetRaw "/ping"
T "bridge answering after reload drill" ($p -like '*"ok":true*') ("sawBlackout=" + $sawDown)

Write-Host "`n=== SUMMARY RUN 2 ==="
Write-Host ("PASS: " + $pass + "   FAIL: " + $fail)
$results | Where-Object { -not $_.ok } | ForEach-Object { Write-Host ("  FAILED: " + $_.test + " - " + $_.detail) }
