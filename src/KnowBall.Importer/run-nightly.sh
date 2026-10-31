#!/bin/sh
# Production's importer: runs `--nightly` at Import__DailyAt (default 04:00) in Import__TimeZone (default
# America/New_York), as a short-lived process each time. Between imports only this shell and `sleep` stay
# in memory, instead of a .NET worker that would sit idle all day on a small shared VM.
# Like the worker, it also runs once on start unless Import__RunOnStartup is false.
set -u
at="${Import__DailyAt:-04:00}"
zone="${Import__TimeZone:-America/New_York}"
child=""
# Docker stops the container with SIGTERM; pass it on so an import or a sleep ends at once.
trap '[ -n "$child" ] && kill -TERM "$child" 2>/dev/null; exit 0' TERM INT

run() {
  dotnet KnowBall.Importer.dll --nightly "$@" &
  child=$!
  wait "$child" || echo "Import exited with status $?"
  child=""
}

[ "${Import__RunOnStartup:-true}" = "false" ] || run "$@"

while :; do
  now=$(date +%s)
  next=$(TZ="$zone" date -d "today $at" +%s)
  [ "$next" -gt "$now" ] || next=$(TZ="$zone" date -d "tomorrow $at" +%s)
  echo "Next import at $(TZ="$zone" date -d "@$next" '+%Y-%m-%d %H:%M %Z') ($zone)"
  sleep $((next - now)) &
  child=$!
  wait "$child"
  child=""
  run "$@"
done
