# Storage cleanup — what was removed and what it saved

The reference TV's writable partition is `/dev/mmcblk0p21` mounted at `/opt`
(3.9 GB). At the start of this work it held 3.1 GB and had 819 MB free (80 %
used). Two cleanup passes took it to **1.3 GB used / 2.7 GB free (33 %)** —
about **1.8 GB freed**.

| | Used | Free |
| --- | ---: | ---: |
| Before | 3.1 GB | 819 MB |
| After both passes | 1.3 GB | 2.7 GB |

These are `df -h /opt` readings on the QN95A / Tizen 6.0 reference set. Sizes are
approximate; `du` reports 1 MiB kilobytes.

---

## Pass 1 — factory media (≈919 MB)

Removed after an off-TV SHA-256-verified copy. The backup is not part of this
repository.

| File | Source | Size | Why it was safe |
| --- | --- | ---: | --- |
| `NQ4K_220331.mp4` | `/opt/data/demo_movie/` | 737 MB | Retail/Shop-mode demo movie; no process or service references it in normal use |
| `4k.ts` + `4K.jpg` + `8K.jpg` | `/opt/data/VideoTest/videos/` | 82 MB | Factory test clip and stills |
| `OOBE_QLED_4K.mp4` | `/opt/data/Ending_Movie/` | 62 MB | First-setup/OOBE ending clip |
| `com.samsung.tv.tranquilnature-tpl-1.0.4.tpk` | `preinstall/3202006021119/` | 38 MB | Superseded by `1.0.5` in the same directory |

The directories were kept; only the files above were deleted. Total
**962,733,827 bytes**.

## Pass 2 — unused apps, installers and data (≈936 MB)

With `/opt` at 1.7 GB free, nine preinstalled but unused packages were
uninstalled with `pkgcmd -u` (payloads removed, not merely `app_disable`):

| Package | Name | Version | App dir |
| --- | --- | --- | ---: |
| `Oxv5Tb6i2d` | ePop-2021-Oscar | 1.19.0 | 50 MB |
| `com.samsung.tv.alexa-client` | alexa-fullscreen-app | 21.026.09210 | 43 MB |
| `com.samsung.tv.coba.healthview` | Samsung Health View | 2.25.06131 | 20 MB |
| `LzU5yAFXeg` | e-Manual | 8.0.0 | 124 MB |
| `com.samsung.tv.vibe-tpl` | Vibe | 0.0.27 | 104 MB |
| `com.samsung.tv.tranquilnature-tpl` | Tranquil Nature | 1.0.6 | 39 MB |
| `com.samsung.tv.mountainview-tpl` | Mountain View | 0.0.12 | 27 MB |
| `com.samsung.tv.cinemagraph-relaxation` | Cozy Room | 1.0.33 | 19 MB |
| `org.tizen.tts-engine-vd-DVB` | TTS engine | 1.2.37 | 117 MB |

**Subtotal ≈543 MB.**

Four superseded preinstall TPKs were also deleted (**≈165 MB**): Vibe 0.0.26,
Tranquil Nature 1.0.5, Mountain View 0.0.12, Cozy Room 1.0.21. The installed
versions above were newer than three of them. `preinstall` went from 260 MB /
11 TPKs to 90.7 MB / 7.

Data caches were emptied without removing directory skeletons or `res` symlinks
(**≈227 MB**):

| Data | Before | After |
| --- | ---: | ---: |
| `apps_rw/com.samsung.tv.slive-daemon` | 57 MB | 1.2 MB |
| `apps_rw/org.tizen.ambientapp` | 44 MB | 0.5 MB |
| `apps_rw/com.samsung.tv.ondevice-voice` | 20 MB | 0.02 MB |
| `apps_rw/com.samsung.tv.csfs` (Home images) | 118 MB | 11 MB |

Samsung Home was stopped before its `shared/trusted/data/image/fsdm` cache was
cleared; it re-downloads artwork when opened, so the 105 MB figure may partly
return. The other caches repopulate only if those apps run again.

## What was deliberately left alone

- Active Chromium engine image, `DynamicFilter`, `canlysis-service`,
  `lib-ariafw-tv` — user-facing/framework payloads.
- The remaining 90.7 MB of preinstall TPKs — factory-reset source.
- `/opt/usr/share/selfroot` and `selfroot-backup` — relay/token and the
  package-DB/archive revert path.
- `com.samsung.tv.cobalt` data — YouTube login/state, not a cache.
- The rotating selfroot logs (≈2.5 MB, two-file rotation) and `/opt/var/log`
  (≈4.4 MB, journal empty).

## Method and caveats

- Uninstalls used `pkgcmd -u -n <pkgid>`, never `rm` inside a package's `res/`
  directory, so the package DB and signatures stayed consistent.
- A factory reset after these deletions will not reinstall the four ambient
  templates whose TPKs were removed.
- Uninstalling the TTS engine disables text-to-speech/voice guide until it is
  restored via firmware; it is not a store app.
- `df` reflects the change immediately; no reboot is required.
