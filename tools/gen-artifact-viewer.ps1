# Generate a SELF-CONTAINED full-fidelity scene viewer HTML for the Artifact surface.
# Geometry goes disk->disk: it never passes through the AI's context, so size is a non-issue.
# three.js is inlined (artifact CSP blocks all external hosts). View-only + inspect.
param(
  [string]$Out = (Join-Path $env:TEMP 'scenebridge-scene-viewer.html'),
  [string]$ThreeJs = '',  # path to a three.min.js UMD build; auto-fetched to a temp cache if omitted
  [int]$Port = 0          # bridge port to target (default: auto-discover)
)
$ErrorActionPreference = 'Stop'
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
# three.js UMD build to inline. Supply your own with -ThreeJs, else fetch it once to a temp cache.
if(-not $ThreeJs){
  $ThreeJs = Join-Path $env:TEMP 'scenebridge-three.min.js'
  if(-not (Test-Path $ThreeJs)){
    Write-Host "downloading three.min.js (one-time) ..."
    Invoke-WebRequest -Uri 'https://cdn.jsdelivr.net/npm/three@0.160.0/build/three.min.js' -OutFile $ThreeJs -UseBasicParsing
  }
}
$three = Get-Content $ThreeJs -Raw
$scene = (Invoke-WebRequest -Uri "$B/scene" -TimeoutSec 60 -UseBasicParsing).Content
$markers = (Invoke-WebRequest -Uri "$B/markers" -TimeoutSec 15 -UseBasicParsing).Content

