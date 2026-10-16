const $ = id => document.getElementById(id);
async function api(path) {
  const response = await fetch('/casper-api' + path, {cache: 'no-store', signal: AbortSignal.timeout(5000)});
  const data = await response.json();
  if (!response.ok) throw new Error(data.message || 'Casper is unavailable.');
  return data;
}
function render(frame) {
  $('world').textContent = frame.world_id;
  const count = field => {
    const values = frame.lanes.map(lane => lane[field]);
    return values.every(value => value !== null) ? values.reduce((a, b) => a + b, 0) : '—';
  };
  $('visible').textContent = count('visible_vehicle_count');
  $('queued').textContent = count('queued_vehicle_count');
  $('coverage').textContent = frame.lanes.filter(lane => lane.measurement_status !== 'unavailable').length + ' / ' + frame.lanes.length;
  $('lanes').replaceChildren();
  for (const lane of frame.lanes) {
    const row = document.createElement('tr');
    const values = [lane.street_id, lane.intersection_id, lane.device_ids.join(', ') || 'Unmapped', lane.detector_id,
      lane.visible_vehicle_count, lane.queued_vehicle_count, lane.crossed_vehicle_count,
      lane.age_seconds === null ? null : lane.age_seconds.toFixed(1) + 's', lane.measurement_status];
    for (const value of values) {
      const cell = document.createElement('td'); cell.textContent = value ?? '—'; row.append(cell);
    }
    const label = document.createElement('small'); label.textContent = lane.lane_id; row.firstChild.append(label);
    row.lastChild.dataset.status = lane.measurement_status;
    $('lanes').append(row);
  }
  const demo = frame.lanes.some(lane => lane.measurement_method === 'synthetic_demo');
  $('note').textContent = (demo ? 'SYNTHETIC DEMO · ' : '') + 'Latest state · sequence ' + frame.sequence + ' · lane timestamps are independent. Unavailable measurements are not zero.';
}
async function poll() {
  try {
    const session = await api('/session');
    const frame = await api('/sessions/' + encodeURIComponent(session.id) + '/traffic');
    if (frame.session_id !== session.id || frame.revision !== session.revision) throw new Error('Configuration changed. Refreshing…');
    render(frame);
    $('connection').textContent = 'Casper · ' + frame.status;
    $('connection').dataset.status = frame.status; $('error').hidden = true;
  } catch (error) {
    $('connection').textContent = 'Casper unavailable'; $('connection').dataset.status = 'unavailable';
    $('error').textContent = error.message + ' Start the Casper service and check its connection.'; $('error').hidden = false;
    for (const id of ['visible', 'queued', 'coverage']) $(id).textContent = '—';
    $('lanes').replaceChildren(); $('note').textContent = 'No current measurements available.';
  } finally { setTimeout(poll, 2000); }
}
poll();
