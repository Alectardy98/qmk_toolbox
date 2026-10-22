#!/bin/sh
set -eu
HERE="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
RULE="$HERE/50-qmk.rules"
test -r "$RULE"
if [ "$(id -u)" -ne 0 ]; then
    exec sudo "$0" "$@"
fi
install -m 0755 "$HERE/qmk_id" /usr/lib/udev/qmk_id
install -m 0644 "$RULE" /etc/udev/rules.d/50-qmk.rules
udevadm control --reload-rules
printf '%s\n' 'QMK rules installed. Reconnect your keyboard to apply them.'