$viewer = @'
<style>
html,body{margin:0;height:100%;overflow:hidden;background:#14171c;color:#e8edf4;font:13px system-ui}
#c{position:fixed;inset:0}
#hud{position:fixed;top:10px;left:10px;background:rgba(20,23,28,.85);border:1px solid #2c323c;border-radius:8px;padding:8px 12px;max-width:300px}
#tag{position:fixed;display:none;background:rgba(22,26,32,.9);color:#eef2f7;font-size:12px;padding:3px 9px;border-radius:5px;pointer-events:none}
#lg{position:fixed;top:10px;right:10px;background:rgba(20,23,28,.85);border:1px solid #2c323c;border-radius:8px;padding:8px 10px;font-size:11.5px;max-height:60vh;overflow-y:auto}
.mk{display:flex;align-items:center;gap:6px;padding:1px 0}
.dot{width:8px;height:8px;border-radius:3px}
</style>
<div id="c"></div>
<div id="hud"><b>SceneBridge - full-fidelity viewer</b><div id="stats" style="color:#9aa7b5;margin-top:3px"></div><div style="color:#5f6b78;margin-top:3px">drag orbit | wheel zoom | right-drag pan | hover names | view-only (tell Claude what to change)</div></div>
<div id="tag"></div>
<div id="lg"></div>
<script>__THREE__</script>
<script>
const DATA=__SCENE__;
const MK=__MARKERS__;
const mount=document.getElementById('c');
const renderer=new THREE.WebGLRenderer({antialias:true});
renderer.setPixelRatio(Math.min(2,devicePixelRatio));
renderer.setSize(innerWidth,innerHeight);
mount.appendChild(renderer.domElement);
const scene=new THREE.Scene();scene.background=new THREE.Color(0x1a2029);
scene.add(new THREE.HemisphereLight(0xcfe0f2,0x3a3f46,0.95));
const sun=new THREE.DirectionalLight(0xfff2dd,1.6);sun.position.set(70,110,50);scene.add(sun);
const grid=new THREE.GridHelper(240,120,0x3a4552,0x262d36);scene.add(grid);
const cam=new THREE.PerspectiveCamera(45,innerWidth/innerHeight,0.5,2000);
let goalPos=new THREE.Vector3(85,70,105),goalTarget=new THREE.Vector3(0,0,0),targetV=goalTarget.clone();
cam.position.copy(goalPos);
const meshes=[];let nMesh=0,nVert=0,nBox=0;
DATA.nodes.forEach(n=>{
 if(n.kind==='mesh'&&n.mesh){
  const g=new THREE.BufferGeometry();
  const v=n.mesh.v.slice();for(let k=2;k<v.length;k+=3)v[k]=-v[k];
  const ix=n.mesh.i.slice();for(let k=0;k<ix.length;k+=3){const t=ix[k+1];ix[k+1]=ix[k+2];ix[k+2]=t;}
  g.setAttribute('position',new THREE.Float32BufferAttribute(v,3));g.setIndex(ix);g.computeVertexNormals();
  const col=n.tint?new THREE.Color(n.tint[0],n.tint[1],n.tint[2]):new THREE.Color(0x9098a0);
  const m=new THREE.Mesh(g,new THREE.MeshStandardMaterial({color:col,roughness:.65,metalness:.1}));
  m.position.set(n.t.p[0],n.t.p[1],-n.t.p[2]);
  m.rotation.order='YXZ';
  m.rotation.set(-n.t.r[0]*Math.PI/180,-n.t.r[1]*Math.PI/180,n.t.r[2]*Math.PI/180);
  m.scale.set(n.t.s[0],n.t.s[1],n.t.s[2]);
  m.name=n.name;scene.add(m);meshes.push(m);nMesh++;nVert+=n.mesh.v.length/3;
 }else if(n.kind==='box'&&n.box){
  const bg=new THREE.BoxGeometry(n.box.size[0],n.box.size[1],n.box.size[2]);
  const m=new THREE.Mesh(bg,new THREE.MeshStandardMaterial({color:0x5a6472,transparent:true,opacity:0.4}));
  m.position.set(n.box.c[0],n.box.c[1],-n.box.c[2]);m.name=n.name;scene.add(m);meshes.push(m);nBox++;
 }
});
document.getElementById('stats').textContent=nMesh+' meshes | '+nVert.toLocaleString()+' verts | '+nBox+' bounds | '+(MK.anchors||[]).length+' markers';
const TYPEC={Point:0xff6b35,Axis:0x4da3ff,Plane:0x3fbf7f,Volume:0xb07cff,Path:0x2ac1c9};
const lg=document.getElementById('lg');
(MK.anchors||[]).forEach(a=>{
 const w=a.world.split(' ').map(Number);
 const c=TYPEC[a.type]||0xff6b35;
 const s=new THREE.Mesh(new THREE.SphereGeometry(0.45,12,8),new THREE.MeshBasicMaterial({color:c}));
 s.position.set(w[0],w[1],-w[2]);scene.add(s);
 if(a.type==='Path'&&a.knots){
  const kn=a.knots.split(' ').map(Number);const pts=[];
  for(let k=0;k<kn.length;k+=3)pts.push(new THREE.Vector3(kn[k],kn[k+1],-kn[k+2]));
  scene.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(pts),new THREE.LineBasicMaterial({color:c})));
 }
 const row=document.createElement('div');row.className='mk';
 row.innerHTML='<span class="dot" style="background:#'+c.toString(16).padStart(6,'0')+'"></span>'+a.name;
 lg.appendChild(row);
});
const ray=new THREE.Raycaster(),m2=new THREE.Vector2(),tag=document.getElementById('tag');
let nav=null,lx=0,ly=0;
const dom=renderer.domElement;
dom.addEventListener('contextmenu',e=>e.preventDefault());
dom.addEventListener('pointerdown',e=>{nav=(e.button===0)?'o':'p';lx=e.clientX;ly=e.clientY;});
dom.addEventListener('pointerup',()=>nav=null);
dom.addEventListener('pointermove',e=>{
 if(nav){
  const dx=e.clientX-lx,dy=e.clientY-ly;lx=e.clientX;ly=e.clientY;
  if(nav==='o'){
   const o=goalPos.clone().sub(goalTarget);const s=new THREE.Spherical().setFromVector3(o);
   s.theta-=dx*0.005;s.phi=Math.max(0.05,Math.min(Math.PI-0.05,s.phi-dy*0.005));
   o.setFromSpherical(s);goalPos.copy(goalTarget).add(o);
  }else{
   const dist=goalPos.distanceTo(goalTarget);const fwd=goalTarget.clone().sub(goalPos).normalize();
   const right=fwd.clone().cross(new THREE.Vector3(0,1,0)).normalize();const up=right.clone().cross(fwd).normalize();
   const q=dist*0.0016;const mv=right.multiplyScalar(-dx*q).add(up.multiplyScalar(dy*q));
   goalPos.add(mv);goalTarget.add(mv);
  }
 }else{
  m2.x=(e.clientX/innerWidth)*2-1;m2.y=-(e.clientY/innerHeight)*2+1;
  ray.setFromCamera(m2,cam);
  const h=ray.intersectObjects(meshes,false)[0];
  if(h){tag.style.display='block';tag.style.left=(e.clientX+14)+'px';tag.style.top=(e.clientY+14)+'px';
   tag.textContent=h.object.name+'  ('+h.point.x.toFixed(1)+', '+h.point.y.toFixed(1)+', '+h.point.z.toFixed(1)+')';}
  else tag.style.display='none';
 }
});
dom.addEventListener('wheel',e=>{
 e.preventDefault();
 const o=goalPos.clone().sub(goalTarget);const d=o.length();
 o.multiplyScalar(Math.max(2,Math.min(800,d*Math.exp(e.deltaY*0.0011)))/d);
 goalPos.copy(goalTarget).add(o);
},{passive:false});
addEventListener('resize',()=>{renderer.setSize(innerWidth,innerHeight);cam.aspect=innerWidth/innerHeight;cam.updateProjectionMatrix();});
(function loop(){requestAnimationFrame(loop);
 cam.position.lerp(goalPos,0.18);targetV.lerp(goalTarget,0.18);cam.lookAt(targetV);
 renderer.render(scene,cam);})();
</script>
'@

# Neutralize our splice tokens if they somehow appear in scene data, then escape '</' so a Unity
# object named like "...</script>..." cannot terminate the script tag early. three.js (a fixed
# library) is spliced first; the sanitized data cannot collide with the remaining placeholders.
foreach($tok in '__THREE__','__SCENE__','__MARKERS__'){ $scene=$scene.Replace($tok,$tok.ToLower()); $markers=$markers.Replace($tok,$tok.ToLower()) }
$scene = $scene.Replace('</','<\/'); $markers = $markers.Replace('</','<\/')
$html = $viewer.Replace('__THREE__',$three).Replace('__SCENE__',$scene).Replace('__MARKERS__',$markers)
[System.IO.File]::WriteAllText($Out, $html, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("artifact HTML: " + [Math]::Round((Get-Item $Out).Length/1024) + " KB -> " + $Out)