"""Package the boot agent DLL into an unsigned Tizen TPK.

The TPK is signed afterwards with `tz pack` using your own Samsung VD developer
certificate (see README). Run from anywhere; paths are relative to this file.
"""
import json
import os
import pathlib
import shutil
import zipfile

HERE = pathlib.Path(__file__).resolve().parent
PKG = HERE / "build" / "pkg"
BIN = PKG / "bin"
DLL = HERE.parent / "agent" / "bin" / "com.selfroot.boot.dll"
RUNTIMECONFIG = HERE.parent / "agent" / "bin" / "com.selfroot.boot.runtimeconfig.json"

if PKG.exists():
    shutil.rmtree(PKG)
BIN.mkdir(parents=True)

shutil.copyfile(DLL, BIN / "com.selfroot.boot.dll")
shutil.copyfile(RUNTIMECONFIG, BIN / "com.selfroot.boot.runtimeconfig.json")

deps = {
    "runtimeTarget": {"name": ".NETCoreApp,Version=v3.1", "signature": ""},
    "compilationOptions": {},
    "targets": {
        ".NETCoreApp,Version=v3.1": {
            "com.selfroot.boot/1.0.0": {"runtime": {"com.selfroot.boot.dll": {}}}
        }
    },
    "libraries": {
        "com.selfroot.boot/1.0.0": {"type": "project", "serviceable": False, "sha512": ""}
    },
}
(PKG / "com.selfroot.boot.deps.json").write_text(json.dumps(deps, indent=2), encoding="utf-8")

out = HERE / "com.selfroot.boot.tpk"
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for name in ("res/", "lib/", "shared/", "shared/lib/", "shared/res/"):
        z.writestr(zipfile.ZipInfo(name), b"")
    for d, _, fs in os.walk(PKG):
        for name in fs:
            rel = os.path.relpath(os.path.join(d, name), PKG).replace(os.sep, "/")
            z.write(os.path.join(d, name), rel)
print(out, os.path.getsize(out))
