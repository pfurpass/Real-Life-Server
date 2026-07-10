# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY RealLifeServer.sln .
COPY src/RealLifeServer.Domain/RealLifeServer.Domain.csproj src/RealLifeServer.Domain/
COPY src/RealLifeServer.Application/RealLifeServer.Application.csproj src/RealLifeServer.Application/
COPY src/RealLifeServer.Infrastructure/RealLifeServer.Infrastructure.csproj src/RealLifeServer.Infrastructure/
COPY src/RealLifeServer.Api/RealLifeServer.Api.csproj src/RealLifeServer.Api/
COPY tests/RealLifeServer.Application.Tests/RealLifeServer.Application.Tests.csproj tests/RealLifeServer.Application.Tests/
RUN dotnet restore src/RealLifeServer.Api/RealLifeServer.Api.csproj

COPY . .
RUN dotnet publish src/RealLifeServer.Api/RealLifeServer.Api.csproj -c Release -o /app --no-restore

# FFmpeg is invoked as a child process at runtime (docs/CONCEPT.md, chapter 4). The Zmq
# compositor strategy requires an FFmpeg build with --enable-libzmq; Debian's apt package does
# not always ship it. Verify with `ffmpeg -filters | grep zmq` after building this image - if
# it's missing, either build a custom FFmpeg with libzmq, or set every channel's
# CompositorStrategy to RestartableFallback (docs/CONCEPT.md, chapter 4.2), which needs no
# special FFmpeg build.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

EXPOSE 8080
ENTRYPOINT ["dotnet", "RealLifeServer.Api.dll"]
