#!/bin/sh
set -eu

RULE_SRC="/usr/lib/udev/rules.d/50-qmk.rules"
RULE_DST="/etc/udev/rules.d/50-qmk.rules"

if [ -f "$RULE_SRC" ]; then
    echo "QMK udev rules are already installed at:"
    echo "  $RULE_SRC"
    exit 0
fi

if [ ! -f "./50-qmk.rules" ]; then
    echo "50-qmk.rules not found next to this installer."
    exit 1
fi

echo "Installing QMK udev rules..."
sudo install -m 0644 ./50-qmk.rules "$RULE_DST"

echo "Reloading udev..."
sudo udevadm control --reload-rules || true
sudo systemctl restart systemd-udevd.service

echo "QMK udev rules installed."
echo "Reconnect or reset your keyboard before flashing."
