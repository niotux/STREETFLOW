const $ = id => document.getElementById(id), canvas = $('calibration-canvas'), ctx = canvas.getContext('2d');
let config = null, image = null, mode = null, points = [];
const lane = () => config?.lanes.find(item => item.lane_id === $('lane').value);
const feedback = text => { $('feedback').textContent = text; };
function sync() { $('config-json').value = JSON.stringify(config, null, 2); draw(); }
function draw() {
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  if (image) ctx.drawImage(image, 0, 0, canvas.width, canvas.height);
  function path(vertices, color, close) {
    if (!vertices?.length) return;
    ctx.beginPath(); vertices.forEach(([x, y], index) => index ? ctx.lineTo(x * canvas.width, y * canvas.height) : ctx.moveTo(x * canvas.width, y * canvas.height));
    if (close) ctx.closePath(); ctx.strokeStyle = color; ctx.lineWidth = 4; ctx.stroke();
    if (close) { ctx.fillStyle = '#73dacb33'; ctx.fill(); }
  }
  path(lane()?.polygon, '#73dacb', true); path(lane()?.counting_line, '#f7c77f', false); path(points, '#fff', false);
  const line = lane()?.counting_line;
  if (line?.length === 2) {
    const [[ax, ay], [bx, by]] = line, dx = bx - ax, dy = by - ay, length = Math.hypot(dx, dy);
    const x = (ax + bx) * canvas.width / 2, y = (ay + by) * canvas.height / 2;
    const ex = x - dy / length * 45, ey = y + dx / length * 45;
    ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(ex, ey); ctx.strokeStyle = '#f7c77f'; ctx.stroke();
    ctx.fillStyle = '#f7c77f'; ctx.font = '18px system-ui'; ctx.fillText('Count →', ex + 5, ey);
  }
}
function apply(value) {
  if (!Array.isArray(value.lanes) || !value.lanes.length || value.lanes.some(item => !item.lane_id)) throw new Error('Expected camera configuration with lane IDs.');
  config = value; mode = null; points = []; $('lane').replaceChildren(...config.lanes.map(item => new Option(item.lane_id, item.lane_id))); sync();
}
$('image-file').onchange = async event => {
  const file = event.target.files[0]; if (!file) return;
  image = await createImageBitmap(file); canvas.width = image.width; canvas.height = image.height; draw();
};
$('config-file').onchange = async event => {
  try { apply(JSON.parse(await event.target.files[0].text())); feedback('Configuration loaded. Choose a lane.'); } catch (error) { feedback(error.message); }
};
$('apply').onclick = () => { try { apply(JSON.parse($('config-json').value)); feedback('JSON applied.'); } catch (error) { feedback(error.message); } };
$('lane').onchange = () => { mode = null; points = []; draw(); };
$('polygon').onclick = () => { if (!lane()) return feedback('Load a camera configuration first.'); mode = 'polygon'; points = []; feedback('Click at least three boundary points, then Finish polygon.'); draw(); };
$('finish').onclick = () => { if (mode !== 'polygon' || points.length < 3) return feedback('A polygon needs at least three points.'); lane().polygon = points; points = []; mode = null; sync(); };
$('line').onclick = () => { if (!lane()) return feedback('Load a camera configuration first.'); mode = 'line'; points = []; feedback('Click two endpoints. The arrow shows the counted direction.'); };
$('reverse').onclick = () => { lane()?.counting_line?.reverse(); sync(); };
canvas.onclick = event => {
  if (!mode) return;
  const rect = canvas.getBoundingClientRect(); points.push([(event.clientX - rect.left) / rect.width, (event.clientY - rect.top) / rect.height]);
  if (mode === 'line' && points.length === 2) { lane().counting_line = points; points = []; mode = null; sync(); } else draw();
};
$('download').onclick = () => {
  if (!config) return feedback('Load a camera configuration first.');
  const url = URL.createObjectURL(new Blob([JSON.stringify(config, null, 2)], {type: 'application/json'}));
  const link = document.createElement('a'); link.href = url; link.download = 'camera.json'; link.click(); URL.revokeObjectURL(url);
};
