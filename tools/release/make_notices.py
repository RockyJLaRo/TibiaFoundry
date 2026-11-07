# Builds THIRD-PARTY-NOTICES.txt for a release from the license files in the local NuGet package
# cache (no downloads). Fails if a package of the build is not covered or a license file is missing.
# Run from the repository root after publishing the folder edition:
#   python -I tools/release/make_notices.py src <output file> [<published TibiaFoundry.deps.json>]
# The .NET runtime version is read from the deps.json of the published build (or, without it, it
# must be the only runtime pack version in the package cache).
import json, os, re, sys, zipfile

src, out = sys.argv[1], sys.argv[2]
nuget = os.path.join(os.path.expanduser("~"), ".nuget", "packages")

props = open(os.path.join(src, "Directory.Build.props"), encoding="utf-8").read()
version = re.search(r"<Version>([^<]+)</Version>", props).group(1)
assets = json.load(open(os.path.join(src, "TibiaFoundry.App", "obj", "project.assets.json"), encoding="utf-8"))
packages = sorted(k for k, v in assets["libraries"].items() if v.get("type") == "package")
if len(sys.argv) > 3:
    deps = json.load(open(sys.argv[3], encoding="utf-8-sig"))
    found = [k.split("/")[1] for target in deps["targets"].values() for k in target
             if k.lower().startswith("runtimepack.microsoft.netcore.app.runtime.win-x64/")]
    if not found:
        sys.exit(f"{sys.argv[3]} names no .NET runtime pack (is it a self-contained build?).")
    runtime = found[0]
    # What the published build ships (build-time packages such as the trimming tasks are not in it).
    packages = sorted(k for k, v in deps["libraries"].items() if v.get("type") == "package")
else:
    cached = os.listdir(os.path.join(nuget, "microsoft.netcore.app.runtime.win-x64"))
    if len(cached) != 1:
        sys.exit(f"Several runtime packs are cached ({', '.join(cached)}); pass the published deps.json.")
    runtime = cached[0]


def pkg_file(name, ver, rel):
    path = os.path.join(nuget, name.lower(), ver, rel)
    if not os.path.isfile(path):
        sys.exit(f"Missing license file: {path}")
    return open(path, encoding="utf-8-sig", errors="strict").read().replace("\r\n", "\n").strip()


def nupkg_file(name, ver, rel):
    path = os.path.join(nuget, name.lower(), ver, f"{name.lower()}.{ver}.nupkg")
    with zipfile.ZipFile(path) as z:
        return z.read(rel).decode("utf-8-sig").replace("\r\n", "\n").strip()


MIT = """Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES
OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE."""

BSD3 = """Redistribution and use in source and binary forms, with or without modification, are permitted
provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright notice, this list of
      conditions and the following disclaimer.
    * Redistributions in binary form must reproduce the above copyright notice, this list of
      conditions and the following disclaimer in the documentation and/or other materials provided
      with the distribution.
    * Neither the name of Google Inc. nor the names of its contributors may be used to endorse or
      promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER
IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF
THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE."""


def nuspec_copyright(name, ver):
    text = open(os.path.join(nuget, name.lower(), ver, f"{name.lower()}.nuspec"), encoding="utf-8-sig").read()
    m = re.search(r"<copyright>([^<]+)</copyright>", text)
    return m.group(1).strip() if m else None


sections = []
covered = set()


def add(title, license_name, body, *names):
    sections.append((title, license_name, body))
    covered.update(n.lower() for n in names)


def ver(name):
    return next(p.split("/")[1] for p in packages if p.split("/")[0].lower() == name.lower())


# .NET and WPF (self-contained builds include the runtime).
add(f".NET runtime {runtime} (Microsoft.NETCore.App)", "MIT",
    pkg_file("microsoft.netcore.app.runtime.win-x64", runtime, "LICENSE.TXT") + "\n\n--- Third-party notices of the .NET runtime ---\n\n" +
    pkg_file("microsoft.netcore.app.runtime.win-x64", runtime, "THIRD-PARTY-NOTICES.TXT"))
