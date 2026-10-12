# Source-preparation utility, outside the C# simulator runtime.
# Reproduce mapped CSVs after running extract.py and obtaining author calibration.
# Dependency versions used are recorded in mapping-summary.json.
# Uses the authors' bounding-box point and camera model. Mapping thresholds and
# low-support fallback are our estimates; output is not ground-truth edge density.
import numpy as np, pandas as pd, pyarrow.parquet as pq, cameratransform as ct
from pyproj import Transformer
from xml.etree import ElementTree as ET
from collections import Counter
from pathlib import Path
import argparse
parser=argparse.ArgumentParser(description='Prepare camera-to-road preference estimates; never consumed by the simulator.')
parser.add_argument('raw_parquet');parser.add_argument('selected_ids');parser.add_argument('network');parser.add_argument('output_directory')
args=parser.parse_args()
out=Path(args.output_directory);out.mkdir(parents=True,exist_ok=True)
net=ET.parse(args.network).getroot()
netoffset=np.array([float(x) for x in net.find('location').get('netOffset').split(',')])
edges=[e for e in net.findall('edge') if e.get('function')!='internal']
ids={int(x) for x in open(args.selected_ids) if x.strip()}
p=pq.read_table(args.raw_parquet).to_pandas()
raw_start=int(p.kafka_timestamp.min())
p=p[p.tracker_id.isin(ids)].sort_values(['tracker_id','kafka_timestamp'])
counts=p.groupby('tracker_id').cumcount()
p=p[counts%25==0].copy()
cam=ct.Camera(ct.RectilinearProjection(image=(1280,720),focallength_px=710))
cam.load(str(out/'camera-calibration.json'))
gps=cam.gpsFromImage(np.array([(p.left+p.right)*.5,p.top*.1+p.bottom*.9]).T)
transform=Transformer.from_crs('EPSG:4326','EPSG:32635',always_xy=True)
x,y=transform.transform(gps[:,1],gps[:,0]);p['x']=x+netoffset[0];p['y']=y+netoffset[1]
prev=p.groupby('tracker_id')[['x','y']].shift(1)
nxt=p.groupby('tracker_id')[['x','y']].shift(-1)
directions=nxt.to_numpy()-prev.to_numpy()
motions=np.linalg.norm(directions,axis=1)
valid=np.isfinite(directions).all(axis=1)&np.isfinite(p[['x','y']]).all(axis=1)&(motions>=2)
p=p[valid].copy(); directions=directions[valid]/motions[valid,None]
start,end,segmentedge=[],[],[]
for edge in edges:
    for lane in edge.findall('lane'):
        allow=lane.get('allow');deny=lane.get('disallow','').split()
        if not((allow is None or 'passenger' in allow.split() or 'all' in allow.split()) and 'passenger' not in deny and 'all' not in deny):continue
        pts=[tuple(map(float,xy.split(',')[:2])) for xy in lane.get('shape','').split()]
        for a,b in zip(pts,pts[1:]):
            if np.linalg.norm(np.array(b)-a)>0.1:
                start.append(a);end.append(b);segmentedge.append(edge.get('id'))
start=np.array(start);end=np.array(end);vec=end-start
length=np.linalg.norm(vec,axis=1);unit=vec/length[:,None]
positions=p[['x','y']].to_numpy();trackids=p.tracker_id.to_numpy()
seen=set();first_matches={};residual=[];assigned=0
elapsed=(p.kafka_timestamp.to_numpy()-raw_start)/1000
for off in range(0,len(positions),500):
    points=positions[off:off+500]; dirs=directions[off:off+500]
    fractions=np.clip(np.einsum('ijk,jk->ij',points[:,None,:]-start[None,:,:],vec)/(length**2)[None,:],0,1)
    closest=start[None,:,:]+fractions[:,:,None]*vec[None,:,:]
    dist=np.linalg.norm(points[:,None,:]-closest,axis=2)
    compatible=dirs@unit.T>=np.cos(np.pi/4)
    dist[~compatible]=np.inf
    best=dist.argmin(axis=1);bestdist=dist[np.arange(len(points)),best]
    for i,(s,d) in enumerate(zip(best,bestdist)):
        if d<=12:
            key=(int(trackids[off+i]),segmentedge[s])
            seen.add(key);first_matches.setdefault(key,float(elapsed[off+i]));residual.append(float(d));assigned+=1
freq=Counter(edge for _,edge in seen)
rows=[]
for edge in edges:
    lane_count=len(edge.findall('lane'));supply=lane_count*max(1,int(edge.get('priority','0'))+1)
    rows.append({'edge':edge.get('id'),'name':edge.get('name',''),'mapped_complete_tracks':freq[edge.get('id')],'lane_priority_supply':supply})
summary=pd.DataFrame(rows).sort_values('edge')
print('sampled points',len(positions),'assigned',assigned,'mappedtracks',len({t for t,e in seen}),'supportededges',len(freq),'unique(track,edge)',len(seen))
print('distquantiles',np.quantile(residual,[.5,.9,.99]))
print(summary[summary.mapped_complete_tracks>0].sort_values('mapped_complete_tracks',ascending=False).to_string(index=False))

import csv,json,importlib.metadata
bins=list(csv.DictReader((out/'observed-track-starts.csv').open()))
reference=max(freq.values())
window=float(bins[-1]['end_elapsed_seconds'])
rows=[]
for edge in sorted(e.get('id') for e in edges):
    for slot in bins:
        a,b=float(slot['start_elapsed_seconds']),float(slot['end_elapsed_seconds'])
        count=sum(e==edge and a<=t<b for (track,e),t in first_matches.items())
        rows.append({'edge':edge,'start_elapsed_seconds':a,'end_elapsed_seconds':b,'mapped_track_first_matches':count,'relative_weight':count/(b-a)/(reference/window)})
with (out/'mapped-edge-track-starts.csv').open('w',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)
summary['spatial_basis']=np.where(summary.mapped_complete_tracks>=50,'camera_map_matching_estimate','lane_priority_estimate')
summary['outside_window_weight']=np.where(summary.mapped_complete_tracks>=50,summary.mapped_complete_tracks/reference,summary.lane_priority_supply/summary.lane_priority_supply.max())
summary.to_csv(out/'spatial-estimates.csv',index=False)
report={'format_version':1,'method':'camera projection of every 25th selected-track detection; nearest compatible passenger lane segment; unique track-edge first match','maximum_distance_m':12,'maximum_heading_difference_degrees':45,'minimum_heading_displacement_m':2,'minimum_supported_tracks':50,'normalization_reference_mapped_tracks':reference,'processing_window_seconds':window,'sampled_points_with_usable_heading':len(positions),'assigned_samples':assigned,'unique_mapped_tracks':len({t for t,e in seen}),'unique_track_edge_matches':len(seen),'edges_with_matches':len(freq),'edges_used_for_camera_spatial_estimate':int((summary.mapped_complete_tracks>=50).sum()),'edges_using_lane_priority_estimate':int((summary.mapped_complete_tracks<50).sum()),'distance_quantiles_m':{str(q):float(np.quantile(residual,q)) for q in [.5,.9,.99]},'dependencies':{n:importlib.metadata.version(n) for n in ['numpy','pandas','pyarrow','cameratransform','pyproj']},'notes':'Map matching is an inference from noisy camera detections and calibration, not measured SUMO edge density or ground-truth directional counts. Limited camera view; unmatched/low-support roads are not assumed empty.'}
(out/'mapping-summary.json').write_text(json.dumps(report,indent=2)+'\n')
