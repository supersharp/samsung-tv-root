# Home-shell services — only with a replacement launcher

> **Do not disable these on a TV that still uses Samsung Home.** They are the data
> layer behind Smart Hub: the app/recommendation board and the content tiles on the
> Home screen. With them off, a stock TV opens a blank or degraded Home — no app
> grid, no recommendations, no store surface.
>
> This is only sane if you have replaced Home with a launcher. The launcher this
> work was built for is
> [**Samsung-TV-Launcher-Redux**](https://github.com/supersharp/Samsung-TV-Launcher-Redux):
> a 1920×1080 remote-only web app that remaps the Home button to
> `TvLaunch01.Launcher`. Install and verify that first, including its All apps and
> Connected Devices screens, before touching anything below.

These services are **not disabled in the reference setup**. They were trialled
once with a one-shot state machine and deliberately restored, because a plain kill
does not hold and because they are the most user-visible thing in this whole
debloat.

---

## The three services

| App id | Role | What stops working without a launcher |
| --- | --- | --- |
| `com.samsung.tv.sboard-app` | Smart Hub board; app-store and recommendation surface | Home board, store/recommendation rows, some store pushes |
| `org.tizen.content-tiles-provider` | Samsung Home content tiles (native) | “For you”/content rows on Home |
| `com.samsung.tv.waapp-contents-provider` | Samsung Home content tiles (web apps) | Web-app content rows on Home |

Samsung Home itself (`com.samsung.tv.csfs`) was **not** disabled. It still backs
Settings ▸ Samsung Home and store flows; disabling it as well would break those
and is not recommended.

## Why a plain kill is not enough

A running instance can be killed, but the app framework restarts
`org.tizen.content-tiles-provider` and `com.samsung.tv.waapp-contents-provider`
within roughly 20 seconds (observed twice on the reference TV). `sboard-app` is
launched by the Home flow rather than held resident, so it simply comes back the
next time Home is opened.

The only durable way is the package database, exactly like the other apps:
`app_disable='true'` plus `app_onboot='false'` and `app_autorestart='false'`.
That survives reboot, and `--revert` restores the flags.

## The one-shot trial used on the reference TV

The reference `launch.sh` contains a small state machine behind
`/opt/usr/share/systemd/system/selfroot-trial-state`, so a mistake costs one
reboot, not a permanent change:

| State on boot | Action |
| --- | --- |
| absent | **arm:** disable the three apps, kill them, write `armed` |
| `armed` | **trial:** leave them disabled for this boot; write `trial` |
| `trial` | **restore:** set the flags back, write `restored` |
| `restored` | nothing |

Flow: arm → **reboot = trial** (three apps absent) → **reboot = restored** (flags
back, `content-tiles-provider` running again). Re-arm by deleting the state file
and rebooting. The reference TV ended in `restored`; the three apps are enabled
today.

That block is not shipped in this repository's `launch.sh` — it is deployment
specific. The commands below reproduce the same end state manually.

## Disable (after installing and testing a launcher)

```sh
DB=/opt/dbspace/.pkgmgr_parser.db
cp -f "$DB" /opt/usr/share/selfroot-backup/pkgmgr-pre-home-shell.db

sqlite3 "$DB" "UPDATE package_app_info
  SET app_disable='true', app_onboot='false', app_autorestart='false'
  WHERE app_id IN (
    'com.samsung.tv.sboard-app',
    'org.tizen.content-tiles-provider',
    'com.samsung.tv.waapp-contents-provider'
  ); SELECT changes();"
```

Kill the two tile providers so the change takes effect before the next boot;
`sboard-app` exits with Home:

```sh
/usr/bin/app_launcher -k org.tizen.content-tiles-provider
/usr/bin/app_launcher -k com.samsung.tv.waapp-contents-provider
/usr/bin/app_launcher -k com.samsung.tv.sboard-app
```

If `app_launcher -k` reports “not available for the user 0”, the app is already
marked disabled — check the DB row, not the kill.

## Verify

```sh
sqlite3 "$DB" "SELECT app_id, app_disable, app_onboot, app_autorestart
  FROM package_app_info WHERE app_id IN (
    'com.samsung.tv.sboard-app',
    'org.tizen.content-tiles-provider',
    'com.samsung.tv.waapp-contents-provider');"

ps -eo pid,args | grep -E '[s]board-app|[c]ontent-tiles|[w]aapp-contents'
```

The DB rows should read `true|false|false`, and the `ps` output should be empty
after the kill (the tiles may reappear within ~20 s if the flags did not apply).

## Revert

```sh
sqlite3 "$DB" "UPDATE package_app_info
  SET app_disable='false', app_onboot='true', app_autorestart='true'
  WHERE app_id IN (
    'com.samsung.tv.sboard-app',
    'org.tizen.content-tiles-provider',
    'com.samsung.tv.waapp-contents-provider');"
```

Remove any trial state file (`/opt/usr/share/systemd/system/selfroot-trial-state`)
so a boot cannot re-arm the trial. Launch Samsung Home once (or reboot) and check
that the content rows return.

## Before you do this

- The launcher must be installed, signed in, and able to launch apps and switch
  inputs. Test a cold boot with the TV offline from the network first.
- Keep the package-DB backup. There is no “reset Smart Hub” shortcut here that
  does not re-download Samsung preloads.
- A firmware update can reinstate the apps and the board. Re-check after every
  update.
