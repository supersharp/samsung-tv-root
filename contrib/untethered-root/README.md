# selfroot-untethered

Untethered root for Samsung Tizen 6.0 televisions, delivered as a **clean signed
TPK** that re-acquires `uid 0` on its own at every boot — no host computer, no
USB stick, no Developer-Mode IP flip.

The TV is rooted again after each full power cycle, by a boot chain that lives
entirely on the TV. A tiny launcher stub in a Samsung service slot starts a
normal, independently signed TPK app through the app framework; that app then
drives the existing `samsung-tv-root` archive-root route to `uid 0` and starts a
token-gated management relay on the LAN.

> This is **research / personal-device tooling**. Use it only on hardware you own
> or are authorised to examine. See [Security & legal](#security--legal).

---

## How it works

```
power on
 └─ systemd → app-boot-manager
     └─ com.samsung.tv.ghservice  (svcapp slot, squatted with our ~5 KB stub)
         └─ aul_launch_app("com.selfroot.boot")      # app framework, not exec()
             └─ com.selfroot.boot  (clean signed TPK, dotnet 3.1)
                 ├─ writes a unique /tmp/selfroot-launch-*.sh
                 ├─ sdb CNXN + OPEN shell on 127.0.0.1:26101
                 ├─ SVE-2025-50109 appinstall package-name injection
                 ├─ cat <script> | bash                  (UEP-safe: stdin, not a path)
                 ├─ /usr/bin/dotnet SamsungTvArchiveRoot.dll
                 │    (file caps cap_setgid,cap_sys_admin)
                 ├─ unshare(CLONE_NEWNS) + bind passwd / public.pem
                 └─ sdbd-tarlauncher -xzf <signed tar.gz> --to-command=/bin/sh
                     └─ launch.sh as uid 0 → RootRelay.dll on 0.0.0.0:26103
```

Nothing on the read-only rootfs is modified; the bind mounts live only in the
payload's private mount namespace.

### Two hard constraints this design works around

1. **The boot manager will not start a new app id.** On Tizen 6.0 the
   `app-boot-manager` only boots app ids it already knows (firmware image + apps
   installed through Samsung's OTA path). Setting `package_app_info.app_onboot`,
   `app_autorestart`, `app_preload`, `package_preload`, manifest `on-boot`, or the
   preload list does **not** add a `pkgcmd`-installed app id. So a *squat* of an
   already-booted Samsung service app is the boot trigger.
2. **The squatted slot cannot `exec()` subprocesses.** The app runs with a
   `User::Pkg` SMACK label; `Process.Start` fails with `Permission denied`. The
   current agent works because it speaks sdb over **sockets**. To launch the TPK
   from the slot we therefore P/Invoke `aul_launch_app` (app framework), which the
   sandbox does allow.

Result: the TPK is the authoritative, versioned, re-signable payload; the squat
is a 5 KB launcher that almost never changes.

---

## Repository layout

| Path | Purpose |
| --- | --- |
| `src/agent/Program.cs` | The TPK agent: sdb handshake, injection, archive-root handoff |
| `src/stub/Program.cs` | The `ghservice`-slot launcher (`aul_launch_app`) |
| `src/relay/Program.cs` | The root management relay (token-gated, binds `:26103`) |
| `src/payload/build_artifacts.py` | RSA key + passwd + signed `launch.sh` tar for archive-root |
| `src/tpk/tizen-manifest.xml` | TPK manifest (ui-app, `nodisplay`, `dotnet`) |
| `src/tpk/build_tpk.py` | Packages the unsigned TPK |

---

## Requirements

- **TV**: Samsung Tizen 6.0, ARMv7 (kernel 4.1.x). Verified on a QE65QN95A
  (2021 Neo QLED, `21_NIKEM2_QTV` platform), firmware `T-NKM2DEUC-2310.0`
  (build `T-NKM2DEUC-REL-202607211844`). Other Tizen 6.0 models are untested.
- **Developer Mode** on, with **Host PC IP = `127.0.0.1`** (this is what lets the
  TV talk to its own `sdbd`). Note: with that setting the PC's `sdb` cannot
  authorise, which is why the relay is the management path.
- **Samsung VD developer certificate** (author + distributor), plus its password.
  The generic Tizen Studio developer certificate is **rejected** by these TVs
  (`Check certificate error [-12]`). You need the Samsung VD / developer pair.
- **Tizen Studio** (`sdb`, `tz` CLI).
- **.NET SDK** able to target `netcoreapp3.1` (the TV ships .NET Core 3.1).
- **openssl** (`genpkey`, `pkey`, `dgst`).
- **Upstream** [`chris-ritsen/samsung-tv-root`](https://github.com/chris-ritsen/samsung-tv-root)
  built (`payloads/common31` → `SamsungTvArchiveRoot.dll`), which provides the
  archive-root escalation and the `SVE-2025-50109` injection idea.

---

## Build

1. Build the upstream archive-root payload:
   ```sh
   # in your samsung-tv-root clone
   make payloads        # or build payloads/common31 directly
   ```
2. Build the three managed assemblies (netcoreapp3.1). A simple `csproj` works;
   this repo's earlier environment used `csc` directly against a `netcoreapp3.1`
   reference pack:
   ```sh
   csc /noconfig /nostdlib+ /target:exe /langversion:8.0 /optimize+ \
       /out:com.selfroot.boot.dll \
       /reference:<refpack>/*.dll src/agent/Program.cs
   # same for stub (com.samsung.tv.ghservice.dll) and relay (RootRelay.dll)
   ```
   Each assembly needs a `*.runtimeconfig.json`:
   ```json
   {"runtimeOptions":{"tfm":"netcoreapp3.1","framework":{"name":"Microsoft.NETCore.App","version":"3.1.0"}}}
   ```
3. Generate the signed archive + key:
   ```sh
   PAYLOAD_DIR=path/to/samsung-tv-root/payloads/common31/out \
     python3 src/payload/build_artifacts.py
   # -> src/payload/art/{public.pem, passwd, <pkg>.tar.gz, <pkg>.tar.gz.signature,
   #      SamsungTvArchiveRoot.dll, SamsungTvArchiveRoot.runtimeconfig.json}
   ```
   `pinned-private.pem` is generated fresh here; **never commit it**.
4. Package + sign the TPK:
   ```sh
   python3 src/tpk/build_tpk.py          # -> src/tpk/com.selfroot.boot.tpk (unsigned)
   tz security-profiles add -n <name> -a <author.p12> -p <pw> -d <distributor.p12> -P <pw>
   tz pack -b src/tpk/com.selfroot.boot.tpk -o com.selfroot.boot-signed.tpk \
           -t tpk -s <name>
   ```

---

## Deploy (one-time, host-assisted)

The host is needed once, to install the app and place files. Afterwards the TV
roots itself at every boot.

1. Point Developer Mode Host PC IP at the PC, reboot, then:
   ```sh
   sdb connect <tv-ip>:26101
   sdb install com.selfroot.boot-signed.tpk
   ```
2. Stage the payload on the TV (`/opt/usr/share/selfroot/art/`, `/relay/RootRelay.dll`,
   `/token`, and the archive-root files), and squat the stub:
   ```sh
   # back up the original service app first
   cp -a /opt/usr/apps/com.samsung.tv.ghservice/bin <backup>/
   # place the stub DLL in the slot
   cp com.samsung.tv.ghservice.dll /opt/usr/apps/com.samsung.tv.ghservice/bin/
   ```
3. Mark the slot for boot and pin the dev IP back:
   ```sh
   sqlite3 /opt/dbspace/.pkgmgr_parser.db \
     "UPDATE package_app_info SET app_onboot='true' WHERE app_id='com.samsung.tv.ghservice';"
   ```
4. Set Developer Mode Host PC IP back to `127.0.0.1` and reboot.

After boot, the relay is on `<tv-ip>:26103`. Authenticate with the 32-byte token
from `/opt/usr/share/selfroot/token`, then send one shell command per line; each
reply ends with `[exit:N]`.

A minimal Python client:

```python
import socket, time
s = socket.create_connection(("<tv-ip>", 26103), timeout=10)
s.sendall(b"<32-byte-token>")     # 32 bytes, no newline
s.sendall(b"id -u\n")
time.sleep(1)
print(s.recv(65536).decode())
```

---

## Rollback

Restore the original service DLL, clear `app_onboot`, and reboot:

```sh
cp -a <backup>/bin/. /opt/usr/apps/com.samsung.tv.ghservice/bin/
sqlite3 /opt/dbspace/.pkgmgr_parser.db \
  "UPDATE package_app_info SET app_onboot='false' WHERE app_id='com.samsung.tv.ghservice';"
```

---

## Security & legal

- The relay listens on **all interfaces**; the token travels in cleartext. Treat
  `<tv-ip>:26103` as a root service: keep the token secret or firewall the port.
- Root is `uid 0` inside Tizen's SMACK policy (`User`), not an unrestricted
  kernel context; unsigned native ELF still cannot run (UEP).
- Firmware updates can overwrite the squatted service. Restore the original app
  before updating.
- Use only on devices you own or are authorised to examine. This project is not
  affiliated with Samsung. The archive-root route and injection originate from
  [`chris-ritsen/samsung-tv-root`](https://github.com/chris-ritsen/samsung-tv-root)
  (Unlicense); see that repository for its terms and the underlying
  `SVE-2025-50109` research.

## License

MIT (see `LICENSE`). Upstream `samsung-tv-root` is Unlicense; its payloads retain
their own terms.
