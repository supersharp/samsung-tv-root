# Method — how untethered TPK-first root was achieved

This document records the reasoning and the dead ends, so the design is
reproducible and auditable. It complements the README.

## 1. Background: the upstream route

[`chris-ritsen/samsung-tv-root`](https://github.com/chris-ritsen/samsung-tv-root)
provides, among other things, an **archive-root** route that works on multiple
Samsung Tizen TVs without a model whitelist:

1. An **SDB foothold** via `SVE-2025-50109`: a package-name injection in the
   `appinstall` service that executes a shell command as `uid 901` (`sdk`).
2. A **capability-bearing `dotnet`** (`/usr/bin/dotnet`, `cap_setgid,cap_sys_admin`)
   running `SamsungTvArchiveRoot.dll`.
3. That payload `unshare(CLONE_NEWNS)`s, bind-mounts a chosen `public.pem` over the
   `sdbd` verification key and a chosen `passwd` over `/etc/passwd`, then hands a
   **signed `tar.gz`** to `sdbd-tarlauncher` (which has `cap_setuid`), extracting
   it `--to-command=/bin/sh` as `uid 0`.

The host drives this once per boot. Nothing is written to the read-only rootfs;
the bind mounts are namespace-local and vanish with the process.

## 2. The persistence problem

Upstream has no TV-side persistence. The goal was a TV that re-roots itself at
every boot with no host. Two facts bound the design space:

### 2.1 `app-boot-manager` ignores new app ids (Tizen 6.0)

The boot manager (`/usr/bin/app-boot-manager`, a packed native ELF) launches apps
at boot. Experimentally, on Tizen 6.0:

- The boot list is the set of `package_app_info.app_onboot='true'` rows **that the
  manager already knows about**. That set is the firmware image plus a few apps
  installed through Samsung's OTA path.
- Setting `app_onboot`, `app_autorestart`, `app_preload`, `package_preload`, the
  manifest `on-boot` attribute, the package install time, or adding the package to
  the preload list does **not** make a `pkgcmd`-installed app id boot.
- The discriminator is **not** `app_package_system` / `package_system` /
  `package_preload` (all `false` for every app, firmware and user alike), and it
  is not in `.app-context.db`, `.component.db`, or `.appsvc.db`. It lives in the
  packed boot-manager binary.

Conclusion: a **squat** of an already-booted Samsung service app is the only
reliable boot trigger. `com.samsung.tv.ghservice` (a `dotnet-inhouse` `svcapp`
that boots with `app_onboot='true'`) is used.

### 2.2 The squat slot cannot `exec()` subprocesses

The squatted app runs under a `User::Pkg` SMACK label. A first attempt made the
squat a *launcher* that ran the TPK via `Process.Start("/usr/bin/dotnet", …)`.
In the real app context this fails:

```
err Win32Exception: Permission denied
```

The existing agent works because it speaks sdb over **sockets** and never spawns
a child. So the squat slot can either run the sockets-based agent itself, or use
the app framework's own launch mechanism.

## 3. The `aul_launch_app` bridge

To keep the TPK authoritative and shrink the squat to a stub, the stub launches
the TPK through the app framework instead of `exec()`:

- Library: `libaul.so.0` (version observed on the test TV).
- Symbol: `aul_launch_app` (confirmed via `grep -a aul_launch_app
  /usr/lib/libaul.so.0` and the Tizen `aul.h` header).
- Signature: `int aul_launch_app(const char *appid, bundle *kb)`; `kb` may be
  `NULL`. The return value is the launched PID on success, negative on failure.

P/Invoke from the stub:

```csharp
[DllImport("libaul.so.0", EntryPoint = "aul_launch_app",
           CallingConvention = CallingConvention.Cdecl)]
static extern int aul_launch_app([MarshalAs(UnmanagedType.LPStr)] string appid, IntPtr bundle);
// aul_launch_app("com.selfroot.boot", IntPtr.Zero)
```

The stub retries a few times (the app framework may still be settling at boot),
then exits; the launched app is managed by `aul` and survives independently.

## 4. Staging-path hardening

The agent writes its launch script to `/tmp`. A fixed name
(`/tmp/selfroot-launch.sh`) collides when a previous run left a file owned by a
different uid (e.g. a root-context run), causing `Access to the path …
denied`. The agent therefore uses a **unique per-run path**:

```csharp
private static readonly string Script =
    "/tmp/selfroot-launch-" + Guid.NewGuid().ToString("N") + ".sh";
```

`/tmp` is tmpfs, so the files vanish on reboot; uniqueness only matters for
manual re-launches within one boot.

## 5. Certificate note

The Tizen Studio developer certificate is rejected by these TVs
(`Check certificate error [-12]`). A Samsung **VD developer** (author +
distributor) pair is required. The signing flow is:

```sh
tz security-profiles add -n <name> -a <author.p12> -p <pw> -d <distributor.p12> -P <pw>
tz pack -b <unsigned.tpk> -o <signed.tpk> -t tpk -s <name>
```

The `security-profiles add` step writes an encrypted `.pwd` next to the
profiles; keep those files out of version control.

## 6. Failure modes

- **Stub fails to launch the TPK** → TV boots unrooted. Recovery: set Developer
  Mode Host PC IP back to the PC, reboot, restore the original service DLL via
  `sdb`, then flip the IP back and reboot.
- **Relay dies** → TV reboot restarts it (the boot agent starts the relay).
- **Firmware update** → can overwrite the squatted service. Restore the original
  app first.
