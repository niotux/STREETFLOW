"""Optional low-rate, bounded JPEG preview for the local Casper demo."""
import cv2

from metrics import inside, point


def annotated_jpeg(image, boxes, config, status, counts):
    height, width = image.shape[:2]
    scale = min(1.0, 960 / max(width, height))
    frame = cv2.resize(image, (round(width * scale), round(height * scale)))
    height, width = frame.shape[:2]
    colors = [(60, 220, 180), (255, 180, 70), (180, 120, 255), (100, 200, 255)]
    for index, lane in enumerate(config["lanes"]):
        color = colors[index % len(colors)]
        polygon = lane["polygon"]
        for start, end in zip(polygon, polygon[1:] + polygon[:1]):
            cv2.line(frame, pixel(start, width, height), pixel(end, width, height), color, 2)
        line = lane.get("counting_line")
        if line:
            a, b = pixel(line[0], width, height), pixel(line[1], width, height)
            cv2.arrowedLine(frame, a, b, color, 2, tipLength=0.12)
        label = counts.get(lane["lane_id"], {})
        message = (f'{lane["lane_id"]} visible={label.get("visible_vehicle_count", "-")} '
                   f'crossed={label.get("crossed_vehicle_count", "-")} '
                   f'queued={label.get("queued_vehicle_count", "-")}')
        cv2.putText(frame, message, (12, 28 + 24 * index), cv2.FONT_HERSHEY_SIMPLEX, 0.55, color, 2)
    for box in boxes:
        left, top = pixel(box[:2], width, height)
        right, bottom = pixel(box[2:], width, height)
        assigned = [lane["lane_id"] for lane in config["lanes"] if inside(point(box), lane["polygon"])]
        cv2.rectangle(frame, (left, top), (right, bottom), (255, 255, 255), 2)
        cv2.putText(frame, ",".join(assigned) or "unassigned", (left, max(15, top - 6)),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 2)
    cv2.putText(frame, status.upper(), (12, height - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.65,
                (80, 180, 255) if status != "ok" else (60, 220, 180), 2)
    for quality in (75, 60, 45):
        ok, encoded = cv2.imencode(".jpg", frame, [cv2.IMWRITE_JPEG_QUALITY, quality])
        if ok and len(encoded) <= 256 * 1024:
            return encoded.tobytes()
    return None


def pixel(point, width, height):
    return round(point[0] * width), round(point[1] * height)
