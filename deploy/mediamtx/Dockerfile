# syntax=docker/dockerfile:1
#
# The official bluenviron/mediamtx image is built FROM scratch (just the static Go binary -
# no shell, no curl). Our runOnReady/runOnNotReady hooks in mediamtx.yml need both to notify
# RealLifeServer of scene-relevant events (docs/CONCEPT.md, chapter 3), so this layers the
# official binary onto a minimal Alpine base, matching the pattern documented at
# https://github.com/bluenviron/mediamtx (README: "Customizing the image").
FROM bluenviron/mediamtx:latest AS mediamtx

FROM alpine:3.20
RUN apk add --no-cache curl
COPY --from=mediamtx /mediamtx /mediamtx
ENTRYPOINT ["/mediamtx"]
