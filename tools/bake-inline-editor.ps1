# Bake the polished inline editor (widget/inline-editor.tmpl.html) full of the LIVE scene, so an
# agent can emit a professional in-chat 3D editor instead of hand-rolling one. Output:
# widget/inline-editor.ready.html -- read it and pass its contents to show_widget.
#   -Focus    : names to include (default: all mesh nodes)
#   -MaxVerts : total vertex budget across included meshes (default 60000); meshes beyond it skipped
#   -Port     : bridge port to target (default: auto-discover). The MCP server passes this so a
#               global install hits the CURRENT project's bridge, not the first responder.
#   -OutFile  : where to write the baked HTML (default: widget/inline-editor.ready.html)
param([string[]]$Focus=@(), [int]$MaxVerts=60000, [int]$Port=0, [string]$OutFile='')
# allow a single comma-separated -Focus arg (how the MCP server passes it)
if($Focus.Count -eq 1 -and $Focus[0] -match ','){ $Focus = $Focus[0] -split ',' | ForEach-Object { $_.Trim() } }

$scriptDir = Split-Path $MyInvocation.MyCommand.Path -Parent
$repo = Split-Path $scriptDir -Parent
$tmpl = Join-Path $repo "widget\inline-editor.tmpl.html"
$out  = if($OutFile){ $OutFile } else { Join-Path $repo "widget\inline-editor.ready.html" }
function Round4($v){ return [Math]::Round([double]$v,4) }
function Rnd3($a){ return @((Round4 $a[0]),(Round4 $a[1]),(Round4 $a[2])) }
# quantize RAW Unity geometry to base64 uint16 (the template dequantizes + mirrors)
function EncodeGeo($mesh){
  $v = $mesh.v
  $min=@([double]::MaxValue,[double]::MaxValue,[double]::MaxValue)
  $max=@([double]::MinValue,[double]::MinValue,[double]::MinValue)
  for($k=0;$k -lt $v.Count;$k++){ $ax=$k%3; $val=[double]$v[$k]; if($val -lt $min[$ax]){$min[$ax]=$val}; if($val -gt $max[$ax]){$max[$ax]=$val} }
  $size=@(0.0,0.0,0.0); for($a=0;$a -lt 3;$a++){ $size[$a]=$max[$a]-$min[$a]; if($size[$a] -lt 1e-6){$size[$a]=1e-6} }
  $pb = New-Object byte[] ($v.Count*2)
  for($k=0;$k -lt $v.Count;$k++){ $ax=$k%3; $q=[int][Math]::Round((([double]$v[$k]-$min[$ax])/$size[$ax])*65535); if($q -lt 0){$q=0}; if($q -gt 65535){$q=65535}; $pb[$k*2]=$q -band 0xFF; $pb[$k*2+1]=($q -shr 8) -band 0xFF }
  $ix = $mesh.i
  $ib = New-Object byte[] ($ix.Count*2)
  for($k=0;$k -lt $ix.Count;$k++){ $iv=[int]$ix[$k]; $ib[$k*2]=$iv -band 0xFF; $ib[$k*2+1]=($iv -shr 8) -band 0xFF }
  return [ordered]@{ pv=[Convert]::ToBase64String($pb); ix=[Convert]::ToBase64String($ib); gmin=@((Round4 $min[0]),(Round4 $min[1]),(Round4 $min[2])); gsize=@((Round4 $size[0]),(Round4 $size[1]),(Round4 $size[2])) }
}

# use the passed -Port if given, else discover (project-local file, then scan 8787..8796)
if($Port -gt 0){ $port = $Port }
else {
  $port = $null
  $pf = Join-Path $repo "Library\scenebridge.port"
  if(Test-Path $pf){ try { $p=[int]((Get-Content $pf -Raw).Trim()); $null=Invoke-RestMethod -Uri ("http://localhost:$p/ping") -TimeoutSec 3; $port=$p } catch {} }
  if(-not $port){ for($p=8787;$p -le 8796;$p++){ try { $null=Invoke-RestMethod -Uri ("http://localhost:$p/ping") -TimeoutSec 2; $port=$p; break } catch {} } }
}
if(-not $port){ Write-Host "No SceneBridge on 8787..8796 - is Unity open?"; exit 1 }
$B = "http://localhost:$port"
Write-Host ("baking from bridge on port " + $port)