add(f"Windows Desktop runtime {runtime} (WPF, System.Drawing.Common)", "MIT",
    pkg_file("microsoft.windowsdesktop.app.runtime.win-x64", runtime, "LICENSE"))

# The Windows SDK projection (7.0-7.3: target framework net10.0-windows10.0.19041.0, for the text recognition of
# Item Discovery, removed in 8.0). It is a runtime pack, not a package, so it is looked up separately, and only
# listed when a build's deps file names it (8.0 builds do not include it).
sdk = next((k.split("/")[1] for k in deps["libraries"] if k.lower().startswith("runtimepack.microsoft.windows.sdk.net.ref/")), None) if len(sys.argv) > 3 else None
if sdk:
    add(f"Microsoft.Windows.SDK.NET {sdk} (the Windows SDK projection for .NET) and WinRT.Runtime (C#/WinRT)",
        "Microsoft Windows SDK license terms (Microsoft.Windows.SDK.NET); MIT (WinRT.Runtime)",
        f"{nuspec_copyright('Microsoft.Windows.SDK.NET.Ref', sdk)}\n\n"
        "Microsoft.Windows.SDK.NET.dll is the .NET projection of the Windows SDK, included by the .NET SDK in\n"
        "builds for Windows 10 target frameworks. It is distributed under the Microsoft Windows SDK license\n"
        "terms: https://aka.ms/WinSDKLicenseURL\n\n"
        "WinRT.Runtime.dll is part of C#/WinRT (https://github.com/microsoft/CsWinRT):\n\n"
        f"Copyright (c) Microsoft Corporation.\n\n{MIT}")

v = ver("Google.Protobuf")
add(f"Google.Protobuf {v}", "BSD-3-Clause", f"{nuspec_copyright('Google.Protobuf', v)}. All rights reserved.\n\n{BSD3}", "Google.Protobuf")

v = ver("Magick.NET-Q16-AnyCPU")
add(f"Magick.NET-Q16-AnyCPU and Magick.NET.Core {v}, with ImageMagick and the libraries it includes",
    "Apache-2.0 (Magick.NET), ImageMagick License and the licenses listed below",
    pkg_file("Magick.NET-Q16-AnyCPU", v, "Notice.txt"), "Magick.NET-Q16-AnyCPU", "Magick.NET.Core")

v = ver("NAudio.Core")
add(f"NAudio.Core and NAudio.WinMM {v}", "MIT", f"{nuspec_copyright('NAudio.Core', v)}\n\n{MIT}", "NAudio.Core", "NAudio.WinMM")
v = ver("NAudio.Vorbis")
add(f"NAudio.Vorbis {v}", "MIT", f"{nuspec_copyright('NAudio.Vorbis', v)}\n\n{MIT}", "NAudio.Vorbis")
v = ver("NVorbis")
add(f"NVorbis {v}", "MIT", nupkg_file("NVorbis", v, "LICENSE"), "NVorbis")
v = ver("Newtonsoft.Json")
add(f"Newtonsoft.Json {v}", "MIT", pkg_file("Newtonsoft.Json", v, "LICENSE.md"), "Newtonsoft.Json")
v = ver("CommunityToolkit.Mvvm")
add(f"CommunityToolkit.Mvvm {v}", "MIT",
    pkg_file("CommunityToolkit.Mvvm", v, "License.md") + "\n\n--- Third-party notices of CommunityToolkit.Mvvm ---\n\n" +
    pkg_file("CommunityToolkit.Mvvm", v, "ThirdPartyNotices.txt"), "CommunityToolkit.Mvvm")
v = ver("TagLibSharp")
lgpl_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "licenses", "LGPL-2.1.txt")
if not os.path.isfile(lgpl_path):
    sys.exit(f"Missing license file: {lgpl_path}")
