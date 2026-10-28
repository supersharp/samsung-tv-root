# Debloat reference — QN95A / Tizen 6.0

This is the debloat actually applied to the reference TV. It is **documentation, not an
installer**: app ids and unit names differ between firmware releases, so verify everything on
your own set before changing the package database or masking a unit. There is nothing
device-unique here (no addresses, tokens, certificates or baselines).

Reference device: `QE65QN95AATXXU`, `T-NKM2DEUC-2310.0` (build
`T-NKM2DEUC-REL-202607211844`), Tizen 6.0.0, ARMv7. Other models and firmware are untested.

Read the [safety notes](../README.md#warnings) first. Root is required for every command here.

> **Home-shell services are a separate risk class.** `com.samsung.tv.sboard-app`,
> `org.tizen.content-tiles-provider` and `com.samsung.tv.waapp-contents-provider`
> are deliberately **not** in the 40 below. Disabling them removes Samsung's Home
> board and content rows, so it only makes sense with a replacement launcher —
> see [HOME-SERVICES.md](HOME-SERVICES.md) for the warning, the one-shot trial and
> the revert.

---

## How the debloat is layered

| Layer | Mechanism | Persistence |
| --- | --- | --- |
| Apps | `package_app_info.app_disable='true'`, `app_onboot='false'`, `app_autorestart='false'` in `/opt/dbspace/.pkgmgr_parser.db` | Persistent; the app framework honours it at boot |
| Daemons | `systemctl mask --runtime` + `systemctl --no-block stop` | Volatile; `/etc/systemd/system` is read-only on this firmware |
| Boot re-apply | the mask block is injected into `launch.sh`, which runs as uid 0 at every boot | Re-applied each power-on |
| Per-group off flags | marker files under `/opt/usr/share/systemd/system/` gate the ACR and reporting passes | Survive reboot; remove the flag and the next boot stops masking |

The `launch.sh` shipped by this repository contains the relay hardening and the boot guards but
**not** the debloat list below, because the list is model-specific. The reference deployment adds
it after the relay start. If you want the same persistence, copy the mask block from
[`masks.sh`](masks.sh) into your `launch.sh` and add the app writes to your install notes.

---

## Disabled apps — 40

Written as one `UPDATE`, then each running instance is killed. `app_disable` is what the app
framework checks; `app_onboot` / `app_autorestart` stop it being pulled up again.

```sh
DB=/opt/dbspace/.pkgmgr_parser.db
cp -f "$DB" /opt/usr/share/selfroot-backup/pkgmgr-pre-debloat.db

sqlite3 "$DB" "UPDATE package_app_info
  SET app_disable='true', app_onboot='false', app_autorestart='false'
  WHERE app_id IN ('…');"
```

| Group | App ids |
| --- | --- |
| Ads / ACR / telemetry | `com.samsung.tv.targeted-ad-service`, `com.samsung.tv.lightweight-node-service`, `com.samsung.tv.acr` |
| PISA | `com.samsung.tv.pisa-service`, `org.tizen.pisa-control-service` |
| Reporting / ad extras | `com.samsung.tv.coba.adagent`, `com.samsung.tv.adagent-service` |
| Xbox (deactivated, package kept) | `GHI3a0zMSx.XboxGamePass`, `GHI3a0zMSx.Service` |
| Preloads | `org.tizen.browser`, `org.tizen.ambientapp`, `com.samsung.tv.fuzzy-search-engine`, `com.samsung.tv.vif`, `com.samsung.tv.action-planner-service`, `com.samsung.tv.digitalbutler-app`, `com.samsung.tv.digitalbutler-daemon` |
| Voice (top level) | `com.samsung.tv.google-assistant`, `com.samsung.tv.bixby-prov`, `com.samsung.tv.ondevice-voice`, `com.samsung.tv.utterance-recommender-core`, `org.tizen.multi-assistant-service`, `org.tizen.voice-app` |
| Voice (companions) | `com.samsung.tv.bixby-context-client`, `com.samsung.tv.bixbycapsuleviewer`, `com.samsung.tv.cnvoice`, `com.samsung.tv.cnvoice-assistant`, `com.samsung.tv.farfieldvoice`, `com.samsung.tv.mybixby`, `com.samsung.tv.utterance-recommender-app`, `com.samsung.tv.voiceassistant-music`, `com.samsung.tv.wakeup-engine-bixby`, `org.tizen.voice-app-service`, `org.tizen.voice-client`, `org.tizen.voice-onboarding` |
| Feature preloads | `org.tizen.hbbtv`, `org.tizen.hbbtv-service`, `com.samsung.tv.multiscreen`, `com.samsung.tv.preview-updater`, `com.samsung.tv.slive-daemon` |
| Launcher trim | `com.samsung.tv.tvplus` |

Notes:

- **Do not put `xvvl3S1TT1.StandaloneService` in this list.** It was in the original telemetry
  group by mistake; it is TizenTube's own service. See [TIZENTUBE.md](TIZENTUBE.md).
- The Xbox apps are **deactivated**, not uninstalled. The store re-pushed the package after an
  uninstall; `pkgcmd -D` plus `app_disable` keeps it off without fighting the store. Launching it
  returns “not available for the user 0”.
- Killing a disabled app with `app_launcher -k <id>` can return “not available for the user 0”.
  Either kill it by exact pid before the disable lands, or accept that it exits at the next boot.

---

## Masked daemons — 23 units

Applied live with `systemctl mask --runtime`, then `systemctl --no-block stop`. The reference
`launch.sh` re-applies them once per boot (after the relay), gated by the flags described below.

| Group | Units |
| --- | --- |
| Core | `adagent-service`, `nagsam-daemon`, `org.tizen.tv.sp-kpi`, `kpi-logging`, `slive-provider-daemon` + `.path` |
| Update | `swu-auto-upgrade` + `.timer` / `.path`, `vdsystem-update-manager` + `.timer` |
| ACR / PISA | `contents-recognition-service`, `pisa-service` + `.socket`, `ambient-ua-agent` |
| Reporting | `media-analytics-service`, `devicetracker`, `abnormal_user_behavior`, `issue_report_agent`, `emergency-log-transmitter`, `wifi-presence` + `.path`, `contents-ui-detector-service` |

Firmware updates are **not** masked: `bootmode-system-update` and the `fw-update-*` units stay
enabled so security updates still work.

### Off flags

- `selfroot-debloat-acr-off` — when present, `launch.sh` skips the ACR/PISA mask group; this is what
  makes an ACR revert survive reboots.
- `selfroot-debloat-report-off` — same for the eight reporting units.
- `/opt/usr/share/systemd/system/pisa-service` — marker file that satisfies `pisa-service`'s
  `ConditionPathExists=`, stopping the unit even outside the mask block.

---

## Verify

```sh
sqlite3 /opt/dbspace/.pkgmgr_parser.db \
  "SELECT count(*) FROM package_app_info WHERE app_disable='true';"

for u in adagent-service swu-auto-upgrade vdsystem-update-manager nagsam-daemon \
         org.tizen.tv.sp-kpi kpi-logging slive-provider-daemon; do
  printf '%-32s %s/%s\n' "$u" "$(systemctl is-active "$u")" "$(systemctl is-enabled "$u")"
done

systemctl --failed --no-pager
ps -eo pid,pcpu,rss,args | grep -E '[c]ontents-recognition|[p]isa-service|[a]mbient-ua'
```

`systemctl --failed` should be empty. A stale failed unit from an earlier mask is cleared with
`systemctl reset-failed` (the reference `launch.sh` does this ~20 s after boot).

---

## Re-enable

Apps:

```sh
sqlite3 "$DB" "UPDATE package_app_info
  SET app_disable='false', app_onboot='true', app_autorestart='true'
  WHERE app_id IN ('…');"
```

Units: `systemctl unmask <units>` and `systemctl start <units>` (skip `.socket`/`.path`), or simply
remove the flag file and power-cycle. For the ACR and reporting groups, remove their flag first,
otherwise `launch.sh` will mask them again at the next boot.

The reference deployment keeps a package-DB copy before each pass under
`/opt/usr/share/selfroot-backup/`; restore that if a change goes wrong.

---

## What is not included

- No relay token, TV address, certificate or private key.
- No Samsung binaries or firmware.
- No per-device backup database. Take your own before writing.

See [TIZENTUBE.md](TIZENTUBE.md) for the optional TizenTube service re-enable, and
[HOME-SERVICES.md](HOME-SERVICES.md) for the launcher-gated Home-shell services.
