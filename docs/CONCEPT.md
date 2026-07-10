# Real-Life-Server — Konzept eines professionellen IRL-Livestream-Vermittlungsservers

> Vollständiges Architektur- und Betriebskonzept. Der zugehörige Quellcode befindet sich in
> `backend/` (C# / ASP.NET Core, Clean Architecture) und `frontend/` (React / TypeScript / Tailwind).

## Inhaltsverzeichnis

1. [Ziele & Designprinzipien](#1-ziele--designprinzipien)
2. [Gesamtarchitektur](#2-gesamtarchitektur)
3. [Datenfluss vom Encoder bis Twitch](#3-datenfluss-vom-encoder-bis-twitch)
4. [Die Streaming-Pipeline im Detail](#4-die-streaming-pipeline-im-detail)
5. [Die Zustandsmaschine für den Szenenwechsel](#5-die-zustandsmaschine-für-den-szenenwechsel)
6. [Klassenstruktur (Clean Architecture)](#6-klassenstruktur-clean-architecture)
7. [Datenbankschema](#7-datenbankschema)
8. [Echtzeitkommunikation (WebSocket / SignalR)](#8-echtzeitkommunikation-websocket--signalr)
9. [REST-API](#9-rest-api)
10. [Sicherheit](#10-sicherheit)
11. [Skalierung & Betrieb](#11-skalierung--betrieb)
12. [Performance-Optimierungen](#12-performance-optimierungen)
13. [Erweiterungsmöglichkeiten](#13-erweiterungsmöglichkeiten)
14. [Verzeichnisstruktur des Repos](#14-verzeichnisstruktur-des-repos)
15. [Betriebs-Runbook](#15-betriebs-runbook)

---

## 1. Ziele & Designprinzipien

Der Real-Life-Server (RLS) ist eine **Vermittlungsschicht** zwischen einem mobilen Encoder
(Belabox, Moblin, OBS, jeder RTMP/SRT/WHIP-fähige Client) und einer oder mehreren
Streaming-Plattformen (Twitch, YouTube, eigenes RTMP-Ziel). Das zentrale Versprechen:

> **Der Zuschauer auf Twitch/YouTube verliert den Stream nie**, selbst wenn die Mobilfunk-
> verbindung des Streamers abbricht, die Bitrate einbricht oder der Encoder crasht.

Erreicht wird das, indem der Server **nicht** das eingehende Signal 1:1 durchreicht, sondern
permanent einen eigenen ausgehenden Stream zur Zielplattform hält (den *Compositor-Stream*)
und dessen Bildquelle abhängig vom Zustand der Encoder-Verbindung softwareseitig umschaltet
(Live-Bild, "Be Right Back", "Verbindung wird wiederhergestellt", "Offline").

Designprinzipien:

- **Clean Architecture**: Domain- und Application-Schicht kennen keine Infrastruktur-Details
  (FFmpeg, PostgreSQL, MediaMTX). Das macht Kernlogik (Zustandsmaschine, Regeln) isoliert testbar.
- **Ausfallsicherheit vor Feature-Reichtum**: Der Ausgangsstream zur Plattform darf nie aktiv
  beendet werden, außer der Nutzer beendet den Kanal explizit.
- **Bewährte Bausteine statt Neuerfindung**: RTMP/SRT/WHIP-Ingest, HLS-Repackaging und
  Low-Latency-Wiedergabe werden von **MediaMTX** (Open Source, MIT-Lizenz) übernommen — ein
  eigener RTMP/SRT-Stack in C# neu zu schreiben wäre unwirtschaftlich und fehleranfälliger als
  ein etabliertes, in Go geschriebenes Media-Gateway zu orchestrieren. RLS ist die **Control- &
  Decision-Plane**, MediaMTX + FFmpeg sind die **Media-Plane**.
- **Asynchron & nebenläufig**: Sämtliche Encoder-Überwachung läuft in Hintergrunddiensten
  (`IHostedService`), niemals blockierend im Request-Pfad.
- **Beobachtbarkeit zuerst**: Jeder Zustandswechsel ist ein Domain-Event, wird geloggt, per
  WebSocket verteilt, persistiert und optional an Discord gemeldet.

---

## 2. Gesamtarchitektur

```
                                   ┌───────────────────────────────────────────────────────┐
                                   │                      Real-Life-Server                   │
                                   │                                                         │
 ┌───────────────┐   RTMP/SRT/WHIP │  ┌───────────┐        ┌──────────────────────────┐     │      RTMP
 │ Mobiler Encoder │───────────────┼─▶│ MediaMTX  │──HLS──▶│  (Vorschau im Frontend)   │     │   ┌─────────┐
 │ (Belabox,       │                 │  Ingest   │        └──────────────────────────┘     │──▶│  Twitch  │
 │  Moblin, OBS…)  │                 │  Gateway  │                                          │   └─────────┘
 └───────────────┘                  └─────┬─────┘                                          │
        ▲   heartbeat (REST, optional)     │ runOnReady / runOnNotReady Webhooks            │   ┌─────────┐
        │                                  ▼                                                │──▶│ YouTube  │
        │                          ┌──────────────────┐        ┌────────────────────┐       │   └─────────┘
        │                          │  ASP.NET Core API │◀─────▶│  StreamOrchestrator │       │
        │       WebSocket (Status) │  + SignalR Hub     │        │  (Hosted Service)   │       │
        └──────────────────────────│  + REST Controller │        │  Scene State Machine│       │
                                    └─────────┬─────────┘        └──────────┬──────────┘       │
                                              │                             │ steuert           │
                                              │                             ▼                   │
                                    ┌─────────▼─────────┐         ┌───────────────────┐         │
                                    │  PostgreSQL         │         │  FFmpeg Compositor │─────────┘
                                    │  (Kanäle, Nutzer,   │         │  Prozess pro Kanal │
                                    │   Metriken, Logs)   │         │  (ZeroMQ-gesteuert) │
                                    └─────────┬─────────┘         └───────────────────┘
                                              │
                                    ┌─────────▼─────────┐
                                    │  Redis              │  Verteilte Locks, Kanal-Zuteilung
                                    │  (Cache/PubSub)     │  bei horizontaler Skalierung,
                                    └───────────────────┘  SignalR-Backplane

                          ┌─────────────────────┐
                          │  React/TS Frontend    │  Dashboard, Live-Vorschau, Szenensteuerung,
                          │  (Tailwind, Vite)     │  Bitrate-Charts, Logs, Stream-Key-Verwaltung
                          └─────────────────────┘

                                    Reverse Proxy: Nginx (TLS-Terminierung, WebSocket-Upgrade,
                                    Routing zu API / Frontend / HLS)
```

**Warum MediaMTX als Ingest-Gateway?**

| Anforderung | Eigenbau in C# | MediaMTX (orchestriert von RLS) |
|---|---|---|
| RTMP/SRT/WHIP-Server | Wochen an Protokollarbeit, hohes Fehlerrisiko | vorhanden, produktionserprobt |
| HLS-Repackaging für Vorschau | zusätzlicher Muxer nötig | eingebaut |
| Auth-Hooks (`runOnPublish` etc.) | müsste selbst gebaut werden | eingebaut, HTTP-Callback |
| Fokus des RLS-Teams | Protokoll-Debugging | Kernlogik: Zuverlässigkeit, UX, Automatisierung |

RLS bindet MediaMTX per Konfigurationsdatei (`deploy/mediamtx/mediamtx.yml`) und REST-Webhooks
ein. Das ist austauschbar: `IIngestGateway`/`IStreamStatsProvider` sind Interfaces in der
Application-Schicht — ein SRS- oder Eigenbau-Adapter ließe sich ohne Änderung an der
Kernlogik ergänzen.

---

## 3. Datenfluss vom Encoder bis Twitch

1. **Verbindungsaufbau**: Der Encoder verbindet sich zu `rtmp://server/live/{streamKey}` (oder
   `srt://server:8890?streamid=publish:{streamKey}`, oder WHIP `POST /whip/{streamKey}`).
2. **Authentifizierung**: MediaMTX ist mit `authMethod: http` konfiguriert und ruft **vor jeder
   Publish- oder Read-Aktion synchron** `POST /api/webhooks/mediamtx/auth` auf RLS auf
   (`authHTTPAddress`). Für `action=publish` validiert RLS den aus dem Pfadnamen extrahierten
   Stream-Key gegen die `channels`-Tabelle und prüft, ob der Kanal aktiv ist; `200 OK` erlaubt,
   jeder andere Status lehnt ab und MediaMTX trennt die Verbindung sofort. Für `action=read`
   (HLS-Vorschau, aber auch der interne RTSP-Pull des Compositors, Kapitel 4.1) antwortet RLS
   immer mit `200 OK` — Lesezugriffe werden nicht gegen Stream-Keys geprüft, sondern liegen
   ausschließlich im internen Docker-Netz. Bei erfolgreicher Publish-Authentifizierung trägt RLS
   das als `EncoderConnected`-Trigger in die Zustandsmaschine ein (`Offline → Connecting`).
3. **Ready-Signal**: `runOnReady`/`runOnNotReady` sind reine (asynchrone) Benachrichtigungs-Hooks
   ohne Gate-Funktion. Sobald der erste Videoframe lesbar ist, ruft MediaMTX `runOnReady` einen
   `curl`-Aufruf gegen `POST /api/webhooks/mediamtx/ready` auf. RLS trägt das als
   `FirstKeyframeReceived`-Trigger ein (`Connecting → Live`).
4. **Compositor startet/übernimmt**: Der `StreamOrchestrator` sorgt dafür, dass für diesen Kanal
   ein `SceneEncoderProcess` (FFmpeg) läuft. Dieser Prozess:
   - liest primär von `rtmp://127.0.0.1:1935/live/{streamKey}` (dem MediaMTX-Ingest-Pfad),
   - liest **parallel** von lokalen Loop-Videos (`brb.mp4`, `reconnecting.mp4`, `offline.mp4`),
   - komponiert per `filter_complex`/`overlay` alle Quellen auf eine Ausgabe-Leinwand,
   - schaltet per **ZeroMQ-Filterkommandos** (FFmpeg `zmq`-Filter) in Echtzeit um, welche
     Ebene sichtbar ist — **ohne den Encoder-Prozess neu zu starten**.
   - sendet die Ausgabe per `tee`-Muxer gleichzeitig an alle konfigurierten
     `StreamDestination`s (z. B. Twitch **und** YouTube).
5. **Laufender Betrieb**: `SceneMonitorHostedService` pollt alle ~2 s Metriken (Bitrate,
   Paketverlust, letzter Heartbeat) über `IStreamStatsProvider` (MediaMTX-API) und speist sie in
   die `SceneStateMachine`. Über- bzw. Unterschreitungen von Schwellwerten lösen
   Zustandsübergänge aus (siehe Kapitel 5).
6. **Verbindungsabbruch**: Encoder-Verbindung bricht ab → MediaMTX ruft `runOnNotReady` /
   `runOnUnpublish` auf. Die Zustandsmaschine wechselt (ggf. über einen kurzen
   "Reconnecting"-Timeout) nach `BRB`. Der Compositor-Prozess läuft **unverändert weiter** und
   sendet weiterhin Daten an Twitch — nur die sichtbare Ebene ändert sich von "Live" auf
   "BRB"/"Reconnecting".
7. **Wiederverbindung**: Encoder verbindet erneut → `runOnReady` feuert erneut → Zustandsmaschine
   wechselt zurück nach `Live`, Compositor blendet die Live-Ebene wieder ein.
8. **Zuschauer-Perspektive**: Auf Twitch bleibt die RTMP-Session die **ganze Zeit** verbunden.
   Es gibt keinen "Stream Offline"-Zustand auf der Plattform — der Zuschauer sieht durchgehend
   Bild (Live oder Szenenbild), nie einen Verbindungsabbruch.

---

## 4. Die Streaming-Pipeline im Detail

### 4.1 Warum "Compositing" statt "Quellenwechsel per Neustart" — und die reale Grenze davon

Ein naiver Ansatz würde bei Verbindungsverlust den FFmpeg-Prozess killen und mit einem neuen
Input (Slate-Video) neu starten. Das funktioniert, verursacht aber jedes Mal eine neue
RTMP-Verbindung zur Zielplattform (1-5 s Unterbrechung, Twitch zeigt kurz "reconnecting").

> **Korrektur (Produktionsfund):** Eine frühere Fassung dieses Kapitels ging davon aus, dass ein
> MediaMTX-RTSP-Reader ohne aktiven Publisher unbegrenzt wartet, statt die Verbindung zu
> terminieren. Das ist für MediaMTX v1.19.2 **falsch** — ein Reader auf einem Pfad ohne
> Publisher bekommt sofort `no stream is available on path` und FFmpeg beendet sich mit
> Exitcode 1. Da der Compositor diesen RTSP-Input *immer* zu öffnen versuchte — auch während
> BRB/Reconnecting/Offline, also praktisch die meiste Zeit im Leben eines Kanals — führte das zu
> einer Absturzschleife alle ~2 Sekunden. Das folgende Kapitel beschreibt die korrigierte
> Architektur.

RLS verwendet einen **Compositor-Prozess pro Kanal**, der mehrere Eingänge gleichzeitig offen
hält und die *sichtbare Ebene* per zmq umschaltet — aber in einem von **zwei sich gegenseitig
ausschließenden Modi**, je nachdem ob gerade ein Live-Signal erwartet wird:

```
NoLiveInput-Modus (Offline, Connecting, Reconnecting, Brb — der Normalfall)
  Input 0: -f lavfi color=c=black:s=WxH   (trivialer Filler, öffnet nie fehlschlagend)
  Input 1: loop von brb.mp4
  Input 2: loop von reconnecting.mp4 + drawtext-Overlay (Countdown)
  Input 3: loop von offline.mp4
  Input 4: -f lavfi anullsrc              (Stille, da der Filler keine eigene Audiospur hat)

LiveInput-Modus (Live, Degraded — erst NACH bestätigtem FirstKeyframeReceived betreten)
  Input 0: rtsp://mediamtx:8554/live/{key}   (echtes Live-Signal)
  Input 1: loop von brb.mp4
  Input 2: loop von reconnecting.mp4 + drawtext-Overlay (Countdown)
  Input 3: loop von offline.mp4

filter_complex (identisch in beiden Modi, nur Input 0 unterscheidet sich):
  [0:v] scale, pad, setpts                  -> base   (live ODER Filler, IMMER sichtbar als unterste Ebene)
  [1:v] scale, setpts                       -> brb
  [2:v] scale, setpts, drawtext@txtCountdown -> reconnecting
  [3:v] scale, setpts                       -> offline
  [base][brb]         overlay@ovBrb=enable=0/1        -> ov1
  [ov1][reconnecting]  overlay@ovReconnect=enable=0/1  -> ov2
  [ov2][offline]       overlay@ovOffline=enable=0/1    -> ov3
  [ov3] zmq=bind_address=tcp\://127.0.0.1\:<port pro Kanal> -> Ausgabe
```

`base` ist absichtlich **keine** eigene per-zmq schaltbare Ebene — sie ist immer die unterste
Schicht und damit implizit sichtbar, sobald keine der drei Overlay-Ebenen aktiviert ist. Das war
in der vorherigen Fassung anders (und fehlerhaft): dort gab es einen Toggle-Namen `ovLive`, der
tatsächlich die BRB-Ebene steuerte, während der const-Name `ovBrb` nirgends im Filtergraphen real
existierte — Kommandos an `ovBrb` liefen daher immer in einen zmq-Timeout, unabhängig vom hier
behobenen Absturzproblem.

**Wechsel *innerhalb* eines Modus** (z. B. Reconnecting → Brb, oder Live → Degraded) ist ein
reines zmq-Kommando — kein Prozess-Neustart, keine Unterbrechung der ausgehenden RTMP-Verbindung.

**Wechsel *zwischen* den beiden Modi** (Encoder verbindet zum ersten Mal / verliert die
Verbindung) erfordert dagegen einen vollständigen Prozess-Neustart: FFmpeg kann seinen
Filtergraphen nach dem Start nicht mehr strukturell verändern — ein Input lässt sich per zmq
weder hinzufügen noch entfernen. Das ist eine reale technische Grenze von FFmpeg, kein
Implementierungsdetail, das sich "besser lösen" ließe, solange man bei FFmpeg als Compositing-
Engine bleibt (siehe Kapitel 4.1.1 für Alternativen). Der ausgehende Twitch/YouTube-Stream
reconnectet daher an genau zwei Momenten im Leben eines Kanals: sobald der mobile Encoder zum
ersten Mal Bild liefert (`FirstKeyframeReceived`) und sobald er die Verbindung verliert
(`EncoderDisconnected`) — begrenzt, vorhersagbar, und (siehe Kapitel 4.1.2)
Backoff-limitiert, statt der vorherigen Absturzschleife alle 2 Sekunden.

`ZmqSceneEncoder` orchestriert das: `ApplySceneAsync(state)` bestimmt aus dem Zielzustand, ob
der aktuell laufende Prozess-Modus noch passt; falls nicht, restart (mit Backoff, s. u.); danach
werden die drei Overlay-Namen sowie ggf. der Countdown-Text per zmq gesetzt.

#### 4.1.1 Warum kein "permanenter interner Filler-Publisher" (bewusste Entscheidung)

Eine robustere, aber deutlich aufwändigere Alternative wäre ein permanent laufender interner
"Filler/Relay"-Prozess, der *immer* auf einen internen MediaMTX-Pfad publiziert (echtes
Live-Signal, wenn vorhanden, sonst ein lokales Loop-Video) — der eigentliche Compositor würde
dann von diesem *immer aktiven* internen Pfad lesen und müsste selbst nie neu starten. Das
verschiebt das Neustart-Problem aber nur eine Ebene tiefer: Auch der Filler-Prozess kann seinen
eigenen Input nicht ohne Neustart wechseln, sein Neustart reißt beim lesenden Compositor
denselben "kein Publisher"-Fehler auf, den dieses Kapitel gerade behebt — nur einmal mehr
indirekt. Eine echte, neustartfreie Quellenumschaltung gibt es in der Praxis nur mit
Media-Frameworks, die dynamische Pipeline-Rekonfiguration unterstützen (z. B. GStreamers
`input-selector`-Element) — das wäre ein Wechsel der Compositing-Engine, kein Bugfix, und bewusst
außerhalb des Umfangs dieser Änderung. Der zweistufige, Backoff-limitierte Neustart-Ansatz oben
ist der pragmatische, mit FFmpeg tatsächlich umsetzbare Kompromiss.

#### 4.1.2 Restart-Backoff

Jeder Moduswechsel-Neustart (und jeder fehlgeschlagene Erststart) zählt gegen einen
Backoff-Zähler pro Kanal (`RestartBackoff`, `StreamOrchestrator`/`ZmqSceneEncoder`):
2 s, 4 s, 8 s, 16 s, gedeckelt bei 30 s. Ein erfolgreicher Start setzt den Zähler zurück. Damit
degradiert eine flackernde Mobilfunkverbindung kontrolliert (zunehmender Abstand zwischen
Versuchen) statt den Compositor und die ausgehende Verbindung im Sekundentakt zu belasten.

**Warum der Compositor intern per RTSP statt RTMP von MediaMTX liest**: Ein RTMP-Client-Input in
FFmpeg (`-i rtmp://…`) bricht ebenfalls mit einem Fehler ab, sobald der Pfad keinen aktiven
Publisher mehr hat. RTSP verhält sich hier nicht grundsätzlich anders (siehe Korrekturhinweis
oben) — der Compositor öffnet die RTSP-Quelle daher ausschließlich im LiveInput-Modus, also erst
nachdem `FirstKeyframeReceived` bestätigt hat, dass tatsächlich ein Publisher aktiv ist. Der
Compositor nutzt intern weiterhin RTSP (`-rtsp_transport tcp`), weil MediaMTX dafür robustere
Reconnect-Optionen und geringeren Overhead als RTMP bietet — das öffentlich vom Encoder genutzte
RTMP/SRT/WHIP bleibt davon unberührt, da es nur MediaMTX selbst betrifft.

`ZmqFilterController` (Infrastructure) sendet bei jedem Szenenwechsel Kommandos wie:

```
ovBrb enable 1
ovReconnect enable 0
txtCountdown reinit text='Verbindung wird wiederhergestellt… 12s'
```

Innerhalb eines Modus bleibt damit die ausgehende RTMP/RTMPS-Verbindung zu Twitch/YouTube
durchgehend bestehen — der Wechsel passiert nur im Bildinhalt. Zwischen den Modi (s. o.) reconnectet
sie kontrolliert, statt gar nicht mehr zu funktionieren.

### 4.2 Fallback-Modus (Kompatibilität)

Für Umgebungen ohne `libzmq`-Unterstützung im FFmpeg-Build implementiert
`RestartableFallbackSceneEncoder` eine einfachere Variante: der Prozess wird bei **jedem**
Szenenwechsel komplett neu gestartet, mit genau einem passenden Input (Live-RTSP für
Live/Degraded, sonst das jeweilige lokale Loop-Video) — kein Compositing, kein zmq. Entsprechend
reconnectet die ausgehende Verbindung bei jedem Szenenwechsel, nicht nur bei den beiden
Live-Input-Übergängen wie im Zmq-Strategie-Fall. Austauschbar über
`Channel.CompositorStrategy: Zmq | RestartableFallback` (`ISceneEncoderFactory`).

### 4.3 Multi-Destination-Ausgabe

Die Ausgabe des Compositors wird per FFmpeg `tee`-Muxer gleichzeitig an mehrere Ziele gesendet:

```
-f tee -map 0:v -map 0:a
  "[f=flv]rtmp://live.twitch.tv/app/{twitchKey}|[f=flv]rtmp://a.rtmp.youtube.com/live2/{ytKey}"
```

Jede `StreamDestination` (Tabelle `stream_destinations`) kann unabhängig aktiviert/deaktiviert
werden, ohne den Compositor neu zu starten (Ziel-Liste wird beim Prozessstart aus der DB gelesen;
Änderungen zur Laufzeit erfordern aktuell einen Neustart des Ausgabe-Muxers — siehe Kapitel 13,
"Dynamische Ziel-Hinzufügung" als Erweiterung).

### 4.4 Low-Latency-Vorschau

MediaMTX stellt für jeden Kanal automatisch **HLS** (`/hls/live/{key}/index.m3u8`, über denselben
nginx-Origin wie das Dashboard, LL-HLS-fähig, ~2-4 s Latenz) sowie optional **WebRTC/WHEP**
(< 1 s Latenz) für die Web-Vorschau bereit. Das Frontend nutzt `hls.js` (Media Source Extensions)
für Chrome/Firefox/Edge, die kein natives HLS unterstützen, und die native `<video>`-Wiedergabe
für Safari. WHEP kann optional aktiviert werden (`VITE_ENABLE_WEBRTC_PREVIEW=true`) für Operator,
die Latenz priorisieren.

**Korrektur (Produktionsfund):** Der reine `hls.js`/`nginx`-Aufbau reichte nicht aus - der
Vorschau-Player blieb bei 0:00 schwarz, obwohl die Master-Playlist erreichbar war. Ursache:
MediaMTX' `lowLatency`-HLS-Muxer liefert seine fMP4-Chunks über lang laufende, effektiv
unbegrenzte Responses; nginx puffert Upstream-Antworten standardmäßig, wodurch diese Chunks nie
(oder erst mit erheblicher Verzögerung) beim Client ankamen. Die `/hls/`-Location in
`deploy/nginx/nginx.conf` setzt seitdem `proxy_buffering off`, `proxy_cache off`,
`proxy_http_version 1.1` und `proxy_read_timeout 60s`. Zusätzlich unterscheidet
`LivePreviewPlayer.tsx` jetzt technische Zustände (lädt / offline / Manifest nicht erreichbar /
Wiedergabefehler / live) statt einer einzigen generischen Fehlermeldung, und RTMP-/SRT-URLs im
Dashboard werden aus `window.location.hostname` abgeleitet (`frontend/src/config/serverConfig.ts`)
statt einen `<host>`-Platzhalter oder eine feste IP anzuzeigen.

---

## 5. Die Zustandsmaschine für den Szenenwechsel

### 5.1 Zustände

| Zustand | Bedeutung | Sichtbare Szene |
|---|---|---|
| `Offline` | Kein Encoder je verbunden / Kanal explizit gestoppt | Offline-Szene |
| `Connecting` | Encoder verbindet, erster Keyframe noch nicht da | Offline-Szene |
| `Live` | Stabiler Stream, alle Schwellwerte im grünen Bereich | Live-Ebene |
| `Degraded` | Verbunden, aber Bitrate/Paketverlust außerhalb Toleranz | Live-Ebene + Warn-Overlay |
| `Reconnecting` | Verbindung gerade verloren, Countdown läuft | Reconnecting-Szene mit Countdown |
| `BRB` | Reconnecting-Timeout überschritten, Encoder weiterhin weg | BRB-Szene |

### 5.2 Transitionstabelle

```
Offline      --EncoderConnected--------------------------------> Connecting
Connecting   --FirstKeyframeReceived----------------------------> Live
Connecting   --ConnectTimeout(10s)-----------------------------> Offline

Live         --BitrateBelowThreshold OR PacketLossAboveThreshold-> Degraded
Live         --EncoderDisconnected------------------------------> Reconnecting
Degraded     --MetricsRecovered(N aufeinanderfolgende Samples)--> Live
Degraded     --EncoderDisconnected------------------------------> Reconnecting

Reconnecting --EncoderConnected----------------------------------> Connecting  (dann s.o. -> Live sobald FirstKeyframeReceived)
Reconnecting --ReconnectTimeoutElapsed(default 20s)--------------> BRB

BRB          --EncoderConnected----------------------------------> Connecting  (dann s.o. -> Live sobald FirstKeyframeReceived)
BRB          --ManualStop----------------------------------------> Offline
```

`EncoderConnected` entspricht einer erfolgreichen `publish`-Prüfung am `authHTTPAddress`-Endpoint
(`POST /api/webhooks/mediamtx/auth`, Kapitel 3 Schritt 2), `FirstKeyframeReceived` dem
`runOnReady`-Webhook (erster Frame lesbar). Reconnecting/BRB
durchlaufen beim Wiederverbinden bewusst erneut `Connecting`, statt direkt nach `Live` zu
springen — so gilt derselbe `ConnectTimeoutSeconds`-Schutz wie beim Erstverbinden, und es gibt
nur einen Codepfad für "auf ersten Keyframe warten".

Zusätzlich: **jeder** Zustand → `Offline` bei `ManualStop` (Operator beendet den Kanal explizit
im Dashboard) oder `ChannelSuspended` (Admin sperrt Kanal).

### 5.3 Schwellwerte (konfigurierbar pro Kanal, `channel.scene_thresholds` JSONB)

| Parameter | Default | Beschreibung |
|---|---|---|
| `HeartbeatTimeoutSeconds` | 5 | Kein Heartbeat/Datenfluss → gilt als Verbindungsabbruch |
| `ReconnectTimeoutSeconds` | 20 | Zeit in `Reconnecting`, bevor auf `BRB` gewechselt wird |
| `MinBitrateKbps` | 1500 | Unterschreitung → `Degraded` |
| `MaxPacketLossPercent` | 3.0 | Überschreitung → `Degraded` |
| `RecoverySampleCount` | 3 | Anzahl guter Samples in Folge, bevor `Degraded → Live` |
| `ConnectTimeoutSeconds` | 10 | Max. Zeit bis erster Keyframe nach Publish |

### 5.4 Implementierung

`SceneStateMachine` (`Application/Streams/StateMachine/SceneStateMachine.cs`) ist eine reine,
seiteneffektfreie Klasse: `SceneTransitionResult Handle(SceneState current, SceneTrigger trigger,
SceneThresholds thresholds, SceneContext context)`. Sie kennt weder FFmpeg noch die Datenbank —
dadurch vollständig unit-testbar (siehe `backend/tests`). Die Hosted-Service-Schicht
(`SceneMonitorHostedService`) ruft sie auf, wendet das Ergebnis an (DB-Update, ZMQ-Kommando,
SignalR-Broadcast, Discord-Notification, Audit-Log-Eintrag) und ist damit der einzige Ort mit
Seiteneffekten.

### 5.5 Diagramm

```
                 ┌───────────┐
        ┌───────▶│  Offline  │◀────────────────────────────┐
        │        └─────┬─────┘                              │
        │ ConnectTimeout│ EncoderConnected                    │ ManualStop /
        │              ▼                                     │ ChannelSuspended
        │        ┌───────────┐   FirstKeyframe      ┌────────┴────┐
        │        │Connecting │─────────────────────▶│    Live      │
        │        └───────────┘                       └──┬───────┬─┘
        │                                    Bitrate low/│       │Encoder
        │                                     PacketLoss │       │Disconnected
        │                                       high      ▼       ▼
        │                                        ┌───────────┐ ┌───────────────┐
        │                    MetricsRecovered◀───│ Degraded  │ │ Reconnecting   │
        │                                        └─────┬─────┘ └───────┬────────┘
        │                                  EncoderDisconnected         │ ReconnectTimeout
        │                                              ▼                ▼
        │                                        ┌───────────────────────┐
        └────────────────────────────────────────│         BRB            │
                              EncoderConnected    └───────────────────────┘
                              (zurück zu Connecting, oben; von dort bei
                               FirstKeyframeReceived weiter zu Live)
```

---

## 6. Klassenstruktur (Clean Architecture)

```
RealLifeServer.Domain            (keine Abhängigkeiten)
 ├─ Entities: User, Channel, StreamDestination, StreamSession, StreamMetricSample,
 │            SceneEvent, HeartbeatRecord, AuditLogEntry
 ├─ Enums: UserRole, SceneState, StreamPlatform, StreamSessionStatus, IngestProtocol
 └─ Common: BaseEntity, IHasDomainEvent

RealLifeServer.Application        (abhängig von Domain)
 ├─ Common/Interfaces: IApplicationDbContext, I*Repository, IJwtTokenService,
 │       IPasswordHasher, ICurrentUserService, IDiscordNotifier, ISceneEventBroadcaster,
 │       IStreamStatsProvider, ISceneEncoder, IStreamOrchestrator, IDateTimeProvider
 ├─ Channels/Commands|Queries|Dtos   (CQRS-artige Use Cases, MediatR-Pattern)
 ├─ Streams/Commands|Queries|StateMachine
 └─ Auth/Commands|Dtos

RealLifeServer.Infrastructure      (abhängig von Application)
 ├─ Persistence: ApplicationDbContext (EF Core/PostgreSQL), Configurations, Repositories
 ├─ Auth: JwtTokenService, PasswordHasher (BCrypt), CurrentUserService
 ├─ Streaming: FfmpegCommandBuilder, ZmqFilterController, SceneEncoderProcess,
 │             MediaMtxClient, MediaMtxStatsProvider, StreamOrchestrator
 ├─ Notifications: DiscordWebhookNotifier
 └─ Caching: RedisChannelLockProvider (verteilte Kanal-Zuteilung bei Skalierung)

RealLifeServer.Api                 (abhängig von Application + Infrastructure — Composition Root)
 ├─ Controllers: Auth, Users, Channels, Streams, Webhooks, Metrics
 ├─ Hubs: StreamHub (SignalR)
 ├─ Realtime: HubSceneEventBroadcaster
 ├─ BackgroundServices: SceneMonitorHostedService, MetricsCollectorHostedService
 ├─ Middleware: ExceptionHandlingMiddleware
 └─ Program.cs (DI-Wiring, Middleware-Pipeline, Rate Limiting, JWT, Swagger)
```

**Abhängigkeitsrichtung**: `Api → Infrastructure → Application → Domain`. Application definiert
Interfaces, Infrastructure implementiert sie (Dependency Inversion). Dadurch lässt sich z. B.
`FfmpegCommandBuilder` durch eine GStreamer-Variante ersetzen, ohne Application/Domain anzufassen.

---

## 7. Datenbankschema

PostgreSQL, verwaltet über EF Core Migrations. Zentrale Tabellen:

```sql
users
 ├─ id                 uuid PK
 ├─ username            varchar(64) unique
 ├─ email               varchar(256) unique
 ├─ password_hash       varchar(256)
 ├─ role                smallint         -- 0=Admin, 1=Moderator, 2=Streamer
 ├─ created_at          timestamptz
 └─ is_active           boolean

channels
 ├─ id                        uuid PK
 ├─ owner_user_id             uuid FK -> users.id
 ├─ name                      varchar(128)
 ├─ slug                      varchar(64) unique
 ├─ stream_key                varchar(64) unique      -- Ingest-Key (RTMP/SRT/WHIP)
 ├─ ingest_protocols          smallint                -- Flags: Rtmp=1, Srt=2, Whip=4
 ├─ is_active                 boolean
 ├─ current_scene_state       smallint                -- Cache des aktuellen Zustands
 ├─ scene_thresholds          jsonb                   -- Kapitel 5.3
 ├─ compositor_strategy       smallint                -- Zmq | RestartableFallback
 ├─ created_at                timestamptz
 └─ updated_at                timestamptz

stream_destinations
 ├─ id                    uuid PK
 ├─ channel_id            uuid FK -> channels.id
 ├─ platform              smallint       -- Twitch, YouTube, Custom
 ├─ rtmp_url              varchar(512)
 ├─ stream_key_encrypted  varchar(512)   -- AES-256-GCM verschlüsselt, Key aus KeyVault/ENV
 ├─ is_enabled            boolean
 └─ display_order         smallint

stream_sessions
 ├─ id                uuid PK
 ├─ channel_id        uuid FK -> channels.id
 ├─ started_at        timestamptz
 ├─ ended_at           timestamptz nullable
 ├─ status             smallint      -- Running, Ended, Failed
 └─ end_reason         varchar(256) nullable

scene_events
 ├─ id                 uuid PK
 ├─ stream_session_id  uuid FK -> stream_sessions.id
 ├─ channel_id         uuid FK -> channels.id
 ├─ from_state         smallint
 ├─ to_state           smallint
 ├─ trigger            varchar(64)     -- z.B. "EncoderDisconnected"
 ├─ occurred_at        timestamptz
 └─ metadata           jsonb           -- z.B. {"bitrateKbps": 420, "packetLoss": 7.2}

stream_metric_samples
 ├─ id                  uuid PK
 ├─ stream_session_id   uuid FK -> stream_sessions.id
 ├─ recorded_at         timestamptz
 ├─ bitrate_kbps         integer
 ├─ packet_loss_percent  real
 ├─ fps_in               real
 ├─ cpu_usage_percent    real       -- Server-Host-Metrik
 ├─ ram_usage_percent    real
 └─ network_usage_mbps   real
   -- Partitioniert nach Monat (Range-Partitionierung) für Performance bei hoher Sample-Rate

heartbeat_records
 ├─ id            uuid PK
 ├─ channel_id    uuid FK -> channels.id
 ├─ received_at   timestamptz
 └─ source        varchar(32)     -- "mediamtx-webhook" | "encoder-agent"

audit_log_entries
 ├─ id            uuid PK
 ├─ user_id       uuid FK -> users.id nullable
 ├─ action        varchar(128)
 ├─ entity_type   varchar(64)
 ├─ entity_id     uuid nullable
 ├─ occurred_at   timestamptz
 └─ details       jsonb

refresh_tokens
 ├─ id             uuid PK
 ├─ user_id        uuid FK -> users.id
 ├─ token_hash     varchar(256)
 ├─ expires_at     timestamptz
 ├─ revoked_at     timestamptz nullable
 └─ created_at     timestamptz
```

Indizes: `channels.stream_key` (unique, für O(1)-Lookup bei jedem Publish-Webhook),
`scene_events(channel_id, occurred_at desc)`, `stream_metric_samples(stream_session_id, recorded_at)`.

`stream_metric_samples` ist die einzige hochfrequente Tabelle (Sample alle 2s pro aktivem Kanal).
Für den produktiven Betrieb: monatliches Partitionieren + Retention-Job (Standard: 30 Tage
Rohdaten, danach stündliche Aggregate in `stream_metric_hourly_aggregates`, siehe Kapitel 12).

---

## 8. Echtzeitkommunikation (WebSocket / SignalR)

`StreamHub` (SignalR, `/hubs/streams`) sendet an Clients, die einer Kanal-Gruppe beigetreten sind:

- `SceneChanged { channelId, fromState, toState, occurredAt, reason }`
- `MetricsUpdated { channelId, bitrateKbps, packetLossPercent, fpsIn, timestamp }`
- `SystemStatsUpdated { cpuPercent, ramPercent, networkMbps }` (serverweit, nur für Admin-Gruppe)
- `LogAppended { channelId, level, message, timestamp }`

Bei horizontaler Skalierung (mehrere API-Instanzen) läuft SignalR mit dem **Redis-Backplane**
(`AddStackExchangeRedis`), damit Broadcasts instanzübergreifend ankommen.

---

## 9. REST-API

Kurzüberblick (vollständige Swagger/OpenAPI-Doku unter `/swagger` im Dev-Modus):

```
POST   /api/auth/login                       Login, gibt JWT + Refresh-Token zurück
POST   /api/auth/refresh                     Access-Token erneuern
POST   /api/auth/register                    Admin legt neuen Nutzer an (Rolle Admin only)

GET    /api/channels                          Liste eigener/aller Kanäle
POST   /api/channels                          Kanal anlegen
GET    /api/channels/{id}                     Kanal-Details inkl. aktuellem Szenenstatus
PUT    /api/channels/{id}                     Kanal aktualisieren (Name, Schwellwerte)
POST   /api/channels/{id}/regenerate-key      Neuen Stream-Key generieren
DELETE /api/channels/{id}                     Kanal löschen (Admin/Owner)
POST   /api/channels/{id}/destinations        Ziel (Twitch/YouTube) hinzufügen
DELETE /api/channels/{id}/destinations/{did}  Ziel entfernen

GET    /api/streams/{channelId}/status         Aktueller Szenenstatus + letzte Metriken
POST   /api/streams/{channelId}/force-scene    Manuelles Override (z. B. BRB erzwingen)
POST   /api/streams/{channelId}/stop           Kanal explizit stoppen -> Offline
GET    /api/streams/{channelId}/metrics?range=1h  Bitrate-/Paketverlust-Verlauf
GET    /api/streams/{channelId}/events         Szenen-Event-Verlauf (Log)

POST   /api/webhooks/mediamtx/publish          MediaMTX runOnPublish-Callback (Key-Auth)
POST   /api/webhooks/mediamtx/ready            MediaMTX runOnReady-Callback
POST   /api/webhooks/mediamtx/not-ready        MediaMTX runOnNotReady-Callback
POST   /api/streams/{channelId}/heartbeat       Optionaler expliziter Heartbeat externer Agents

GET    /api/system/stats                       CPU/RAM/Netzwerk des Servers (Admin)
```

Alle Endpunkte außer `/api/auth/login` und `/api/webhooks/*` (die per Shared-Secret statt JWT
abgesichert sind) erfordern ein gültiges JWT (`Authorization: Bearer …`) und respektieren
Rollen via `[Authorize(Roles = "Admin,Moderator")]`.

---

## 10. Sicherheit

- **JWT** (kurzlebige Access-Token, 15 Min) + **Refresh-Token** (rotierend, in DB gehasht
  gespeichert, 14 Tage). Signierung mit HMAC-SHA256, Secret aus ENV/Secret-Manager, nie im Code.
- **Rollen**: `Admin` (Vollzugriff, Nutzerverwaltung), `Moderator` (alle Kanäle steuern, keine
  Nutzerverwaltung), `Streamer` (nur eigene Kanäle).
- **Passwort-Hashing**: BCrypt (Work Factor 12).
- **Stream-Key**: kryptographisch zufällig (32 Byte, Base62), nie im Klartext geloggt.
- **Zieldaten (Twitch/YouTube-Stream-Keys)**: AES-256-GCM verschlüsselt in der DB, Schlüssel aus
  `KeyProtection:MasterKey` (ENV, in Produktion aus Vault/KMS).
- **HTTPS**: TLS-Terminierung am Nginx-Reverse-Proxy (Let's Encrypt/Certbot), interner Verkehr
  zwischen Nginx↔API im Docker-Netz unverschlüsselt (vertrauenswürdiges internes Netz).
- **Rate Limiting**: ASP.NET Core `Microsoft.AspNetCore.RateLimiting`, striktere Policy für
  `/api/auth/login` (Brute-Force-Schutz, Fixed-Window 5 Versuche/Minute/IP) und
  `/api/webhooks/*` (Schutz vor Callback-Flooding).
- **Webhook-Absicherung**: unterschiedlich je Hook-Typ, da MediaMTX unterschiedliche Fähigkeiten
  bietet. `runOnReady`/`runOnNotReady` sind von uns selbst formulierte Shell-Kommandos
  (`deploy/mediamtx/mediamtx.yml`) und tragen deshalb einen `X-Webhook-Secret`-Header, den RLS
  prüft. Der synchrone Auth-Gate-Endpoint (`authHTTPAddress`, `POST /api/webhooks/mediamtx/auth`)
  wird dagegen von MediaMTX selbst mit einem fest vorgegebenen JSON-Body und **ohne
  konfigurierbare Header** aufgerufen — ein Secret-Header lässt sich dort technisch nicht setzen.
  Seine Sicherheit beruht stattdessen auf zwei Dingen: (a) für `action=publish` wird der reale
  Stream-Key gegen die `channels`-Tabelle geprüft, (b) der gesamte `/api/webhooks/*`-Präfix ist
  ausschließlich im internen Docker-Netz erreichbar — der `api`-Container veröffentlicht keinen
  Host-Port, und der öffentliche Nginx-Reverse-Proxy blockt `/api/webhooks/` explizit
  (`deploy/nginx/nginx.conf`).
- **CORS**: Whitelist der Frontend-Origin aus Konfiguration, keine Wildcards in Produktion.
- **Audit-Log**: sicherheitsrelevante Aktionen (Login, Key-Regenerierung, Rollenänderung,
  manuelle Szenen-Overrides) werden in `audit_log_entries` protokolliert.

---

## 11. Skalierung & Betrieb

### 11.1 Docker

`docker-compose.yml` orchestriert: `api` (RLS Backend), `frontend` (statisch via Nginx
ausgeliefert oder gemeinsam mit API-Container), `mediamtx`, `postgres`, `redis`, `nginx`
(Reverse Proxy). Siehe `docker-compose.yml` im Repo-Root.

### 11.2 Horizontale Skalierung

- **API-Schicht**: zustandslos bezüglich HTTP; mehrere Replicas möglich, JWT-Validierung
  benötigt keinen Shared State. SignalR nutzt Redis-Backplane (Kapitel 8).
- **Orchestrator/Compositor-Schicht** (FFmpeg-Prozesse) ist an eine konkrete Maschine
  gebunden (Prozess + Ports + evtl. GPU-Encoding). Bei mehreren Media-Nodes:
  - `channel_assignments`-Eintrag (Redis, `SET NX` mit TTL) bindet jeden Kanal exklusiv an genau
    einen Media-Node (verteiltes Lock, verhindert doppelte Ausgabe-Streams zur selben Plattform).
  - MediaMTX kann selbst im Cluster-Modus mit gemeinsamem RTSP-Redirect betrieben werden, oder
    Kanäle werden per Ingest-Hostname (`node1.ingest.example.com`) auf Media-Nodes verteilt
    (DNS round-robin oder ein leichter Zuteilungsdienst beim Kanal-Erstellen).
- **Datenbank**: PostgreSQL mit Read-Replicas für Dashboard-Leseabfragen (Metrik-Verlauf),
  Schreiblast bleibt gering (Events, keine Video-Daten).
- **Redis**: Cache für aktuelle Szenenstatus (Vermeidung von DB-Round-Trips bei jedem
  Statusabruf), Pub/Sub für SignalR-Backplane, verteilte Locks für Kanal-Zuteilung.

### 11.3 Reverse Proxy (Nginx)

- TLS-Terminierung, HTTP/2.
- `/api/*` → API-Container.
- `/hubs/*` → API-Container mit WebSocket-Upgrade-Headern.
- `/hls/*` → MediaMTX-Container (HLS-Segmente, mit Cache-Control für Segmentdateien).
- `/` → Frontend-Static-Build.
- Siehe `deploy/nginx/nginx.conf`.

---

## 12. Performance-Optimierungen

- **Kein Transcoding wo vermeidbar**: Der Compositor kodiert nur neu, wenn nötig (Szenen-Overlay
  erfordert Re-Encoding der video-Ebene; Audio wird bei reinem Live-Durchreichen per `-c:a copy`
  behandelt, wenn keine Szenenüberblendung aktiv ist — spart CPU).
  ## Empfehlung: NVENC/QuickSync-Hardwarebeschleunigung (`-c:v h264_nvenc` /
  `-c:v h264_qsv`) statt `libx264`, konfigurierbar über `Streaming:HardwareEncoder`.
- **Metrik-Sampling**: 2s-Intervall ist ein bewusster Kompromiss (reaktionsschnell genug für
  5s-Heartbeat-Timeout, aber keine DB-Flut). Hochfrequente Rohsamples werden nach 30 Tagen zu
  Stundenwerten aggregiert (Hintergrundjob `MetricsRetentionHostedService`).
- **Connection Pooling**: Npgsql-Pooling (Standard aktiv), `DbContext` als Scoped/Pooled
  (`AddDbContextPool`) zur Reduktion von GC-Druck bei hoher Request-Rate.
- **Redis-Cache** für `GET /api/streams/{id}/status` (häufig gepolltes Dashboard-Endpoint),
  invalidiert bei jedem Szenenwechsel-Event.
- **AOT-fähige Minimal-Hosting-Pipeline**: `Program.cs` nutzt Minimal-API-Hosting-Modell,
  Response-Compression (Brotli/Gzip) für JSON-Antworten aktiviert.
- **Backpressure beim Metrik-Polling**: `IStreamStatsProvider`-Aufrufe laufen pro Kanal in
  eigenem `Task`, aber global durch einen `SemaphoreSlim` begrenzt, um MediaMTX bei vielen
  gleichzeitigen Kanälen nicht mit API-Calls zu fluten.
- **FFmpeg-Prozessüberwachung**: `SceneEncoderProcess` überwacht `stderr`/`-progress`-Pipe
  asynchron (`Channel<T>`-basierte Producer/Consumer-Warteschlange), kein Polling per Sleep-Loop.

---

## 13. Erweiterungsmöglichkeiten

Diese Punkte sind bewusst **nicht** in der aktuellen Codebasis implementiert, aber die
Architektur ist so geschnitten, dass sie sich sauber ergänzen lassen:

- **Cloudflare Stream / Cloudflare-Tunnel**: `IIngestGateway`-Interface erlaubt einen weiteren
  Adapter, der statt lokalem MediaMTX Cloudflare Stream Live Inputs verwendet (Ingest global
  verteilt, geringere Latenz für Streamer). Der `StreamOrchestrator` bliebe unverändert.
- **Multi-Region**: Mehrere Media-Nodes (Kapitel 11.2) je Region, Nutzer wird beim Kanal-Setup
  dem nächstgelegenen Node zugewiesen (GeoDNS oder Anycast). Control-Plane (API/DB) bleibt
  zentral oder wird selbst georepliziert (Postgres-Logical-Replication).
- **Recording**: MediaMTX unterstützt bereits serverseitiges Aufzeichnen pro Pfad
  (`record: yes` in `mediamtx.yml`). Ergänzung: `RecordingsController` + `recordings`-Tabelle
  (Pfad, Dauer, Kanal, Größe) und ein Hintergrundjob, der fertige Segmente in Object Storage
  (S3/MinIO) verschiebt.
- **Replay/Highlight-System**: Auf Basis der Aufzeichnung ein Ringpuffer der letzten N Minuten
  (bereits von MediaMTX als Segmente vorgehalten) + Endpoint, der per FFmpeg einen Clip aus dem
  Zeitfenster `[jetzt-30s, jetzt]` schneidet ("Instant Replay"-Button im Dashboard).
- **Clip-System**: Nutzer markiert im Dashboard einen Zeitraum, Hintergrundjob schneidet per
  FFmpeg `-ss`/`-t` aus der Aufzeichnung, lädt in Object Storage, erzeugt Thumbnail + Share-Link.
  Neue Tabelle `clips (id, channel_id, start, end, storage_url, created_by)`.
- **Adaptive Bitrate / mehrere Qualitätsstufen für die Web-Vorschau**: MediaMTX kann mehrere
  Ausgabe-Ladder-Profile erzeugen; Frontend-Player auf HLS-ABR umstellen.
- **Mobile Encoder-Companion-App**: eigener Heartbeat-Agent, der zusätzliche Telemetrie
  (GPS, Akkustand, Signalstärke) über `/api/streams/{id}/heartbeat` sendet — Schema ist bereits
  erweiterbar (`heartbeat_records.metadata jsonb`, aktuell ungenutzt, siehe TODO im Entity).
- **Automatische Szenen-Overlays mit Live-Daten** (Uhrzeit, GPS-Karte, Akkustand) via
  `drawtext`/`overlay`-Filter, gespeist aus Heartbeat-Metadaten.
- **Kubernetes statt Docker Compose** für große Deployments: `StatefulSet` für Media-Nodes
  (stabile Netzwerkidentität pro Node wichtig für Ingest-DNS), `Deployment` für API/Frontend.

---

## 14. Verzeichnisstruktur des Repos

```
Real-Life-Server/
├── docs/
│   └── CONCEPT.md                 dieses Dokument
├── docker-compose.yml
├── .env.example
├── deploy/
│   ├── nginx/nginx.conf
│   ├── mediamtx/mediamtx.yml
│   └── assets/scenes/             Platzhalter BRB/Reconnecting/Offline-Videos (README)
├── backend/
│   ├── RealLifeServer.sln
│   ├── src/
│   │   ├── RealLifeServer.Domain/
│   │   ├── RealLifeServer.Application/
│   │   ├── RealLifeServer.Infrastructure/
│   │   └── RealLifeServer.Api/
│   └── tests/
│       └── RealLifeServer.Application.Tests/
└── frontend/
    ├── src/
    │   ├── api/, hooks/, context/, types/, pages/, components/
    └── ...Vite/Tailwind-Konfiguration
```

---

## 15. Betriebs-Runbook

```bash
# 1. Umgebungsvariablen konfigurieren
cp .env.example .env   # JWT-Secret, DB-Passwort, Discord-Webhook-URL etc. eintragen

# 2. Kompletten Stack starten
docker compose up -d --build

# 3. Datenbank-Migrationen anwenden (einmalig / bei Schema-Änderungen)
docker compose exec api dotnet RealLifeServer.Api.dll --migrate

# 4. Frontend erreichbar unter https://<host>/
#    API/Swagger (nur Dev) unter https://<host>/api/swagger
#    RTMP-Ingest: rtmp://<host>:1935/live/{streamKey}
#    SRT-Ingest:  srt://<host>:8890?streamid=publish:{streamKey}
#    WHIP-Ingest: https://<host>/whip/{streamKey}

# 5. Neuen Kanal anlegen -> im Dashboard "Kanal erstellen", Stream-Key im Encoder eintragen.
```

Health-Checks: `GET /healthz` (API liveness), `GET /healthz/ready` (DB+Redis+MediaMTX
erreichbar). Docker-Compose-Healthchecks sind entsprechend konfiguriert, `depends_on:
condition: service_healthy` verhindert Race-Conditions beim Start.