add(f"TagLibSharp {v} (TagLib#)", "LGPL-2.1-only",
    f"{nuspec_copyright('TagLibSharp', v)}\n"
    "Source code: https://github.com/mono/taglib-sharp\n\n"
    "Tibia Foundry uses TagLib# unmodified. The folder edition ships it as TagLibSharp.dll; the portable\n"
    "edition bundles it into TibiaFoundry.exe. The complete source code of Tibia Foundry is published with\n"
    "each release (TibiaFoundry-<version>-source.zip), so it can be rebuilt with a modified TagLib#.\n\n" +
    open(lgpl_path, encoding="utf-8").read().replace("\r\n", "\n").strip(), "TagLibSharp")
# Asset Library (8.8): Microsoft.Data.Sqlite (MIT) over SQLitePCLRaw (Apache-2.0), which ships the SQLite library (public domain).
v = ver("Microsoft.Data.Sqlite.Core")
add(f"Microsoft.Data.Sqlite and Microsoft.Data.Sqlite.Core {v}", "MIT",
    f"{nuspec_copyright('Microsoft.Data.Sqlite.Core', v)}\n\n{MIT}", "Microsoft.Data.Sqlite", "Microsoft.Data.Sqlite.Core")
v = ver("SQLitePCLRaw.core")
apache_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "licenses", "Apache-2.0.txt")
if not os.path.isfile(apache_path):
    sys.exit(f"Missing license file: {apache_path}")
add(f"SQLitePCLRaw {v} (core, provider.e_sqlite3, lib.e_sqlite3, bundle_e_sqlite3)", "Apache-2.0",
    f"{nuspec_copyright('SQLitePCLRaw.core', v)}\nSource code: https://github.com/ericsink/SQLitePCL.raw\n\n" +
    open(apache_path, encoding="utf-8").read().replace("\r\n", "\n").strip(),
    "SQLitePCLRaw.core", "SQLitePCLRaw.provider.e_sqlite3", "SQLitePCLRaw.lib.e_sqlite3", "SQLitePCLRaw.bundle_e_sqlite3")
add("SQLite (e_sqlite3, the native library shipped by SQLitePCLRaw.lib.e_sqlite3)", "public domain",
    "SQLite is in the public domain (https://www.sqlite.org/copyright.html). The Asset Library stores its index\n"
    "with it; the library file (library.db) is an ordinary SQLite database on the user's computer.")
# No personal names here (docs/13 §3).
add("LZMA SDK", "public domain",
    "The sprite sheet decompressor is a port of the LZMA decoder of the LZMA SDK, which is in the public\n"
    "domain (https://www.7-zip.org/sdk.html). The compressor writes the same format.")

missing = [p for p in packages if p.split("/")[0].lower() not in covered]
if missing:
    sys.exit("Packages without a notice: " + ", ".join(missing))

lines = [
    f"Tibia Foundry {version} - third-party notices",
    "=" * 60,
    "",
    "Tibia Foundry includes the third-party software listed below. Each entry gives the component, its",
    "license and the license text or notice that comes with it.",
    "",
    "Tibia Foundry includes no code from Nekiro's RCC Editor or from Arch-Mina's ClientConverter and",
    "Assets Editor; those projects are acknowledged on the program's About page.",
    "",
    "Contents:",
]
lines += [f"  {i}. {t} - {l}" for i, (t, l, _) in enumerate(sections, 1)]
for i, (title, license_name, body) in enumerate(sections, 1):
    lines += ["", "", "-" * 100, f"{i}. {title}", f"License: {license_name}", "-" * 100, "", body]
text = "\n".join(lines).rstrip() + "\n"
with open(out, "w", encoding="utf-8", newline="\r\n") as f:
    f.write(text)
print(f"{out}: {len(sections)} sections, {len(text):,} characters; packages covered: {', '.join(packages)}; runtime {runtime}")
