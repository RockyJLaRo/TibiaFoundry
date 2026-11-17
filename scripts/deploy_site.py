"""
Copies the built website (website/site) into a checkout of the GitHub Pages repository (branch main) and stages the
change - it never commits or pushes:

    python -I scripts/deploy_site.py <pages repository checkout> [--dry-run]

* Protected paths of the Pages repository are never written, removed or staged: games/, pokemon/, tools/, .github/,
  CNAME, robots.txt (and the repository's own .gitignore, README*, LICENSE*). website/site's CNAME and robots.txt are not
  copied.
* Files of the previous site that the new site no longer has are removed (git rm); everything else is copied over.
* Afterwards: git status of the protected paths must be empty, otherwise the script stops with an error.
"""
import os
import shutil
import subprocess
import sys

PROTECTED = ("games/", "pokemon/", "tools/", ".github/", "CNAME", "robots.txt", ".gitignore", "README", "LICENSE")
ROOT = os.getcwd()
SITE = os.path.join(ROOT, "website", "site")


def git(repo, *args):
    return subprocess.run(["git", "-C", repo, *args], capture_output=True, text=True, check=True).stdout


def protected(path):
    return path.startswith(PROTECTED)


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    repo = os.path.abspath(sys.argv[1])
    dry = "--dry-run" in sys.argv[2:]
    if git(repo, "rev-parse", "--abbrev-ref", "HEAD").strip() != "main":
        sys.exit("The Pages repository checkout must be on main.")
    tracked = [f for f in git(repo, "ls-files", "-z").split("\0") if f]
    new = []
    for base, _, files in os.walk(SITE):
        for f in files:
            rel = os.path.relpath(os.path.join(base, f), SITE).replace(os.sep, "/")
            if not protected(rel):
                new.append(rel)
    removed = [f for f in tracked if not protected(f) and f not in new]
    added = [f for f in new if f not in tracked]
    changed = [f for f in new if f in tracked and open(os.path.join(SITE, f), "rb").read() != open(os.path.join(repo, f), "rb").read()]
    print(f"site files {len(new)}: {len(added)} new, {len(changed)} changed, {len(removed)} removed; "
          f"protected paths untouched: {', '.join(PROTECTED[:6])}")
    if dry:
        for title, items in (("new", added), ("changed", changed), ("removed", removed)):
            print(f"  {title}: " + ", ".join(items[:12]) + (" ..." if len(items) > 12 else ""))
        return
    for f in removed:
        git(repo, "rm", "-q", "--", f)
    for f in new:
        dst = os.path.join(repo, f)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copyfile(os.path.join(SITE, f), dst)
    git(repo, "add", "--", *sorted({f.split("/")[0] for f in new}))
    touched = git(repo, "status", "--porcelain", "--", "games", "pokemon", "tools", ".github", "CNAME", "robots.txt")
    if touched.strip():
        sys.exit("PROTECTED PATHS CHANGED - stop:\n" + touched)
    print("Staged in " + repo + " (not committed). Review with: git -C <repo> status")


if __name__ == "__main__":
    main()
