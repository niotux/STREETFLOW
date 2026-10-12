const $ = id => document.getElementById(id);
const escape = value => String(value ?? '—').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const number = (n, unit = '') => n == null ? '—' : `${Number(n).toFixed(1)}${unit}`;
let scenarios = [], run = null, network = null, snapshot = null, selected = null, tab = 'vehicle', offset = 0, totalTrips = 0;
let generation = 0, busy = false, lastDetails = 0, observationCursor = 0, frames = new Map(), route = new Set();
const canvas = $('map'), ctx = canvas.getContext('2d');
let view = {x:0,y:0,scale:1}, width = 1, height = 1, drag = null;
async function api(path, method = 'GET', body) {
 const response = await fetch(`/api/v1${path}`, {method, headers:body ? {'Content-Type':'application/json'} : {}, body:body ? JSON.stringify(body) : undefined});
 const data = await response.json();
 if (!response.ok) throw new Error(data.message || data.detail || `Request failed (${response.status})`);
 return data;
}
function error(e) { $('error').textContent = e.message; $('error').hidden = false; }
function clearError() { $('error').hidden = true; }
function active(r = run) { return r && ['running','paused'].includes(r.state); }
function controls() {
 $('state').textContent = run ? `${run.state} · ${run.id.slice(0,8)}` : 'No run loaded';
 for (const command of ['resume','pause','step','stop','reset']) $(''+command).disabled = busy || !run || (command === 'reset' ? run.state === 'running' : command === 'pause' ? run.state !== 'running' : command === 'stop' ? !active() : run.state !== 'paused');
 $('speed').disabled = busy || !active(); $('load').disabled = busy || active();
 $('metrics').innerHTML = run ? [['Time',`${number(run.simulation_time)} / ${number(run.configuration.duration_seconds)} s`],['Active cars',run.active_vehicles],['Inserted',run.inserted_vehicles],['Arrived',run.arrived_vehicles],['Teleports',run.teleport_events]].map(([a,b])=>`<dt>${a}</dt><dd>${escape(b)}</dd>`).join('') : '';
}
async function history() {
 const runs = await api('/runs');
 $('history').innerHTML = '<option value="">Choose a run</option>' + runs.map(r=>`<option value="${escape(r.id)}">${escape(r.configuration.scenario_id)} · ${escape(r.state)} · ${escape(r.id.slice(0,8))}</option>`).join('');
 if(run) $('history').value = run.id;
 return runs;
}
async function selectRun(id) {
 const token = ++generation;
 network = null; snapshot = null; selected = null; route.clear(); frames.clear(); observationCursor = 0; offset = 0; lastDetails = 0;
 $('map-label').textContent = 'Loading road network…';
 for(const panel of ['trip-table','detectors','passages']) $(panel).innerHTML = '<p class="muted">Loading run records…</p>';
 $('vehicle').innerHTML = '<p class="muted">Click a car on the map or a row in Trips to inspect its journey.</p>';
 const status = await api(`/runs/${encodeURIComponent(id)}`);
 if(token !== generation) return;
 run = status; controls(); $('history').value = id;
 const map = {lanes:[],junctions:[]};
 for(let start=0;;start+=10000) {
  const page = await api(`/runs/${id}/map?offset=${start}&limit=10000`);
  if(token !== generation) return;
  map.lanes.push(...page.lanes); map.junctions.push(...page.junctions);
  Object.assign(map,{cameras:page.cameras,checkpoints:page.checkpoints,attribution:page.attribution});
  if(start+10000 >= Math.max(page.total_lanes,page.total_junctions)) break;
 }
 network = map; network.byLane = new Map(map.lanes.map(l=>[l.id,l]));
 $('map-label').textContent = `${status.configuration.scenario_id} · ${map.lanes.length.toLocaleString()} lanes`;
 $('attribution').textContent = map.attribution; $('empty').hidden = true;
 $('speed').value = status.pacing.mode === 'unpaced' ? 'unpaced' : String(status.pacing.multiplier);
 fit(); await refresh(token); await history();
}
async function action(work) {
 if(busy) return;
 busy = true; ++generation; clearError(); controls();
 try { await work(); } catch(e) { error(e); }
 finally { busy = false; controls(); }
}
$('create').addEventListener('submit', e => { e.preventDefault(); action(async()=>{
 const created = await api('/runs','POST',{scenario_id:$('scenario').value,request_id:crypto.randomUUID(),seed:Number($('seed').value),start_paused:true,overrides:{duration_seconds:Number($('duration').value),demand_multiplier:Number($('demand').value)}});
 await selectRun(created.id);
}); });
for(const command of ['resume','pause','step','stop','reset']) $(command).onclick = ()=>action(async()=>{
 const body = {request_id:crypto.randomUUID()};
 if(command === 'step') body.steps = Math.max(1,Math.min(1000,Math.round(5/run.configuration.step_seconds)));
 const result = await api(`/runs/${run.id}/${command}`,'POST',body);
 if(command === 'reset') await selectRun(result.id);
 else { run=result; await refresh(generation); await history(); }
});
$('speed').onchange = ()=>action(async()=>{
 const value = $('speed').value;
 run = await api(`/runs/${run.id}/pacing`,'PATCH',{request_id:crypto.randomUUID(),mode:value==='unpaced'?'unpaced':'realtime',multiplier:value==='unpaced'?1:Number(value)});
});
$('history').onchange = ()=> { if($('history').value) action(()=>selectRun($('history').value)); };
function scenarioChanged() {
 const info=scenarios.find(s=>s.manifest.id===$('scenario').value); if(!info) return;
 $('duration').value=info.manifest.duration_seconds;
 $('scenario-note').textContent=`${info.scheduled_vehicles} scheduled cars. ${info.manifest.evidence_level}. Demand is synthetic; this is not calibrated city traffic.`;
}
$('scenario').onchange=scenarioChanged;
document.querySelectorAll('[data-tab]').forEach(button=>button.onclick=()=>{
 tab=button.dataset.tab;
 document.querySelectorAll('[data-tab]').forEach(b=>b.classList.toggle('selected',b===button));
 for(const name of ['vehicle','trips','detectors','passages']) $(name).hidden = name!==tab;
 lastDetails=0;
});
$('previous').onclick=()=>{offset=Math.max(0,offset-25);lastDetails=0;};
$('next').onclick=()=>{if(offset+25<totalTrips) offset+=25;lastDetails=0;};
async function refresh(token) {
 if(!run || !network) return;
 const id=run.id;
 const bounds = `&min_x=${view.x-width/2/view.scale}&max_x=${view.x+width/2/view.scale}&min_y=${view.y-height/2/view.scale}&max_y=${view.y+height/2/view.scale}`;
 const [status,snap] = await Promise.all([api(`/runs/${id}`),api(`/runs/${id}/snapshot?limit=10000${bounds}`)]);
 if(token!==generation) return;
 run=status; snapshot=snap; controls();
 $('snapshot-note').textContent = snap.available ? `${snap.returned_count} cars in view${snap.sampled?' · display sampled':''} · orange = stopped` : 'Archived run · live positions unavailable; trip records remain available';
 $('connection').textContent='Connected';
 if(run.failure) error(new Error(run.failure));
 draw();
 if(Date.now()-lastDetails<1000) return;
 lastDetails=Date.now();
 if(tab==='vehicle' && selected) {
  const detail = await api(`/runs/${id}/vehicles/${encodeURIComponent(selected)}`);
  if(token!==generation || selected!==detail.trip.id) return;
  route=new Set(detail.trip.route || []); const t=detail.trip, v=detail.current;
  $('vehicle').innerHTML=`<div class="detail">${[['Vehicle',t.id],['Status',t.status],['Speed',v?number(v.speed_mps*3.6,' km/h'):'—'],['Departure',number(t.departure,' s')],['Arrival',number(t.arrival,' s')],[t.status==='completed'?'Completed journey':'Elapsed journey',number(t.duration,' s')],['Waiting',number(t.waiting_time,' s')],['Time lost',number(t.time_loss,' s')],['Current lane',v?.lane_id]].map(([a,b])=>`<div><span>${a}</span><strong>${escape(b)}</strong></div>`).join('')}</div><p class="route">Route: ${escape((t.route||[]).join(' → '))}</p>`;
 } else if(tab==='trips') {
  const data=await api(`/runs/${id}/trips?offset=${offset}&limit=25`); if(token!==generation) return;
  totalTrips=data.total; $('trip-page').textContent=`${data.total?offset+1:0}–${Math.min(offset+25,data.total)} of ${data.total}`;
  $('previous').disabled=offset===0; $('next').disabled=offset+25>=data.total;
  $('trip-table').innerHTML=table(['Vehicle','Status','Departure (s)','Arrival (s)','Duration (s)','Waiting (s)'],data.trips.map(t=>[t.id,t.status,number(t.departure),number(t.arrival),number(t.duration),number(t.waiting_time)]),data.trips.map(t=>t.id));
  $('trip-table').querySelectorAll('[data-id]').forEach(row=>row.onclick=()=>pick(row.dataset.id));
 } else if(tab==='detectors') {
  const data=await api(`/runs/${id}/observations?after=${observationCursor}&limit=1000`); if(token!==generation) return;
  observationCursor=data.next_cursor;
  for(const frame of data.frames) frames.set(frame.detector_id,frame);
  $('detectors').innerHTML=frames.size ? '<p class="muted">Ideal simulated cameras. Crossed = vehicles crossing the counting line during the displayed window. Speed is the lane mean.</p>'+table(['Detector / lane','Window (s)','Visible','Crossed','Queued','Speed (km/h)'],[...frames.values()].flatMap(f=>f.lanes.map(l=>[`${f.detector_id} / ${l.lane_id}`,`${number(f.window.start)}–${number(f.window.end)}`,l.visible_vehicle_count,l.crossed_vehicle_count,l.queued_vehicle_count,l.mean_speed_mps==null?'—':number(l.mean_speed_mps*3.6)]))) : '<p class="muted">No completed observation window yet. Run or step the simulation.</p>';
 } else if(tab==='passages') {
  const data=await api(`/runs/${id}/passages?limit=100`); if(token!==generation) return;
  $('passages').innerHTML=data.total ? `<p class="muted">Checkpoint-to-checkpoint journeys · showing ${data.passages.length} of ${data.total}</p>`+table(['Pair','Vehicle','Started (s)','Finished (s)','Duration (s)','Status'],data.passages.map(p=>[p.pair_id,p.vehicle_id,number(p.started_at),number(p.finished_at),number(p.duration_seconds),p.status])) : '<p class="muted">No checkpoint passages recorded. The tiny intersection includes a west-to-east checkpoint pair.</p>';
 }
}
function table(headers,rows,ids) { return `<table><thead><tr>${headers.map(h=>`<th>${escape(h)}</th>`).join('')}</tr></thead><tbody>${rows.map((r,i)=>`<tr${ids?` data-id="${escape(ids[i])}"`:''}>${r.map(c=>`<td>${escape(c)}</td>`).join('')}</tr>`).join('')}</tbody></table>`; }
function pick(id) { selected=id; lastDetails=0; document.querySelector('[data-tab="vehicle"]').click(); draw(); }
function screen(x,y) {return {x:width/2+(x-view.x)*view.scale,y:height/2-(y-view.y)*view.scale};}
function fit() {
 if(!network?.lanes.length) return;
 let minX=Infinity,minY=Infinity,maxX=-Infinity,maxY=-Infinity;
 for(const lane of network.lanes) for(const p of lane.shape) {minX=Math.min(minX,p.x);maxX=Math.max(maxX,p.x);minY=Math.min(minY,p.y);maxY=Math.max(maxY,p.y);}
 view={x:(minX+maxX)/2,y:(minY+maxY)/2,scale:Math.min(width/Math.max(1,maxX-minX),height/Math.max(1,maxY-minY))*.88};draw();
}
function line(points) { if(!points.length) return; ctx.beginPath(); points.forEach((p,i)=>{const s=screen(p.x,p.y); i?ctx.lineTo(s.x,s.y):ctx.moveTo(s.x,s.y);});ctx.stroke(); }
function at(lane,position) {
 const shape=lane.shape; let length=0; for(let i=1;i<shape.length;i++) length+=Math.hypot(shape[i].x-shape[i-1].x,shape[i].y-shape[i-1].y);
 let remaining=position/Math.max(1,lane.length)*length;
 for(let i=1;i<shape.length;i++) {const a=shape[i-1],b=shape[i],d=Math.hypot(b.x-a.x,b.y-a.y);if(remaining<=d && d>0) return {x:a.x+(b.x-a.x)*remaining/d,y:a.y+(b.y-a.y)*remaining/d};remaining-=d;}
 return shape.at(-1);
}
function draw() {
 ctx.clearRect(0,0,width,height); if(!network) return;
 ctx.lineCap='round'; ctx.lineJoin='round';
 for(const lane of network.lanes) {ctx.strokeStyle=route.has(lane.edge_id)?'#649fcb':lane.id.startsWith(':')?'#a5b3aa':'#8e9f94';ctx.lineWidth=Math.max(1,Math.min(10,3.2*view.scale));line(lane.shape);}
 if($('cameras').checked) for(const camera of network.cameras) for(const region of camera.lanes) {
  const lane=network.byLane.get(region.lane_id);if(!lane) continue;
  const points=[at(lane,region.start_position)];
  for(let pos=region.start_position+5;pos<region.end_position;pos+=5) points.push(at(lane,pos));
  points.push(at(lane,region.end_position));ctx.strokeStyle='#26a99470';ctx.lineWidth=6;line(points);
  const p=screen(at(lane,region.count_position).x,at(lane,region.count_position).y);ctx.fillStyle='#168474';ctx.fillRect(p.x-2,p.y-2,4,4);
 }
 for(const checkpoint of network.checkpoints?.checkpoints || []) {const lane=network.byLane.get(checkpoint.lane_id);if(!lane) continue;const point=at(lane,checkpoint.position),p=screen(point.x,point.y);ctx.strokeStyle='#805ac1';ctx.lineWidth=2;ctx.beginPath();ctx.arc(p.x,p.y,5,0,Math.PI*2);ctx.stroke();}
 for(const signal of snapshot?.signals || []) {const junction=network.junctions.find(j=>j.id===signal.id);if(!junction) continue;const p=screen(junction.x,junction.y);const colors=[...new Set([...signal.state].map(c=>/[gG]/.test(c)?'#3e9e64':/[yY]/.test(c)?'#edb132':'#d15a4c'))];colors.forEach((color,i)=>{ctx.fillStyle=color;ctx.beginPath();ctx.arc(p.x+(i-(colors.length-1)/2)*5,p.y,2.5,0,Math.PI*2);ctx.fill();});}
 for(const car of snapshot?.vehicles || []) {
  const p=screen(car.x,car.y); if(p.x< -20||p.y< -20||p.x>width+20||p.y>height+20) continue;
  ctx.save();ctx.translate(p.x,p.y);ctx.rotate(car.angle*Math.PI/180);ctx.fillStyle=car.id===selected?'#306baa':car.speed_mps<.1?'#dc9143':'#204f46';
  const size=Math.max(5,Math.min(15,4.5*view.scale));ctx.fillRect(-size*.3,-size*.5,size*.6,size);
  if(car.id===selected){ctx.strokeStyle='#306baa';ctx.lineWidth=2;ctx.beginPath();ctx.arc(0,0,size+3,0,Math.PI*2);ctx.stroke();}ctx.restore();
 }
}
new ResizeObserver(()=>{const rect=canvas.getBoundingClientRect();width=rect.width;height=rect.height;const dpr=window.devicePixelRatio||1;canvas.width=Math.round(width*dpr);canvas.height=Math.round(height*dpr);ctx.setTransform(dpr,0,0,dpr,0,0);draw();}).observe(canvas);
$('fit').onclick=fit; $('cameras').onchange=draw;
canvas.onpointerdown=e=>{drag={x:e.clientX,y:e.clientY,originX:e.clientX,originY:e.clientY,moved:false};canvas.setPointerCapture(e.pointerId);};
canvas.onpointermove=e=>{if(!drag)return;view.x-=(e.clientX-drag.x)/view.scale;view.y+=(e.clientY-drag.y)/view.scale;drag.moved ||= Math.hypot(e.clientX-drag.originX,e.clientY-drag.originY)>4;drag.x=e.clientX;drag.y=e.clientY;draw();};
canvas.onpointerup=e=>{if(!drag)return;if(!drag.moved){const rect=canvas.getBoundingClientRect();let closest=null,distance=14;for(const car of snapshot?.vehicles||[]){const p=screen(car.x,car.y),d=Math.hypot(p.x-(e.clientX-rect.left),p.y-(e.clientY-rect.top));if(d<distance){closest=car;distance=d;}}if(closest)pick(closest.id);}drag=null;};
canvas.onpointercancel=()=>{drag=null;};
canvas.addEventListener('wheel',e=>{e.preventDefault();const rect=canvas.getBoundingClientRect(),x=e.clientX-rect.left-width/2,y=e.clientY-rect.top-height/2;const old=view.scale;view.scale=Math.max(.005,Math.min(30,old*Math.exp(-e.deltaY*.001)));view.x+=x/old-x/view.scale;view.y-=y/old-y/view.scale;draw();},{passive:false});
async function poll() {try {if(!busy)await refresh(generation);}catch(e){$('connection').textContent='Connection interrupted';error(e);}finally{setTimeout(poll,document.hidden?1000:200);}}
try {
 scenarios=await api('/scenarios');$('scenario').innerHTML=scenarios.map(s=>`<option value="${escape(s.manifest.id)}">${escape(s.manifest.name)}</option>`).join('');scenarioChanged();
 const runs=await history(); const existing=runs.find(active)||runs[0];if(existing)await selectRun(existing.id);
 $('connection').textContent='Connected';
}catch(e){error(e);$('connection').textContent='Connection interrupted';}
poll();
