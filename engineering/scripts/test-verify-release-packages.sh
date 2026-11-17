#!/usr/bin/env bash
# Exercises dependency-family boundaries with a throwaway package and repository; no restore or publish occurs.
set -euo pipefail

script="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/verify-release-packages.sh"
fixture=$(mktemp -d)
trap 'rm -rf "$fixture"' EXIT
repo="$fixture/repo"
artifacts="$fixture/artifacts"
content="$fixture/content"
version=10.0.5-beta
core=10.0.113-beta
commit=0123456789012345678901234567890123456789
assembly=WoW.Two.Sdk.Beta.Be.Harvest.Module
id=WoW2.Sdk.Beta.Be.Harvest.Module
mkdir -p "$repo/engineering/codebase/harvest.backend-services/packages/Module" "$artifacts" "$content/lib/net10.0"
git -C "$repo" init -q
touch "$repo/engineering/codebase/harvest.backend-services/packages/Module/$assembly.csproj"
printf '<Project><BackendSdkVersion>%s</BackendSdkVersion></Project>\n' "$core" \
  > "$repo/engineering/codebase/harvest.backend-services/Directory.Packages.props"
touch "$content/README.md" "$content/lib/net10.0/$assembly.dll" "$artifacts/$id.$version.snupkg"

pack() { # pack <core-version> <family-version>
  cat > "$content/$id.nuspec" <<XML
<?xml version="1.0"?>
<package><metadata>
  <id>$id</id><version>$version</version>
  <license type="expression">MIT</license>
  <repository type="git" commit="$commit" />
  <dependencies><group targetFramework="net10.0">
    <dependency id="WoW2.Sdk.Beta.Be" version="$1" />
    <dependency id="WoW2.Sdk.Beta.Be.Harvest.Abstractions" version="$2" />
    <dependency id="WoW2.Sdk.Beta.Be.Identity.Auth" version="10.0.30-beta" />
  </group></dependencies>
</metadata></package>
XML
  (cd "$content" && zip -qr "$artifacts/$id.$version.nupkg" .)
}

verify() {
  (cd "$repo" && bash "$script" "$artifacts" "$version" "$commit")
}

pack "$core" "$version"
verify > "$fixture/result" 2>&1 || { cat "$fixture/result"; exit 1; }
echo 'ok   independent Identity.Auth version is not a Harvest family dependency'

pack "$core" 10.0.4-beta
if verify > "$fixture/result" 2>&1; then
  echo 'FAIL mismatched Harvest family dependency was accepted' >&2
  exit 1
fi
grep -Fq "a family dependency is not at $version" "$fixture/result"
echo 'ok   mismatched Harvest family dependency is rejected'

pack 10.0.112-beta "$version"
if verify > "$fixture/result" 2>&1; then
  echo 'FAIL mismatched core dependency was accepted' >&2
  exit 1
fi
grep -Fq "be sdk floor is not the pin $core" "$fixture/result"
echo 'ok   mismatched core dependency is rejected'
