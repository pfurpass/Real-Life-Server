# Real-Life-Server

Professioneller IRL-Livestream-Vermittlungsserver: nimmt RTMP/SRT/WHIP von einem mobilen
Encoder (Belabox, Moblin, OBS, …) entgegen, überwacht die Verbindung, und stellt einen
ausfallsicheren Ausgangsstream zu Twitch/YouTube bereit, der bei Verbindungsabbrüchen
automatisch auf BRB-/Reconnecting-Szenen umschaltet statt den Zuschauern die Verbindung zu
kappen.

**Das vollständige Architektur- und Betriebskonzept steht in [`docs/CONCEPT.md`](docs/CONCEPT.md)**
— Datenfluss, Klassenstruktur, Datenbankschema, Streaming-Pipeline, Zustandsmaschine,
Sicherheit, Skalierung, Performance und Erweiterungsmöglichkeiten (Cloudflare, Multi-Region,
Recording, Replay, Clip-System).

## Projektstruktur

```
backend/    C# / ASP.NET Core, Clean Architecture (Domain, Application, Infrastructure, Api)
frontend/   React / TypeScript / Tailwind (Vite)
deploy/     nginx, MediaMTX config, Slate-Video-Platzhalter
docs/       CONCEPT.md
```

## Schnellstart (Docker)

```bash
cp .env.example .env
# .env ausfüllen: Secrets generieren, siehe Kommentare in der Datei
#   openssl rand -base64 48   -> JWT_SECRET
#   openssl rand -base64 32   -> KEY_PROTECTION_MASTER_KEY
#   openssl rand -hex 32      -> WEBHOOK_SECRET

# Platzhalter-Szenenvideos erzeugen (benötigt lokal installiertes ffmpeg)
./deploy/assets/scenes/generate-placeholders.sh

# Selbstsigniertes Zertifikat für lokale Tests (Produktion: echtes Zertifikat, z. B. certbot)
mkdir -p deploy/nginx/certs
openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
  -keyout deploy/nginx/certs/privkey.pem -out deploy/nginx/certs/fullchain.pem \
  -subj "/CN=localhost"

docker compose up -d --build
docker compose run --rm api-migrate   # einmalig: Datenbankschema anlegen
```

Danach:
- Frontend: `https://localhost/`
- RTMP-Ingest: `rtmp://localhost:1935/live/{streamKey}`
- SRT-Ingest: `srt://localhost:8890?streamid=publish:{streamKey}`
- WHIP-Ingest: `http://localhost:8889/live/{streamKey}/whip`

Ersten Admin-Nutzer anlegen (kein Nutzer existiert nach der Migration):

```bash
docker compose exec postgres psql -U reallifeserver -d reallifeserver -c \
  "INSERT INTO users (id, username, email, password_hash, role, is_active, created_at) \
   VALUES (gen_random_uuid(), 'admin', 'admin@example.com', '<bcrypt-hash>', 0, true, now());"
```

(`<bcrypt-hash>` z. B. per `docker compose exec api dotnet-script` oder einem kleinen
BCrypt-CLI-Snippet erzeugen — die `RegisterUserCommand`-Logik unter
`backend/src/RealLifeServer.Application/Auth/Commands/RegisterUserCommand.cs` zeigt exakt, wie
der Hash gebildet wird. Ein `POST /api/auth/register`-Bootstrap-Endpoint ohne Auth-Zwang für den
allerersten Nutzer ist ein naheliegender Erweiterungspunkt, siehe `docs/CONCEPT.md` Kapitel 13.)

## Lokale Entwicklung ohne Docker

**Backend** (benötigt .NET 8 SDK, lokal laufendes PostgreSQL/Redis/MediaMTX):

```bash
cd backend
dotnet restore
dotnet run --project src/RealLifeServer.Api
```

**Frontend**:

```bash
cd frontend
npm install
npm run dev
```

## Tests

```bash
cd backend
dotnet test tests/RealLifeServer.Application.Tests
```

Die Tests decken die Zustandsmaschine (`SceneStateMachine`) vollständig ab — sie ist bewusst
seiteneffektfrei gehalten, damit sie ohne Datenbank/FFmpeg/Mocks testbar ist. Siehe
`docs/CONCEPT.md` Kapitel 5.4.

> **Hinweis zu diesem Repository-Stand:** Der Code wurde in dieser Session ohne lokal
> installiertes .NET-SDK erstellt; die C#-Tests und der Backend-Build konnten daher nicht
> tatsächlich ausgeführt werden (das Frontend wurde gebaut und typgeprüft, das gelingt
> fehlerfrei). Vor einem produktiven Einsatz unbedingt `dotnet build` und `dotnet test` lokal
> laufen lassen.

## Wichtige Designentscheidungen (Kurzfassung, Details in docs/CONCEPT.md)

- **MediaMTX** übernimmt RTMP/SRT/WHIP-Ingest und HLS-Ausgabe; RealLifeServer ist die
  Control-Plane (Kapitel 2).
- Der Compositor (FFmpeg pro Kanal) hält die ausgehende Verbindung zu Twitch/YouTube
  **permanent** offen und schaltet nur die sichtbare Ebene per ZeroMQ-Filterkommandos um
  (Kapitel 4) — deshalb verliert der Zuschauer nie die Verbindung.
  Erfordert einen FFmpeg-Build mit `--enable-libzmq`; ohne das läuft automatisch die
  `RestartableFallback`-Strategie (Kapitel 4.2).
- Der Compositor liest das Live-Signal intern per **RTSP** (nicht RTMP) von MediaMTX, weil
  RTSP-Reader auf einen Publisher warten können, statt sofort abzubrechen (Kapitel 4.1).
