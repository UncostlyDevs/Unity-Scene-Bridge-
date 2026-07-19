# Bake a FOCUS-tier widget payload from the live scene.
#   -Focus: node names to bake with FULL geometry (quantized int16 + base64, z-mirrored)
#   -Context: node names (or path prefixes) to include as tinted bounding boxes
# Everything else is listed as hidden (names only). Output: scratchpad focus-bake.js
param(
  [string[]]$Focus = @(),
  [string[]]$Context = @(),
  [int]$ClusterGrid = 0,   # >0: decimate focus meshes by snapping verts to an NxNxN grid (silhouette-preserving)
  [string]$Out = (Join-Path $env:TEMP 'scenebridge-focus-bake.js'),
  [int]$Port = 0           # bridge port to target (default: auto-discover)
)
# Use -Port if given, else discover the bridge (project-local file, then scan 8787..8796).
$repoRoot = Split-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) -Parent
if($Port -gt 0){ $port = $Port }
else {
  $port = $null
  $pf = Join-Path $repoRoot 'Library\scenebridge.port'
  if(Test-Path $pf){ try { $p=[int]((Get-Content $pf -Raw).Trim()); $null=Invoke-RestMethod -Uri ("http://localhost:$p/ping") -TimeoutSec 3; $port=$p } catch {} }
  if(-not $port){ for($p=8787;$p -le 8796;$p++){ try { $null=Invoke-RestMethod -Uri ("http://localhost:$p/ping") -TimeoutSec 2; $port=$p; break } catch {} } }
}
if(-not $port){ Write-Host "No SceneBridge on 8787..8796 - is Unity open?"; exit 1 }
$B="http://localhost:$port"
$full = Invoke-RestMethod -Uri "$B/scene" -TimeoutSec 60
$inv = [System.Globalization.CultureInfo]::InvariantCulture
function F($v){ return [Math]::Round([double]$v,4).ToString($inv) }

function Decimate($mesh,$grid){
  # vertex clustering: merge verts sharing a grid cell, remap tris, drop degenerates
  $v=$mesh.v; $ix=$mesh.i
  $min=@([double]::MaxValue,[double]::MaxValue,[double]::MaxValue)
  $max=@([double]::MinValue,[double]::MinValue,[double]::MinValue)
  for($i=0;$i -lt $v.Count;$i++){ $ax=$i%3; $val=[double]$v[$i]; if($val -lt $min[$ax]){$min[$ax]=$val}; if($val -gt $max[$ax]){$max[$ax]=$val} }
  $size=@(($max[0]-$min[0]),($max[1]-$min[1]),($max[2]-$min[2]))
  for($i=0;$i -lt 3;$i++){ if($size[$i] -lt 1e-6){$size[$i]=1e-6} }
  $cells=@{}; $remap=New-Object int[] ($v.Count/3); $nv=New-Object System.Collections.ArrayList
  for($i=0;$i -lt $v.Count/3;$i++){
    $x=[double]$v[$i*3];$y=[double]$v[$i*3+1];$z=[double]$v[$i*3+2]
    $cx=[int](( ($x-$min[0])/$size[0])*($grid-1));$cy=[int](( ($y-$min[1])/$size[1])*($grid-1));$cz=[int](( ($z-$min[2])/$size[2])*($grid-1))
    $key=([long]$cx*$grid*$grid)+([long]$cy*$grid)+$cz
    if($cells.ContainsKey($key)){ $remap[$i]=$cells[$key] }
    else{ $ni=$nv.Count/3; $cells[$key]=$ni; $remap[$i]=$ni; [void]$nv.Add($x);[void]$nv.Add($y);[void]$nv.Add($z) }
  }
  $ni2=New-Object System.Collections.ArrayList
  for($t=0;$t -lt $ix.Count;$t+=3){
    $a=$remap[[int]$ix[$t]];$b2=$remap[[int]$ix[$t+1]];$c2=$remap[[int]$ix[$t+2]]
    if($a -ne $b2 -and $b2 -ne $c2 -and $a -ne $c2){ [void]$ni2.Add($a);[void]$ni2.Add($b2);[void]$ni2.Add($c2) }
  }
  return @{ v=$nv; i=$ni2 }
}
function Encode-Mesh($mesh){
  if($ClusterGrid -gt 0){ $mesh = Decimate $mesh $ClusterGrid }
  $v = $mesh.v
  $n = $v.Count / 3
  $min=@([double]::MaxValue,[double]::MaxValue,[double]::MaxValue)
  $max=@([double]::MinValue,[double]::MinValue,[double]::MinValue)
  for($i=0;$i -lt $v.Count;$i++){
    $ax=$i%3; $val=[double]$v[$i]
    if($ax -eq 2){ $val = -$val } # z-mirror to match Unity view
    if($val -lt $min[$ax]){$min[$ax]=$val}; if($val -gt $max[$ax]){$max[$ax]=$val}
  }
  $size=@(($max[0]-$min[0]),($max[1]-$min[1]),($max[2]-$min[2]))
  for($i=0;$i -lt 3;$i++){ if($size[$i] -lt 1e-6){$size[$i]=1e-6} }
  $bytes = New-Object byte[] ($v.Count*2)
  for($i=0;$i -lt $v.Count;$i++){
    $ax=$i%3; $val=[double]$v[$i]; if($ax -eq 2){ $val=-$val }
    $q=[int][Math]::Round((($val-$min[$ax])/$size[$ax])*32767)
    if($q -lt 0){$q=0}; if($q -gt 32767){$q=32767}
    $bytes[$i*2]=$q -band 0xFF; $bytes[$i*2+1]=($q -shr 8) -band 0xFF
  }
  # indices: uint16, winding FLIPPED (mirror inverts orientation)
  $ix=$mesh.i
  $ib = New-Object byte[] ($ix.Count*2)
  for($t=0;$t -lt $ix.Count;$t+=3){
    $tri=@([int]$ix[$t],[int]$ix[$t+2],[int]$ix[$t+1]) # swapped = winding flip
    for($k=0;$k -lt 3;$k++){ $ib[($t+$k)*2]=$tri[$k] -band 0xFF; $ib[($t+$k)*2+1]=($tri[$k] -shr 8) -band 0xFF }
  }
  return @{
    n=$n
    min=("["+(F $min[0])+","+(F $min[1])+","+(F $min[2])+"]")
    size=("["+(F $size[0])+","+(F $size[1])+","+(F $size[2])+"]")
    pv=(Wrap ([Convert]::ToBase64String($bytes)))
    ix=(Wrap ([Convert]::ToBase64String($ib)))
  }
}
# wrap long base64 into concatenated JS string chunks so tooling can read the file line-by-line
function Wrap($s){
  if($s.Length -le 1500){ return $s }
  $sb=New-Object System.Text.StringBuilder
  for($i=0;$i -lt $s.Length;$i+=1500){
    $len=[Math]::Min(1500,$s.Length-$i)
    [void]$sb.Append($s.Substring($i,$len))
    if(($i+1500) -lt $s.Length){ [void]$sb.Append("`"+`n`"") }
  }
  return $sb.ToString()
}

