# Fork additions: untethered root + UEP

This directory contains sanitised source and notes for three Tizen 6.0 areas:

| Path | What |
| --- | --- |
| [`untethered-root/`](untethered-root/) | Clean signed TPK that re-acquires root at boot, a `ghservice`-slot stub, the sdb/archive-root agent and the token-gated relay |
| [`uep/`](uep/) | Generic UEP/SFD state-word tool; the physical page and word offset are supplied at runtime |
| [`debloat/`](debloat/) | The applied app/unit debloat inventory, a `launch.sh` mask fragment, the optional TizenTube service fix and the launcher-gated Home-shell caution |

Reference device: `QE65QN95AATXXU` (2021 Neo QLED, `21_NIKEM2_QTV`),
`T-NKM2DEUC-2310.0` / build `T-NKM2DEUC-REL-202607211844`, Tizen 6.0.0,
ARMv7. Other models and firmware are untested. The UEP tool is generic: it does
not contain that device's physical page or word offset.

## What is intentionally not here

- Samsung firmware, extracted keys, or Samsung binaries.
- Developer certificates, private keys, passwords, relay tokens, signed tarballs,
  or TPK/WGT/DLL build outputs.
- Host IPs, the real relay address, device baselines, or logs. The debloat list is
  app/unit names only; it contains no device addresses or backup database.
- The platform-specific UEP physical page/offset. `uepctl` takes those as
  arguments so the repository is not a drop-in tuned for one firmware.

## Warnings

This is research tooling, not a supported Samsung feature. The root chain edits
a service slot, app-context database and boot flags; the UEP tool writes kernel
memory. Use only on hardware you own or are authorised to examine. Keep the
original service app, the stock values and a baseline off the TV before changing
anything. A firmware update can invalidate or overwrite all of it.

The upstream project is [`chris-ritsen/samsung-tv-root`](https://github.com/chris-ritsen/samsung-tv-root)
(Unlicense). These additions are provided under the MIT licence in
[`LICENSE`](LICENSE); the upstream files and payloads retain their own terms.