$scene = Invoke-RestMethod -Uri "$B/scene" -TimeoutSec 60
$markers = Invoke-RestMethod -Uri "$B/markers" -TimeoutSec 15

$nodes = New-Object System.Collections.ArrayList
$used=0; $skipped=0
foreach($n in $scene.nodes){
  if($n.kind -ne 'mesh' -or -not $n.mesh){ continue }
  if($Focus.Count -gt 0 -and ($Focus -notcontains $n.name)){ continue }
  $vc = $n.mesh.v.Count/3
  if($used + $vc -gt $MaxVerts){ $skipped++; continue }
  # Non-primitive meshes ship base64 uint16 index buffers, which cannot address past 65535 verts;
  # skip anything larger rather than silently wrapping its triangles.
  if($vc -gt 65535){ $skipped++; continue }
  $used += $vc
  $o = [ordered]@{ id=$n.id; name=$n.name; p=(Rnd3 $n.t.p); r=(Rnd3 $n.t.r); s=(Rnd3 $n.t.s) }
  # Unity primitives share one mesh: cube=72 floats (24 verts), cylinder=264 (88). Reference the
  # template's shared geometry instead of shipping verts. Real/custom meshes ship as base64.
  $vcount = $n.mesh.v.Count
  if($vcount -eq 72){ $o.geo = "cube" }
  elseif($vcount -eq 264){ $o.geo = "cyl" }
  else { $g = EncodeGeo $n.mesh; $o.pv=$g.pv; $o.ix=$g.ix; $o.gmin=$g.gmin; $o.gsize=$g.gsize }
  if($n.tint){ $o.tint = (Rnd3 $n.tint) }
  [void]$nodes.Add([pscustomobject]$o)
}

$mk = New-Object System.Collections.ArrayList
foreach($m in $markers.anchors){
  $w = $m.world -split ' '
  $o = [ordered]@{ name=$m.name; type=$m.type; host=(($m.host -split '/')[-1]); w=@((Round4 $w[0]),(Round4 $w[1]),(Round4 $w[2])) }
  if($m.type -eq 'Path' -and $m.knots){ $o.knots = @(($m.knots -split ' ') | ForEach-Object { Round4 $_ }) }
  [void]$mk.Add([pscustomobject]$o)
}

# per-object ConvertTo-Json then join with newlines: always a valid JS array (never collapses),
# and one node per line keeps every line short enough for tools to read the ready file back.
$nodesJs = "[`n" + (($nodes | ForEach-Object { $_ | ConvertTo-Json -Depth 6 -Compress }) -join ",`n") + "`n]"
$markersJs = "[" + (($mk | ForEach-Object { $_ | ConvertTo-Json -Depth 6 -Compress }) -join ",`n") + "]"
$sceneJs = ($scene.scene | ConvertTo-Json -Compress)

$html = Get-Content $tmpl -Raw
$html = $html.Replace('/*__NODES__*/ []', $nodesJs)
$html = $html.Replace('/*__MARKERS__*/ []', $markersJs)
$html = $html.Replace('/*__SCENE__*/ "scene"', $sceneJs)
[System.IO.File]::WriteAllText($out, $html, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ("baked " + $nodes.Count + " meshes, " + $mk.Count + " markers, " + [Math]::Round((Get-Item $out).Length/1024,1) + " KB")
if($skipped -gt 0){ Write-Host ("  skipped " + $skipped + " heavy meshes over the vert budget (use -Focus or raise -MaxVerts)") }
Write-Host ("  emit: read the ready file and pass its contents to show_widget")
