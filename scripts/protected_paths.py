"""
Protected-path guard: records and verifies that paths which a release must never touch stay byte-identical.

    python -I scripts/protected_paths.py snapshot <repo> <baseline.json>
    python -I scripts/protected_paths.py verify   <repo> <baseline.json>

Protected (both in the GitHub Pages repository root and, where they exist, in this source tree):
games/, pokemon/, tools/, .github/, CNAME, robots.txt.

The snapshot stores, per path: the git object id at HEAD (when the repository is a git checkout), the number of
files and a SHA-256 of every file in the working tree. verify recomputes everything, also lists uncommitted git
changes under the paths, and exits 1 on any difference (added, removed, changed file, different HEAD object).
"""
import hashlib
import json
import os
import subprocess
import sys

PROTECTED = ["games", "pokemon", "tools", ".github", "CNAME", "robots.txt"]


def git(repo, *args):
    r = subprocess.run(["git", "-C", repo, *args], capture_output=True, text=True)
    return r.stdout.strip() if r.returncode == 0 else None


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def state(repo):
    out = {}
    for p in PROTECTED:
        full = os.path.join(repo, p)
        if not os.path.exists(full):
            out[p] = {"exists": False}
            continue
        files = {}
        if os.path.isdir(full):
            for d, ds, fs in os.walk(full):
                ds[:] = [x for x in ds if x not in ("__pycache__",)]
                for f in fs:
                    fp = os.path.join(d, f)
                    files[os.path.relpath(fp, repo).replace(os.sep, "/")] = sha256(fp)
        else:
            files[p] = sha256(full)
        out[p] = {"exists": True, "head": git(repo, "rev-parse", f"HEAD:{p}"), "count": len(files), "files": files}
    return out


def main():
    if len(sys.argv) != 4 or sys.argv[1] not in ("snapshot", "verify"):
        sys.exit(__doc__)
    cmd, repo, baseline = sys.argv[1], os.path.abspath(sys.argv[2]), sys.argv[3]
    now = state(repo)
    if cmd == "snapshot":
        os.makedirs(os.path.dirname(os.path.abspath(baseline)), exist_ok=True)
        json.dump({"repo": repo, "head": git(repo, "rev-parse", "HEAD"), "paths": now}, open(baseline, "w", encoding="utf-8"), indent=1)
        for p, s in now.items():
            print(f"{p:12} " + (f"{s['count']:5} files  HEAD {s['head']}" if s["exists"] else "absent"))
        return
    base = json.load(open(baseline, encoding="utf-8"))["paths"]
    problems = []
    for p in PROTECTED:
        b, n = base.get(p, {"exists": False}), now[p]
        if b["exists"] != n["exists"]:
            problems.append(f"{p}: {'appeared' if n['exists'] else 'disappeared'}")
            continue
        if not n["exists"]:
            continue
        if b.get("head") != n.get("head"):
            problems.append(f"{p}: committed content changed (HEAD object {b.get('head')} -> {n.get('head')})")
        bf, nf = b["files"], n["files"]
        problems += [f"removed: {f}" for f in sorted(set(bf) - set(nf))]
        problems += [f"added: {f}" for f in sorted(set(nf) - set(bf))]
        problems += [f"changed: {f}" for f in sorted(set(bf) & set(nf)) if bf[f] != nf[f]]
    status = git(repo, "status", "--porcelain", "--", *[p for p in PROTECTED if now[p]["exists"]])
    if status:
        problems += ["uncommitted: " + line for line in status.splitlines()]
    for p, s in now.items():
        print(f"{p:12} " + (f"{s['count']:5} files" if s["exists"] else "absent"))
    if problems:
        print("PROTECTED PATHS CHANGED:\n  " + "\n  ".join(problems))
        sys.exit(1)
    print(f"OK: all protected paths unchanged in {repo}")


if __name__ == "__main__":
    main()
