#!/usr/bin/env bash
set -euo pipefail

# #49: check that the symbol packages a build produces let a consumer navigate the matching source revision.
# Every PDB in every .snupkg must carry Source Link JSON whose URLs point at this exact commit in this repository,
# and its documents must be repository-relative, so the PDBs are deterministic and do not name the build machine.
#
# usage: verify-sourcelink.sh <directory holding the .nupkg and .snupkg files> <version> [expected commit]
# The commit defaults to HEAD, so the script also works outside CI. Needs the sourcelink tool on PATH:
#   dotnet tool install --global sourcelink

artifacts=${1:?usage: verify-sourcelink.sh <directory holding the .nupkg and .snupkg files> <version> [expected commit]}
version=${2:?usage: verify-sourcelink.sh <directory holding the .nupkg and .snupkg files> <version> [expected commit]}
repository=m-vaysman/recdiffy.net
expected=${3:-$(git rev-parse HEAD)}

command -v sourcelink > /dev/null || { echo "::error::sourcelink is not on PATH. Install it with: dotnet tool install --global sourcelink"; exit 1; }

failures=0
fail() {
  echo "::error::$1"
  failures=$((failures + 1))
}

expected_url="https://raw.githubusercontent.com/$repository/$expected/"

for package in RecDiffy RecDiffy.RulePacks.Trading RecDiffy.IO RecDiffy.Excel; do
  nupkg="$artifacts/$package.$version.nupkg"
  snupkg="$artifacts/$package.$version.snupkg"
  [[ -s $nupkg ]] || fail "$package $version made no .nupkg."
  [[ -s $snupkg ]] || { fail "$package $version made no .snupkg, so a consumer gets no PDBs and no Source Link."; continue; }

  work=$(mktemp -d)
  unzip -qo "$snupkg" -d "$work"
  mapfile -t pdbs < <(find "$work/lib" -name '*.pdb' | sort)
  if [[ ${#pdbs[@]} != 2 ]]; then
    fail "$package $version holds ${#pdbs[@]} PDBs, expected one for net8.0 and one for net10.0."
    rm -rf "$work"
    continue
  fi

  for pdb in "${pdbs[@]}"; do
    framework=$(basename "$(dirname "$pdb")")
    label="$package $version $framework"

    if ! json=$(sourcelink print-json "$pdb" 2>&1); then
      fail "$label has no Source Link JSON: $json"
      continue
    fi
    if ! templates=$(python3 - "$json" "$expected_url" "$label" 2>&1 <<'PY'
import json, sys

raw, expected_url, label = sys.argv[1:4]
try:
    documents = json.loads(raw).get("documents") or {}
except ValueError as error:
    sys.exit(f"{label} has unreadable Source Link JSON: {error}")
if not documents:
    sys.exit(f"{label} has empty Source Link JSON, so a debugger cannot find the source.")
templates = list(documents.values())
missing = [url for url in templates if not url.startswith(expected_url)]
if missing:
    sys.exit(f"{label} maps source to {missing[0]}, which is not the commit it was built from, {expected_url}.")
print("\n".join(templates))
PY
    ); then
      fail "$templates"
      continue
    fi

    if ! documents=$(sourcelink print-documents "$pdb" 2>&1); then
      fail "$label lists no source documents: $documents"
      continue
    fi
    count=$(grep -c ' sha256 ' <<< "$documents" || true)
    [[ $count -gt 0 ]] || fail "$label lists no source documents, so a debugger cannot step into it."
    absolute=$(grep -m1 -E ' (/home/|/Users/|/private/|[A-Za-z]:[\\/])' <<< "$documents" || true)
    [[ -z $absolute ]] || fail "$label keeps an absolute source path, so the PDB is not deterministic: $absolute"
    echo "$label: $count source documents, mapping to $(head -1 <<< "$templates")"
  done
  rm -rf "$work"
done

[[ $failures == 0 ]] || { echo "$failures symbol check(s) failed."; exit 1; }
echo "Every PDB in the four symbol packages links the source at $expected, with no absolute paths."
