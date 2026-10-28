# Samsung TV Root — Tizen 6.0 untethered fork

A fork of [chris-ritsen/samsung-tv-root](https://github.com/chris-ritsen/samsung-tv-root)
(Unlicense). Upstream provides host-side root and control tooling for several
Samsung Tizen TVs. This fork keeps that tree and adds sanitised TV-side
untethered-root and UEP tooling for the tested Tizen 6.0 device.

Fork base: upstream `master` at `3b97edf` ("Handle cold SDB probes and frozen
OpenSSL"). Upstream usage, compatibility tables and release notes remain in the
[upstream README](https://github.com/chris-ritsen/samsung-tv-root#readme).

## Tested setup

| | |
| --- | --- |
| Model | `QE65QN95AATXXU` (2021 Neo QLED, platform `21_NIKEM2_QTV`) |
| Firmware | `T-NKM2DEUC-2310.0`, build `T-NKM2DEUC-REL-202607211844` (2026-07-21) |
| OS | Tizen 6.0.0, ARMv7 / 32-bit |
| Web engine | Chromium 110.0.5415.0 / 120.0.6099.5 (`chromium-efl 0.3.63120`); stock 1.1.76 preload untouched |
| Boot chain | `com.samsung.tv.ghservice` stub → `com.selfroot.boot` TPK → archive-root → token-gated relay |
| UEP | volatile state-word disable; a reboot restores stock |

No compatibility claim for any other model or firmware. The UEP physical page
and word offset are platform-specific and are not included in this repository.

## What this fork adds

- **Untethered boot chain** — after a one-time host-assisted install, the TV
  reacquires root at boot without a host computer, USB stick or developer-IP
  flip.
- **Clean signed TPK** — the payload is a normal, independently signed Tizen
  app; the squatted service slot is reduced to a small launcher stub.
- **Token-gated relay** — a root command relay on the TV, one shell command per
  line, intended for LAN management.
- **Generic UEP tool** — `uepctl` reads or flips the UEP/SFD state word; the
  physical page and offset are supplied as arguments, so no device address is
  baked into the source.
- **Sanitised sources** — no Samsung firmware or binaries, developer
  certificates, private keys, relay tokens, device baselines, LAN addresses or
  built TPK/WGT/DLL payloads.
- **Upstream preserved** — the upstream `src/`, `payloads/`, `docs/`, `tools/`
  and `swu/` trees are not replaced; the additions live under `contrib/`.

## Upstream vs this fork

| Area | Upstream | This fork |
| --- | --- | --- |
| Root persistence | Host controller reacquires after boot; no TV-side boot service | TV-side boot chain reacquires at boot after a one-time install |
| Boot trigger | Host-driven root session | `ghservice` service-slot stub launches a clean TPK |
| UEP control | Host-session status/disable controls | Standalone generic `uepctl`; page/offset at runtime |
| Tested target | QN90B, QN90F and other reported models | QN95A on Tizen 6.0 |
| Scope | Root, control, SWU and host tooling | Additions under `contrib/`; upstream code unchanged |

## Layout

- [`contrib/README.md`](contrib/README.md) — what is included, what was
  removed, warnings and licensing.
- [`contrib/untethered-root/`](contrib/untethered-root/) — agent, stub, relay,
  archive payload and TPK build sources, plus the method notes.
- [`contrib/uep/`](contrib/uep/) — `uepctl` source, build file and runbook.
- Upstream `src/`, `payloads/`, `docs/`, `tools/`, `swu/`, `tests/` — retained
  from upstream for the host-side root/control tooling.

## Start here

The additions are research tooling, not a supported Samsung feature. Read
[`contrib/README.md`](contrib/README.md) before building anything.

- Untethered root build/deploy: [`contrib/untethered-root/README.md`](contrib/untethered-root/README.md).
- UEP tool: [`contrib/uep/README.md`](contrib/uep/README.md).
- Upstream host tooling: [upstream README](https://github.com/chris-ritsen/samsung-tv-root#readme)
  and [QUICKSTART.md](QUICKSTART.md).

The untethered build needs a Samsung VD developer certificate, Tizen Studio,
a .NET Core 3.1-capable build and the upstream archive-root payload. The UEP
tool needs root and the platform-specific page/offset; it is not a drop-in
address for another firmware.

**Generating the certificate to sign the package and getting the SDB binary:**
- Download and install the Tizen Studio Baseline: https://download.tizen.org/sdk/Installer/Latest/
- Open Package Manager and install the certificate extensions.
- Open Certificate Manager and create a Samsung Certificate profile
- Keep defaults and sign in with your Samsung Account, you will need to enter your TV's Unique ID during the creation.

## Safety

- Use only on hardware you own or are authorised to examine. Root and UEP
  changes remove platform protections; a wrong UEP write can crash or corrupt
  the TV.
- The root chain edits a Samsung service slot, the app-context database and
  boot values. Keep the original service app and a baseline off the TV.
- The UEP state word is volatile; a reboot restores stock enforcement unless a
  boot step re-applies the patch.
- A firmware update can overwrite or invalidate the root chain and change the
  UEP layout. Re-check the exact firmware before reusing these notes.
- This project is not affiliated with Samsung. No warranty; use at your own
  risk.

## License

Upstream `samsung-tv-root` is Unlicense. The additions under `contrib/` are MIT;
see [`contrib/LICENSE`](contrib/LICENSE). Upstream payloads and files retain
their own terms.
