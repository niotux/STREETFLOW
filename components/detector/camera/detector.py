"""CPU YOLOX ONNX adapter. Supports the official raw or decoded model exports."""
import cv2
import numpy as np


class Detector:
    def __init__(self, model, size=416, threshold=0.35, decoded=False, threads=2):
        cv2.setNumThreads(threads)
        self.net = cv2.dnn.readNetFromONNX(model)
        self.size, self.threshold, self.decoded = size, threshold, decoded

    def detect(self, image):
        height, width = image.shape[:2]
        ratio = min(self.size / height, self.size / width)
        padded = np.full((self.size, self.size, 3), 114, dtype=np.uint8)
        resized = cv2.resize(image, (int(width * ratio), int(height * ratio)))
        padded[:resized.shape[0], :resized.shape[1]] = resized
        # Official YOLOX preprocessing: BGR, top-left padding, no /255 normalization.
        self.net.setInput(cv2.dnn.blobFromImage(padded, scalefactor=1.0, swapRB=False))
        predictions = self.net.forward()[0].copy()
        if predictions.ndim != 2 or predictions.shape[1] != 85:
            raise ValueError("Expected COCO YOLOX output [1,N,85]")
        if not self.decoded:
            grids, strides = [], []
            for stride in (8, 16, 32):
                y, x = np.meshgrid(np.arange(self.size // stride), np.arange(self.size // stride), indexing="ij")
                grids.append(np.stack((x, y), axis=-1).reshape(-1, 2))
                strides.append(np.full((x.size, 1), stride))
            grid, stride = np.concatenate(grids), np.concatenate(strides)
            if len(predictions) != len(grid):
                raise ValueError("Model input size does not match --size")
            predictions[:, :2] = (predictions[:, :2] + grid) * stride
            predictions[:, 2:4] = np.exp(predictions[:, 2:4]) * stride
        classes = predictions[:, 5:].argmax(axis=1)
        scores = predictions[:, 4] * predictions[np.arange(len(predictions)), classes + 5]
        # COCO: car, motorcycle, bus, truck. Class-agnostic NMS prevents duplicate counts.
        keep = np.isin(classes, [2, 3, 5, 7]) & (scores >= self.threshold)
        boxes, scores = predictions[keep, :4] / ratio, scores[keep]
        if not len(boxes):
            return []
        boxes[:, :2] -= boxes[:, 2:4] / 2
        indices = cv2.dnn.NMSBoxes(boxes.tolist(), scores.tolist(), self.threshold, 0.45)
        result = []
        for index in np.asarray(indices).reshape(-1):
            x, y, w, h = boxes[index]
            result.append((max(0.0, float(x / width)), max(0.0, float(y / height)),
                           min(1.0, float((x + w) / width)), min(1.0, float((y + h) / height))))
        return result
