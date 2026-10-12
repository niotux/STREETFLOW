FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src
COPY global.json Directory.Build.props Streetflow.slnx ./
COPY components/ components/
RUN dotnet restore Streetflow.slnx --locked-mode
RUN dotnet publish components/simulator/Streetflow.Simulator -c Release --no-restore -o /out

FROM build AS test
USER root
COPY docker/install-sumo.sh /tmp/install-sumo.sh
RUN sh /tmp/install-sumo.sh
COPY scenarios/ scenarios/
COPY experiments/ experiments/
ENV Simulator__SumoBinary=sumo
ENTRYPOINT ["dotnet", "test", "Streetflow.slnx", "--no-restore", "--logger", "console;verbosity=normal"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble AS runtime
USER root
COPY docker/install-sumo.sh /tmp/install-sumo.sh
RUN sh /tmp/install-sumo.sh
WORKDIR /app
COPY --from=build /out/ ./
COPY scenarios/ scenarios/
RUN mkdir -p /app/data && chown -R app:app /app/data
COPY experiments/drafts/ projects/
USER app
ENV ASPNETCORE_URLS=http://+:8080 Simulator__ScenarioRoot=/app/scenarios Simulator__DataRoot=/app/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Streetflow.Simulator.dll"]
