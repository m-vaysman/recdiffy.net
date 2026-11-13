#!/usr/bin/env bash
set -euo pipefail

version=${1:?usage: consume.sh <version of the packages in feed/>}
cd "$(dirname "${BASH_SOURCE[0]}")"

failures=0
fail() {
  echo "::error::$1"
  failures=$((failures + 1))
}

# #120: every package leads with "reconciliation" and carries the searchable tags, so it turns up for the word.
common_tags=(reconciliation reconcile data-reconciliation matching diff)

[[ -d feed ]] || { echo "::error::Put the packed .nupkg files in $(pwd)/feed first."; exit 1; }
rm -rf packages Consumer/bin Consumer/obj

dotnet restore Consumer -p:RecDiffyVersion="$version"
dotnet build Consumer -c Release --no-restore -p:RecDiffyVersion="$version"

check_package() {
  local id=$1
  local extra_tags=$2
  local folder="packages/${id,,}/$version"
  local nuspec="$folder/${id,,}.nuspec"
  [[ -f $nuspec ]] || { fail "$id $version was not restored."; return; }
  grep -q '"source": ".*feed' "$folder/.nupkg.metadata" || fail "$id was not restored from the local feed."
  grep -qF '<readme>README.md</readme>' "$nuspec" || fail "$id does not declare a readme."
  [[ -s $folder/README.md ]] || fail "$id has no README.md."
  grep -qF '<icon>columns-square-icon.jpg</icon>' "$nuspec" || fail "$id does not declare an icon."
  [[ -s $folder/columns-square-icon.jpg ]] || fail "$id has no icon file."
  grep -qF '<projectUrl>https://github.com/m-vaysman/recdiffy.net</projectUrl>' "$nuspec" || fail "$id has no project URL."
  grep -qF '<repository type="git" url="https://github.com/m-vaysman/recdiffy.net"' "$nuspec" || fail "$id has no repository URL."
  local title description tags
  title=$(field "$nuspec" title)
  description=$(field "$nuspec" description)
  tags=$(field "$nuspec" tags)
  [[ $title == [Rr]econciliation* ]] || fail "$id title '$title' does not lead with reconciliation."
  [[ $description == [Rr]econciliation* ]] || fail "$id description '$description' does not lead with reconciliation."
  local tag
  for tag in "${common_tags[@]}" $extra_tags; do
    [[ " $tags " == *" $tag "* ]] || fail "$id is missing the package tag '$tag'; it has '$tags'."
  done
  for framework in net8.0 net10.0; do
    [[ -s $folder/lib/$framework/$id.dll ]] || fail "$id has no lib/$framework/$id.dll."
  done
}

# Reads one element from a nuspec, on one line, so a multi-line value still matches.
field() {
  tr -d '\n' < "$1" | sed -n "s/.*<$2>\(.*\)<\/$2>.*/\1/p"
}

# The tags that are specific to one package, on top of the shared ones.
extra_tags() {
  case $1 in
    RecDiffy.RulePacks.Trading) echo "trade-reconciliation trading finance ledger" ;;
    RecDiffy.IO) echo "csv excel" ;;
    RecDiffy.Excel) echo "excel xlsx reports" ;;
    *) echo "" ;;
  esac
}

check_core_dependency() {
  local id=$1
  local nuspec="packages/${id,,}/$version/${id,,}.nuspec"
  local groups
  groups=$(grep -cF "<dependency id=\"RecDiffy\" version=\"$version\"" "$nuspec" || true)
  [[ $groups == 2 ]] || fail "$id does not depend on RecDiffy $version for both net8.0 and net10.0."
}

for id in RecDiffy RecDiffy.RulePacks.Trading RecDiffy.IO RecDiffy.Excel; do
  check_package "$id" "$(extra_tags "$id")"
done
for id in RecDiffy.RulePacks.Trading RecDiffy.IO RecDiffy.Excel; do
  check_core_dependency "$id"
done

for framework in net8.0 net10.0; do
  echo "::group::Samples on $framework, from the packages"
  output=$(dotnet run --project Consumer -c Release --no-build -f "$framework" -p:RecDiffyVersion="$version") \
    || fail "A sample did not get its expected result on $framework, run from the packages."
  echo "$output"
  # #295: the engine reports the package version, without build metadata. A prerelease label has to survive,
  # because the assembly version drops it.
  engine=$(grep -F "engine version: " <<< "$output" | sed 's/.*engine version: //')
  [[ $engine == "$version" ]] || fail "The engine reported '$engine' on $framework, but the packed version is '$version'."
  echo "::endgroup::"
done

[[ $failures == 0 ]] || { echo "$failures package check(s) failed."; exit 1; }
echo "The packages restore from the local feed, hold their files and run every sample on net8.0 and net10.0."
