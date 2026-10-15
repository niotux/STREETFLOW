FROM python:3.12-slim
WORKDIR /trainer
RUN apt-get update && apt-get install -y --no-install-recommends libgomp1 libstdc++6 && rm -rf /var/lib/apt/lists/*
COPY components/route-trainer/requirements.txt ./
RUN pip install --no-cache-dir torch==2.14.1 --index-url https://download.pytorch.org/whl/cpu && pip install --no-cache-dir -r requirements.txt
COPY components/route-trainer/route_trainer/ route_trainer/
COPY components/route-trainer/web/ web/
COPY components/ui/web/ shared-ui/
ENV TRAINER_DATA_ROOT=/data STREETFLOW_UI_ROOT=/trainer/shared-ui
EXPOSE 8092
CMD ["python", "-m", "route_trainer", "serve", "--host", "0.0.0.0", "--port", "8092"]
