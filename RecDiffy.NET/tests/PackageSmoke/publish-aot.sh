#!/usr/bin/env bash
set -euo pipefail

version=${1:?usage: publish-aot.sh <version of the packages in feed/>}
cd "$(dirname "${BASH_SOURCE[0]}")"

failures=0
fail() {
  echo "::error::$1"
  failures=$((failures + 1))
}

[[ -d feed ]] || { echo "::error::Put the packed .nupkg files in $(pwd)/feed first."; exit 1; }
rm -rf packages out Aot/bin Aot/obj

publish_and_run() {
  local mode=$1 framework=$2
  shift 2
  local output="out/$mode-$framework"
  echo "::group::Publish $mode on $framework"
  if dotnet publish Aot -c Release -f "$framework" --use-current-runtime -o "$output" \
      -warnaserror -p:RecDiffyVersion="$version" "$@"; then
    "$output/PackageSmoke.Aot" || fail "A sample did not get its expected result when published $mode on $framework."
  else
    fail "Publishing $mode on $framework produced a warning or an error."
  fi
  echo "::endgroup::"
}

for framework in net8.0 net10.0; do
  publish_and_run native-aot "$framework"
  publish_and_run trimmed "$framework" -p:PublishAot=false -p:PublishTrimmed=true --self-contained
done

[[ $failures == 0 ]] || { echo "$failures AOT or trimming check(s) failed."; exit 1; }
echo "RecDiffy and RecDiffy.RulePacks.Trading publish with Native AOT and with trimming, with no warnings, and the samples pass."
