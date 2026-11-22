#!/usr/bin/env python3
"""Package the built mod as the release zip the MikanXR launcher installs from.

The zip is MikanXR_Warudo_<version>.zip and holds two files at its root: the mod,
MikanXR.warudo, and plugin.json, the manifest that tells the launcher where the mod
goes inside a Warudo install. The version is the one MikanXRPlugin.cs declares.

Usage: python Tools/package_release.py [--out <dir>] [--allow-stale] [--publish]
  --out <dir>     the folder the zip is written to, Build/ by default
  --allow-stale   package a mod file older than the sources it is built from
  --publish       after packaging, draft a GitHub release for the tag v<version>
                  and attach the zip to it

The mod is built in the Unity editor (see Building in the README), which this script
does not run. It refuses a mod file that is not what the sources would build:

  - the version compiled into the mod differs from the one in MikanXRPlugin.cs
  - a file under Assets/MikanXR, or the export profile, is newer than the mod

--publish also needs a clean working tree and the tag v<version> on the checked out
commit and pushed. The release is a draft, so nothing is public until it is published
from the GitHub releases page or with gh release edit v<version> --draft=false.
"""

import argparse
import hashlib
import io
import json
import re
import subprocess
import sys
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
MOD_FILE = REPO / "Mod/MikanXR.warudo"
MOD_SOURCES = REPO / "Assets/MikanXR"
EXPORT_PROFILE = REPO / "Assets/ExportSettings.asset"
VERSION_FILE = MOD_SOURCES / "Plugin/MikanXRPlugin.cs"
RUNTIME_SOURCE = MOD_SOURCES / "Runtime/source.txt"

PLUGIN_ID = "mikanxr-warudo"
PLUGIN_HOST = "warudo"
MOD_NAME = "MikanXR.warudo"
MOD_DEST = "Warudo_Data/StreamingAssets/Plugins/MikanXR.warudo"
MANIFEST_NAME = "plugin.json"

# The mod file is a zip behind a short header, and this entry holds its assemblies.
MOD_MAGIC = b"UMOD"
MOD_ASSEMBLIES = "assemblymodules.dat"


class PackageError(Exception):
    """A reason the release cannot be packaged or published, worded for the console."""


def source_version():
    """The version MikanXRPlugin.cs declares in its PluginType attribute."""
    text = VERSION_FILE.read_text(encoding="utf-8", errors="replace")
    found = re.search(r'Version\s*=\s*"([^"]+)"', text)

    if not found:
        raise PackageError(f'no Version = "..." in {VERSION_FILE.relative_to(REPO)}')

    version = found.group(1)

    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise PackageError(f'the version "{version}" is not of the form 1.2.3')

    return version


def compiled_versions(mod_bytes):
    """The versions the mod's assemblies declare. An attribute stores a string property
    as its type (0x0E), then its name and its value, each a length byte and UTF-8."""
    if not mod_bytes.startswith(MOD_MAGIC):
        raise PackageError(f"{MOD_FILE.relative_to(REPO)} is not a Warudo mod file")

    try:
        with zipfile.ZipFile(io.BytesIO(mod_bytes)) as mod:
            assemblies = mod.read(MOD_ASSEMBLIES)
    except (zipfile.BadZipFile, KeyError) as error:
        raise PackageError(f"cannot read the assemblies in the mod file: {error}") from error

    versions = set()

    for found in re.finditer(rb"\x0e\x07Version", assemblies):
        length = assemblies[found.end()]
        value = assemblies[found.end() + 1 : found.end() + 1 + length]
        versions.add(value.decode("utf-8", errors="replace"))

    return versions


def newer_sources(mod_time):
    """The mod's source files changed after the mod file was written."""
    sources = [path for path in MOD_SOURCES.rglob("*") if path.is_file()]
    sources.append(EXPORT_PROFILE)

    return sorted(path for path in sources if path.stat().st_mtime > mod_time)


def check_mod(version, allow_stale):
    """Checks the mod file against the sources and returns its bytes."""
    if not MOD_FILE.is_file():
        raise PackageError(
            f"{MOD_FILE.relative_to(REPO)} is missing. Build the mod first, see Building in the README."
        )

    mod_bytes = MOD_FILE.read_bytes()
    compiled = compiled_versions(mod_bytes)

    if compiled != {version}:
        found = ", ".join(sorted(compiled)) or "no version"
        raise PackageError(
            f"the mod file was compiled with {found}, and MikanXRPlugin.cs says {version}. Rebuild the mod."
        )

    newer = newer_sources(MOD_FILE.stat().st_mtime)

    if newer:
        listed = "\n".join(f"  {path.relative_to(REPO).as_posix()}" for path in newer[:10])
        more = f"\n  and {len(newer) - 10} more" if len(newer) > 10 else ""
        message = f"these sources are newer than the mod file:\n{listed}{more}"

        if not allow_stale:
            raise PackageError(f"{message}\nRebuild the mod, or pass --allow-stale.")

        print(f"Warning: {message}")

    return mod_bytes