$focusLines=@(); $ctxLines=@(); $hidden=@()
foreach($n in $full.nodes){
  $isFocus = ($Focus -contains $n.name) -or ($Focus -contains $n.path)
  if(($Focus | Where-Object { $_ -like '*/*' }).Count -gt 0){ $isFocus = ($Focus -contains $n.path) } # paths given -> exact path matching only
  $isCtx = $false
  foreach($c in $Context){ if($n.name -like $c -or $n.path -like ($c+"*")){ $isCtx=$true; break } }
  if($isFocus -and $n.kind -eq 'mesh' -and $n.mesh){
    $enc = Encode-Mesh $n.mesh
    $tint = if($n.tint){ ",tint:[" + (F $n.tint[0]) + "," + (F $n.tint[1]) + "," + (F $n.tint[2]) + "]" } else { "" }
    $focusLines += (" {id:" + $n.id + ',name:"' + $n.name + '",p:[' + (F $n.t.p[0]) + "," + (F $n.t.p[1]) + "," + (F (-$n.t.p[2])) + '],ry:' + (F (-$n.t.r[1])) + ',s:[' + (F $n.t.s[0]) + "," + (F $n.t.s[1]) + "," + (F $n.t.s[2]) + '],n:' + $enc.n + ',min:' + $enc.min + ',size:' + $enc.size + ',pv:"' + $enc.pv + '",ix:"' + $enc.ix + '"' + $tint + "}")
  }
  elseif($isCtx -and ($n.kind -eq 'mesh' -or $n.kind -eq 'box')){
    # oriented bounds box proxy: use world pos + scale of the node (mesh local bbox approximated by unit cube * scale)
    $tint = if($n.tint){ ",tint:[" + (F $n.tint[0]) + "," + (F $n.tint[1]) + "," + (F $n.tint[2]) + "]" } else { "" }
    $ctxLines += (" {id:" + $n.id + ',name:"' + $n.name + '",p:[' + (F $n.t.p[0]) + "," + (F $n.t.p[1]) + "," + (F (-$n.t.p[2])) + '],ry:' + (F (-$n.t.r[1])) + ',s:[' + (F $n.t.s[0]) + "," + (F $n.t.s[1]) + "," + (F $n.t.s[2]) + "]" + $tint + "}")
  }
  elseif($n.kind -eq 'mesh'){
    $hidden += ('"' + $n.name + '"')
  }
}
$payload = "const FOCUS=[`n" + ($focusLines -join ",`n") + "`n];`nconst CTX=[`n" + ($ctxLines -join ",`n") + "`n];`nconst HIDDEN=[" + ($hidden -join ",") + "];`n"
[System.IO.File]::WriteAllText($Out, $payload, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("focus meshes: " + $focusLines.Count + "   context boxes: " + $ctxLines.Count + "   hidden: " + $hidden.Count)
Write-Host ("payload: " + [Math]::Round($payload.Length/1024,1) + " KB -> " + $Out)