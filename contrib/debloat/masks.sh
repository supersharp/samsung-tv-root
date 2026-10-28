#!/bin/sh
# Debloat mask block for launch.sh — a fragment, not a standalone installer.
#
# Paste this after the relay start in launch.sh if you want the masks re-applied
# every boot. It can also be run once by hand on a rooted TV to apply them live.
# Unit names differ between firmware releases; check `systemctl list-units`
# before using this on another model.
#
# Removing the flag file and rebooting (or unmasking by hand) reverts a group:
#   /opt/usr/share/systemd/system/selfroot-debloat-acr-off
#   /opt/usr/share/systemd/system/selfroot-debloat-report-off

mask_units() {
    for unit in "$@"; do
        systemctl mask --runtime "$unit" >/dev/null 2>&1
    done
    systemctl --no-block stop "$@" >/dev/null 2>&1
}

# Core background services: ads/KPI, update checks and the SLive provider.
mask_units \
    adagent-service.service \
    nagsam-daemon.service \
    org.tizen.tv.sp-kpi.service \
    kpi-logging.service \
    slive-provider-daemon.service \
    slive-provider-daemon.path \
    swu-auto-upgrade.service \
    swu-auto-upgrade.timer \
    swu-auto-upgrade.path \
    vdsystem-update-manager.service \
    vdsystem-update-manager.timer

# ACR / PISA. The marker file also stops pisa-service through its
# ConditionPathExists=, independent of the runtime mask.
if [ ! -e /opt/usr/share/systemd/system/selfroot-debloat-acr-off ]; then
    mask_units \
        contents-recognition-service.service \
        pisa-service.service \
        pisa-service.socket \
        ambient-ua-agent.service
    touch /opt/usr/share/systemd/system/pisa-service 2>/dev/null
fi

# Reporting / telemetry.
if [ ! -e /opt/usr/share/systemd/system/selfroot-debloat-report-off ]; then
    mask_units \
        media-analytics-service.service \
        devicetracker.service \
        abnormal_user_behavior.service \
        issue_report_agent.service \
        emergency-log-transmitter.service \
        wifi-presence.service \
        wifi-presence.path \
        contents-ui-detector-service.service
fi