def manifest_for(version):
    """The plugin.json the launcher reads, as the MikanLauncher README defines it."""
    return {
        "id": PLUGIN_ID,
        "version": version,
        "host": PLUGIN_HOST,
        "files": [{"src": MOD_NAME, "dest": MOD_DEST}],
    }


def write_zip(archive, manifest, mod_bytes):
    """Writes the zip, then reads it back and checks it holds exactly what it should."""
    archive.parent.mkdir(parents=True, exist_ok=True)

    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as package:
        package.writestr(MANIFEST_NAME, json.dumps(manifest, indent=4) + "\n")
        package.writestr(MOD_NAME, mod_bytes)

    with zipfile.ZipFile(archive) as package:
        names = package.namelist()

        if names != [MANIFEST_NAME, MOD_NAME]:
            raise PackageError(f"the zip holds {names}")

        if json.loads(package.read(MANIFEST_NAME)) != manifest:
            raise PackageError("the zip's plugin.json does not read back as written")

        for entry in manifest["files"]:
            if entry["src"] not in names:
                raise PackageError(f"plugin.json names {entry['src']}, which the zip lacks")

        packed = hashlib.sha256(package.read(MOD_NAME)).hexdigest()

    built = hashlib.sha256(mod_bytes).hexdigest()

    if packed != built:
        raise PackageError("the mod in the zip does not hash the same as the built mod file")

    return built


def git(*arguments):
    """Runs git in the repository and returns its output, or None when it fails."""
    ran = subprocess.run(["git", *arguments], cwd=REPO, capture_output=True, text=True, check=False)

    return ran.stdout.strip() if ran.returncode == 0 else None


def check_tag(tag):
    """Checks that the release would be cut from the commit the tag names."""
    if git("status", "--porcelain") != "":
        raise PackageError("the working tree has uncommitted changes")

    head = git("rev-parse", "HEAD")
    tagged = git("rev-parse", "--verify", "--quiet", f"refs/tags/{tag}^{{commit}}")

    if tagged is None:
        raise PackageError(f"there is no tag {tag}. Tag the release commit: git tag {tag}")

    if tagged != head:
        raise PackageError(f"the tag {tag} is not on the checked out commit")

    pushed = git("ls-remote", "--tags", "origin", f"refs/tags/{tag}", f"refs/tags/{tag}^{{}}")

    if pushed is None:
        raise PackageError("cannot reach origin to check the tag")

    if head not in pushed.split():
        raise PackageError(f"the tag {tag} is not on origin at this commit. Push it: git push origin {tag}")


def publish(tag, archive):
    """Drafts the GitHub release for the tag and attaches the zip."""
    ran = subprocess.run(
        [
            "gh", "release", "create", tag, str(archive),
            "--draft", "--verify-tag", "--generate-notes",
            "--title", f"MikanXR Warudo Plugin {tag}",
        ],
        cwd=REPO,
        check=False,
    )

    if ran.returncode != 0:
        raise PackageError("gh release create failed")


def main():
    parser = argparse.ArgumentParser(description="Package the built mod as a release zip.")
    parser.add_argument("--out", type=Path, default=REPO / "Build")
    parser.add_argument("--allow-stale", action="store_true")
    parser.add_argument("--publish", action="store_true")
    arguments = parser.parse_args()

    try:
        if arguments.publish and arguments.allow_stale:
            raise PackageError("a stale mod file cannot be published")

        version = source_version()
        tag = f"v{version}"

        if arguments.publish:
            check_tag(tag)

        mod_bytes = check_mod(version, arguments.allow_stale)
        archive = arguments.out.resolve() / f"MikanXR_Warudo_{version}.zip"
        digest = write_zip(archive, manifest_for(version), mod_bytes)

        print(f"Version:  {version} (source and compiled mod agree)")

        if RUNTIME_SOURCE.is_file():
            print(f"Runtime:  {RUNTIME_SOURCE.read_text(encoding='utf-8', errors='replace').strip()}")

        print(f"Mod:      {MOD_FILE.relative_to(REPO).as_posix()} sha256 {digest}")
        print(f"Zip:      {archive}")

        if arguments.publish:
            publish(tag, archive)
            print(f"Drafted the release {tag}. Publish it from the releases page when it reads right.")
        else:
            print(f"Tag:      {tag} (pass --publish to draft the release)")
    except PackageError as error:
        print(f"Error: {error}", file=sys.stderr)

        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
