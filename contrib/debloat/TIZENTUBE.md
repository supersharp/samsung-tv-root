# Optional edit — keep TizenTube working after debloating

Only relevant if you use **TizenTube**. Skip it if you use stock YouTube.

TizenTube is installed as the web app `xvvl3S1TT1.TizenTubeStandalone`, a native wrapper around
Samsung's `com.samsung.tv.cobalt-yt`. The wrapper ships a service, `xvvl3S1TT1.StandaloneService`,
which runs the local proxy (`127.0.0.1:8100`, Cobalt proxy `127.0.0.1:8101`). The wrapper launches
Cobalt with userdata that points it at that proxy (`--proxy=127.0.0.2:8101`).

## What went wrong

The 2026-10-26 debloat disabled `xvvl3S1TT1.StandaloneService` in the “ads / telemetry” group. It is
not an ad service; it is TizenTube's own proxy. With it disabled:

- the TizenTube tile opens **stock YouTube**;
- the TizenTube settings/menu is missing;
- nothing in the package list looks broken, because the app itself is still installed.

If TizenTube suddenly behaves like stock YouTube after a debloat, check this service first.

## The edit

Two parts. The first is persistent, the second is needed because the TV preloads stock YouTube at
boot.

### 1. Re-enable the service

```sh
DB=/opt/dbspace/.pkgmgr_parser.db
cp -f "$DB" /opt/usr/share/selfroot-backup/pkgmgr-pre-tizentube.db

sqlite3 "$DB" "UPDATE package_app_info
  SET app_disable='false', app_onboot='true', app_autorestart='true'
  WHERE app_id='xvvl3S1TT1.StandaloneService';"

/usr/bin/app_launcher -s xvvl3S1TT1.StandaloneService
```

The row should read `app_disable=false|app_onboot=true|app_autorestart=true`.

### 2. Boot hook (fresh Cobalt launch)

`org.tizen.litewebappservice` preloads `com.samsung.tv.cobalt-yt` at startup. The wrapper can only
apply its `--proxy=…` userdata to a **fresh** launch, so a preloaded Cobalt wins and TizenTube again
looks like stock YouTube. The reference `launch.sh` therefore ends with:

```sh
/usr/bin/app_launcher -k com.samsung.tv.cobalt-yt
/usr/bin/app_launcher -s xvvl3S1TT1.StandaloneService
```

That is the “option” part: add those two lines to your `launch.sh` if you want TizenTube to be the
YouTube you get after a reboot. Without them the service is enabled but a boot-time preload can
still take over; opening TizenTube again usually recovers, but the first tap may be stock.

## Verify

```sh
sqlite3 /opt/dbspace/.pkgmgr_parser.db \
  "SELECT app_id, app_disable, app_onboot, app_autorestart
   FROM package_app_info WHERE app_id='xvvl3S1TT1.StandaloneService';"

curl -s -m 3 http://localhost:8100/tizentube/getState; echo

ps -eo pid,args | grep -E '[x]vvl3S1TT1|[c]obalt_launcher' | cut -c1-110
```

A healthy install returns a JSON state from `:8100` and shows the service process. Launch TizenTube
and confirm the TizenTube menu/settings appear.

## Revert

Return to the pre-edit (broken) state:

```sh
sqlite3 "$DB" "UPDATE package_app_info
  SET app_disable='true', app_onboot='false', app_autorestart='false'
  WHERE app_id='xvvl3S1TT1.StandaloneService';"
/usr/bin/app_launcher -k xvvl3S1TT1.StandaloneService
```

Remove the two `launch.sh` lines if you added them. Stock YouTube (`com.samsung.tv.cobalt-yt`) is
never modified by this edit.

## Note on other TizenTube components

The wrapper's own app id (`xvvl3S1TT1.TizenTubeStandalone`) and the stock Cobalt app stay enabled.
Do not add any `xvvl3S1TT1.*` row to the debloat list; only Samsung's telemetry/ads components
belong there.
