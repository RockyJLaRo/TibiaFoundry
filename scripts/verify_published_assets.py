"""
After the GitHub release is published: downloads every asset from its canonical URL and compares it with the local
release folder (run from the repository root):

    python -I scripts/verify_published_assets.py

https://github.com/RockyJLaRo/TibiaFoundry/releases/download/v<version>/<file> for the three desktop archives (the
website's download buttons), the Website archive and SHA256SUMS.txt. Each must answer 200, have the local file's exact
size and SHA-256. Exit code 1 otherwise - then the website must not be deployed yet.
"""
import hashlib
import os
import re
import sys
import urllib.request

ROOT = os.getcwd()
VERSION = re.search(r"<Version>([^<]+)</Version>", open(os.path.join(ROOT, "src", "Directory.Build.props"), encoding="utf-8").read()).group(1)
LOCAL = os.path.join(ROOT, "Release", VERSION)
BASE = f"https://github.com/RockyJLaRo/TibiaFoundry/releases/download/v{VERSION}/"
FILES = [f"TibiaFoundry-{VERSION}-MSI.zip", f"TibiaFoundry-{VERSION}-Portable.zip", f"TibiaFoundry-{VERSION}-Source.zip",
         f"TibiaFoundry-{VERSION}-Website.zip", "SHA256SUMS.txt"]


def main():
    bad = 0
    for name in FILES:
        local = os.path.join(LOCAL, name)
        want = hashlib.sha256(open(local, "rb").read()).hexdigest()
        try:
            req = urllib.request.Request(BASE + name, headers={"User-Agent": "TibiaFoundry-release-check"})
            with urllib.request.urlopen(req, timeout=300) as r:
                data = r.read()
                status = r.status
        except Exception as e:  # noqa: BLE001 - reported
            print(f"FAIL  {name}: {e}")
            bad += 1
            continue
        got = hashlib.sha256(data).hexdigest()
        good = status == 200 and got == want and len(data) == os.path.getsize(local)
        bad += not good
        print(f"{'PASS' if good else 'FAIL'}  {name}: HTTP {status}, {len(data):,} bytes, sha256 {got}{'' if got == want else ' (local ' + want + ')'}")
    print("OK: the published assets are the local release files" if not bad else f"FAILED: {bad} asset(s)")
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
