# Casper camera data service

Raspberry Pi workers own video capture, inference, local tracking, camera polygons, counting lines, and image-motion queue estimates. They send UTC-window measurements to Casper. Casper owns camera-to-lane, street, intersection, and stoplight mappings and serves the latest state through [TACO](../contracts/taco.md).

Melchior connects to Casper or Balthasar using the same discovery/map/device/traffic routes. TACO is a provider interface, not another server. Casper supplies measurements with unknown lamp states and no signal controller adapter.

CameraObservation uses UTC windows; TrafficObservation retains numeric simulation windows. The common TacoTraffic response identifies its time basis and provides lane-specific timestamps, freshness, and measurement method. Camera queue estimates use image displacement/dwell, not calibrated physical speed. Balthasar's city TACO adapter supplies complete lane snapshot counts when available; queue/crossing/speed remain null because that snapshot has no camera calibration. These methods must not be confused with equivalent detection accuracy.

Each lane has one authoritative camera to avoid blindly summing overlapping feeds. Unavailable and stale measurements are null; measured empty regions are zero. Current state is bounded and not persisted; a Casper restart creates a new consumer session.

The initial worker uses CPU OpenCV DNN with YOLOX Nano, a simple IoU tracker, and metadata-only delivery. It supports USB, RTSP, and recorded video. Physical speed and cross-camera identity are not inferred. Traffic decisions and physical light safety remain outside Casper.

See the [implementation, calibration, demo, API, and testing guide](../../../components/detector/README.md).
