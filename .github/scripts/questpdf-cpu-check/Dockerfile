# Harness for .github/scripts/questpdf-cpu-check.sh; same SDK/runtime refs as src/backend/Dockerfile.
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
ARG QUESTPDF_VERSION
WORKDIR /src
COPY QuestPdfCpuCheck.csproj Program.cs ./
RUN test -n "${QUESTPDF_VERSION}" \
    && dotnet publish QuestPdfCpuCheck.csproj -c Release -r linux-x64 --self-contained false -o /app -p:QuestPdfVersion=${QUESTPDF_VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS native
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "/app/QuestPdfCpuCheck.dll"]

# Older QEMU crashes when .NET reads /proc/self/maps; this static build also runs on 24.04.
FROM ubuntu:26.04@sha256:f144425ff09be612d6d9ad965196e9cdc23dae1f42110a8a11a3e9a8198759f7 AS qemu
RUN apt-get update \
    && apt-get install -y --no-install-recommends qemu-user \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS emulate
RUN apt-get update \
    && apt-get install -y --no-install-recommends gcc libc6-dev \
    && rm -rf /var/lib/apt/lists/*
COPY --from=qemu /usr/bin/qemu-x86_64 /usr/local/bin/qemu-x86_64
COPY avx2-probe.c /probe/avx2-probe.c
RUN gcc -O0 -mavx2 -mfma -mbmi2 -o /probe/avx2-probe /probe/avx2-probe.c
# .NET maps JIT memory twice by default, which QEMU user-mode does not handle
ENV DOTNET_EnableWriteXorExecute=0
WORKDIR /app
COPY --from=build /app .
