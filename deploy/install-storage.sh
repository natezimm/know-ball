#!/usr/bin/env bash
set -euo pipefail
root="${KNOWBALL_ROOT:-$HOME/know-ball}"
user="$(id -un)"
[[ "$root" =~ ^/[a-zA-Z0-9_./-]+$ && "$user" =~ ^[a-zA-Z0-9_-]+$ ]] || { echo 'Unsupported maintenance path or user' >&2; exit 1; }
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cat > "$work/knowball-storage.service" <<EOF
[Unit]
Description=Remove unused Know Ball deployment images
After=docker.service

[Service]
Type=oneshot
User=$user
WorkingDirectory=$root
ExecStart=/usr/bin/python3 "$root/storage.py" cleanup --root "$root"
UMask=0077
EOF
cat > "$work/knowball-storage.timer" <<'EOF'
[Unit]
Description=Daily Know Ball image cleanup

[Timer]
OnCalendar=*-*-* 08:00:00 UTC
RandomizedDelaySec=15m
Persistent=true

[Install]
WantedBy=timers.target
EOF
for unit in knowball-storage.service knowball-storage.timer; do
  sudo -n install -m 644 "$work/$unit" "/etc/systemd/system/$unit"
done
sudo -n systemctl daemon-reload
sudo -n systemctl enable --now knowball-storage.timer
