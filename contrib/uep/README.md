# UEP / SFD state-word tool

Sanitised extract of the UEP (Unsigned Executable Protection, also called SFD)
runtime state-word control used on a Samsung Tizen 6.0 TV.

This is **one 32-bit word** in kernel RAM, reached through the `/dev/sdp_hwmem`
physical-memory mapping. It is not a kernel image patch and it does not persist:
the stock value returns on the next boot unless something re-applies it.

The physical page and word offset are **platform and firmware specific**. They
are deliberately not hard-coded here. Supply them with `--page` and `--offset`;
an incorrect address can crash or corrupt the TV.

> Reference device: `QE65QN95AATXXU` (2021 Neo QLED, `21_NIKEM2_QTV`),
> `T-NKM2DEUC-2310.0` / build `T-NKM2DEUC-REL-202607211844`. The exact UEP
> address is intentionally omitted from this public copy.

## What it changes

| Field | Meaning |
| --- | --- |
| UEP/SFD state word | 32-bit kernel value controlling unsigned-executable enforcement |
| `0` | UEP disabled (unsigned native ELF allowed) |
| `1` | UEP enabled (stock) |
| Lifetime | Volatile RAM; a reboot restores the stock value |
| Scope | One page mapped, one word read or written |

`/dev/sdp_hwmem` exposes an unvalidated physical-memory `mmap`; the tool maps
exactly one page and touches one 4-byte word at the supplied offset. It does not
patch the kernel, forge a signature, or change any file on the TV.

## Build

```sh
dotnet build -c Release
# -> bin/Release/netcoreapp3.1/uepctl.dll + uepctl.runtimeconfig.json
```

The TV ships .NET Core 3.1 and runs the IL-only tool through Samsung's signed
`/usr/bin/dotnet`, so UEP does not block the tool itself. Copy the DLL and its
runtimeconfig to the TV (for example `/tmp/uepctl/`).

## Use

Run as root (through the untethered-root relay or another root shell):

```sh
/usr/bin/dotnet uepctl.dll status  --page <physical-page> --offset <word-offset>
/usr/bin/dotnet uepctl.dll disable --page <physical-page> --offset <word-offset>
/usr/bin/dotnet uepctl.dll enable  --page <physical-page> --offset <word-offset>
```

`--page` must be a positive, 4 KB aligned physical address. `--offset` must be
4-byte aligned and in `0..0xffc`. Both accept `0x` hex or decimal.

`status` is read-only. `disable` writes `0`; `enable` writes `1`; both read back
and verify the value. The aliases are `status`/`s`, `disable`/`off`/`patch`, and
`enable`/`on`/`revert`.

Example shape only — **not real values**:

```sh
# Replace <page> and <offset> with values from your own firmware analysis.
/usr/bin/dotnet uepctl.dll status --page 0x<page> --offset 0x<offset>
```

## Safety

- **Wrong address:** do not guess the page/offset. Derive them from the exact
  firmware and verify by reading the stock value before any write.
- **One word only:** do not extend this tool to arbitrary physical writes. The
  same primitive can corrupt kernel memory.
- **Root required:** `/dev/sdp_hwmem` is root-only; `uid 0` inside Tizen's SMACK
  policy is sufficient.
- **Volatile:** a reboot restores UEP. If a boot chain re-applies the disable,
  the explicit `enable` lasts only until that boot step runs again.
- **Legal:** use only on hardware you own or are authorised to examine. This is
  not affiliated with Samsung.

## License

MIT. See [../LICENSE](../LICENSE).
