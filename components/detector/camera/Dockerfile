FROM python:3.12-slim
WORKDIR /camera
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt
COPY worker.py detector.py metrics.py preview.py ./
USER 65534:65534
ENV PYTHONDONTWRITEBYTECODE=1
ENTRYPOINT ["python", "/camera/worker.py"]
