#!/usr/bin/env bash

set -euo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd -- "$script_directory/.." && pwd)"
branding_file="$root/branded_resources/branding.yaml"
android_manifest="$root/src/MasjidApp/Platforms/Android/AndroidManifest.xml"
ios_info_plist="$root/src/MasjidApp/Platforms/iOS/Info.plist"
generated_cs="$root/src/MasjidApp.Core/Configuration/TenantBranding.g.cs"

android_start='<!-- BEGIN GENERATED BRANDED LAUNCHER ALIASES -->'
android_end='<!-- END GENERATED BRANDED LAUNCHER ALIASES -->'
ios_start='<!-- BEGIN GENERATED BRANDED ALTERNATE ICONS -->'
ios_end='<!-- END GENERATED BRANDED ALTERNATE ICONS -->'

check=false
if [[ ${1:-} == '--check' ]]; then
    check=true
    shift
fi

if (( $# != 0 )); then
    printf 'usage: %s [--check]\n' "$0" >&2
    exit 1
fi

fail() {
    printf 'branding generation failed: %s\n' "$1" >&2
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "$1 is required to generate app icons"
}

run_sips() {
    if ! sips "$@" >/dev/null; then
        fail "sips command failed"
    fi
}

sync_text() {
    local source="$1"
    local target="$2"

    if "$check"; then
        if [[ ! -f "$target" ]] || ! cmp -s "$source" "$target"; then
            printf 'out of date: %s\n' "${target#"$root/"}" >&2
            up_to_date=false
        fi
        return
    fi

    mkdir -p "$(dirname -- "$target")"
    cp "$source" "$target"
}

sync_icon() {
    local source="$1"
    local target="$2"

    if "$check"; then
        if [[ ! -f "$target" ]] || ! cmp -s "$source" "$target"; then
            printf 'out of date: %s\n' "${target#"$root/"}" >&2
            up_to_date=false
        fi
        return
    fi

    mkdir -p "$(dirname -- "$target")"
    cp "$source" "$target"
}

replace_generated_section() {
    local source="$1"
    local replacement="$2"
    local start_marker="$3"
    local end_marker="$4"

    awk -v start_marker="$start_marker" -v end_marker="$end_marker" -v replacement="$replacement" '
        function fail(message) {
            print "branding generation failed: " message > "/dev/stderr"
            failed = 1
            exit 1
        }

        index($0, start_marker) {
            if (found_start) {
                fail("duplicate generated section " start_marker)
            }

            found_start = 1
            indent = substr($0, 1, index($0, start_marker) - 1)
            print
            while ((getline line < replacement) > 0) {
                print indent line
            }
            close(replacement)
            skipping = 1
            next
        }

        skipping {
            if (index($0, end_marker)) {
                print
                found_end = 1
                skipping = 0
            }
            next
        }

        { print }

        END {
            if (!failed && (!found_start || !found_end || skipping)) {
                print "branding generation failed: missing generated section " start_marker > "/dev/stderr"
                exit 1
            }
        }
    ' "$source"
}

parse_organizations() {
    awk '
        function fail(message) {
            print "branding generation failed: " message > "/dev/stderr"
            failed = 1
            exit 1
        }

        function emit() {
            if (!in_organization) {
                return
            }

            if (id == "" || name == "" || website == "" || icon == "") {
                fail("each organization must contain Id, Name, Website, and Icon")
            }

            print id "\t" website "\t" icon
            organization_count++
        }

        /^Version: 1[[:space:]]*$/ {
            if (version_seen || NR != 1) {
                fail("Version must be exactly 1 and appear first")
            }
            version_seen = 1
            next
        }

        /^Organizations:[[:space:]]*$/ {
            if (!version_seen || organizations_seen) {
                fail("Organizations must appear once after Version")
            }
            organizations_seen = 1
            next
        }

        /^  - Id: / {
            if (!organizations_seen) {
                fail("organization appears before Organizations")
            }
            emit()
            in_organization = 1
            id = substr($0, 9)
            name = website = icon = ""
            next
        }

        /^    Name: / {
            if (!in_organization || name != "") {
                fail("Name must appear once for each organization")
            }
            name = substr($0, 11)
            next
        }

        /^    Website: / {
            if (!in_organization || website != "") {
                fail("Website must appear once for each organization")
            }
            website = substr($0, 14)
            next
        }

        /^    Icon: / {
            if (!in_organization || icon != "") {
                fail("Icon must appear once for each organization")
            }
            icon = substr($0, 11)
            next
        }

        /^[[:space:]]*(#.*)?$/ { next }

        { fail("unrecognized branding.yaml entry: " $0) }

        END {
            if (failed) {
                exit 1
            }
            if (!version_seen || !organizations_seen) {
                fail("branding.yaml must contain Version: 1 and Organizations")
            }
            emit()
            if (organization_count == 0) {
                fail("Organizations must be a non-empty list")
            }
        }
    ' "$branding_file"
}

launcher_alias() {
    local id="$1"
    local first_character
    first_character="$(printf '%s' "${id:0:1}" | tr '[:lower:]' '[:upper:]')"
    printf '%s%sLauncher' "$first_character" "${id:1}"
}

require_command sips
[[ -f "$branding_file" ]] || fail "branding.yaml does not exist"

temporary_directory="$(mktemp -d "${TMPDIR:-/tmp}/masjid-branding.XXXXXX")"
trap 'rm -rf "$temporary_directory"' EXIT

organization_rows="$temporary_directory/organizations.tsv"
parse_organizations > "$organization_rows"

tenant_ids=()
tenant_websites=()
tenant_icons=()
while IFS=$'\t' read -r id website icon; do
    [[ "$id" =~ ^[a-z][a-z0-9]*$ ]] || fail "Id must use lowercase letters and digits: $id"
    [[ "$icon" =~ ^[a-z0-9][a-z0-9_-]*\.png$ ]] || fail "Icon must be a PNG filename for $id"
    [[ -f "$root/branded_resources/$icon" ]] || fail "icon source does not exist for $id"
    [[ "$website" =~ ^https://[A-Za-z0-9][A-Za-z0-9.-]*[A-Za-z0-9](:[0-9]+)?(/[^[:space:]]*)?$ ]] || fail "Website must be an HTTPS URL for $id"

    if (( ${#tenant_ids[@]} > 0 )); then
        for known_id in "${tenant_ids[@]}"; do
            [[ "$known_id" != "$id" ]] || fail "duplicate Id: $id"
        done
    fi

    tenant_ids+=("$id")
    tenant_websites+=("$website")
    tenant_icons+=("$icon")
done < "$organization_rows"

(( ${#tenant_ids[@]} > 0 )) || fail "Organizations must be a non-empty list"

generated_cs_file="$temporary_directory/TenantBranding.g.cs"
{
    cat <<'EOF'
// <auto-generated />
// Generated from branded_resources/branding.yaml by scripts/generate-branded-resources.sh.

namespace MasjidApp.Core.Configuration;

/// <summary>Describes the native branding assets compiled for one tenant.</summary>
public sealed record TenantBrandingDefinition(
    string Id,
    string WebsiteHost,
    string AndroidLauncherAlias,
    string IosAlternateIconName);

public static partial class TenantBranding
{
    private static readonly TenantBrandingDefinition[] Definitions =
    [
EOF

    for index in "${!tenant_ids[@]}"; do
        id="${tenant_ids[$index]}"
        website="${tenant_websites[$index]}"
        host="${website#https://}"
        host="${host%%/*}"
        host="${host%%:*}"
        separator=','
        if (( index == ${#tenant_ids[@]} - 1 )); then
            separator=''
        fi
        printf '        new("%s", "%s", "%s", "%s")%s\n' \
            "$id" "$host" "$(launcher_alias "$id")" "$(printf '%s' "$id" | tr '[:lower:]' '[:upper:]')" "$separator"
    done

    cat <<'EOF'
    ];

    /// <summary>Gets every tenant with compiled native branding.</summary>
    public static IReadOnlyList<TenantBrandingDefinition> All => Definitions;
}
EOF
} > "$generated_cs_file"

android_aliases="$temporary_directory/android-aliases.xml"
ios_icons="$temporary_directory/ios-icons.plist"
: > "$android_aliases"
: > "$ios_icons"

for index in "${!tenant_ids[@]}"; do
    id="${tenant_ids[$index]}"
    alias="$(launcher_alias "$id")"
    ios_name="$(printf '%s' "$id" | tr '[:lower:]' '[:upper:]')"

    cat >> "$android_aliases" <<EOF
<activity-alias android:name=".$alias" android:enabled="false" android:exported="true"
	android:icon="@mipmap/${id}_appicon" android:roundIcon="@mipmap/${id}_appicon"
	android:targetActivity="com.goodsoftware.masjidapp.MainActivity">
	<intent-filter>
		<action android:name="android.intent.action.MAIN" />
		<category android:name="android.intent.category.LAUNCHER" />
	</intent-filter>
</activity-alias>
EOF

    cat >> "$ios_icons" <<EOF
<key>$ios_name</key>
<dict>
	<key>CFBundleIconFiles</key>
	<array>
		<string>${id}_icon</string>
	</array>
	<key>UIPrerenderedIcon</key>
	<false/>
</dict>
EOF
done

android_manifest_file="$temporary_directory/AndroidManifest.xml"
ios_info_plist_file="$temporary_directory/Info.plist"
replace_generated_section "$android_manifest" "$android_aliases" "$android_start" "$android_end" > "$android_manifest_file"
replace_generated_section "$ios_info_plist" "$ios_icons" "$ios_start" "$ios_end" > "$ios_info_plist_file"

up_to_date=true
sync_text "$generated_cs_file" "$generated_cs"
sync_text "$android_manifest_file" "$android_manifest"
sync_text "$ios_info_plist_file" "$ios_info_plist"

for index in "${!tenant_ids[@]}"; do
    id="${tenant_ids[$index]}"
    source="$root/branded_resources/${tenant_icons[$index]}"
    dimensions="$(sips -g pixelWidth -g pixelHeight "$source")" || fail "could not read dimensions for $source"
    width="$(awk '/pixelWidth:/ { print $2 }' <<< "$dimensions")"
    height="$(awk '/pixelHeight:/ { print $2 }' <<< "$dimensions")"
    [[ "$width" =~ ^[0-9]+$ && "$height" =~ ^[0-9]+$ ]] || fail "could not read dimensions for $source"

    normalized="$temporary_directory/$id.png"
    if (( width == height )); then
        cp "$source" "$normalized"
    else
        side="$width"
        if (( height < width )); then
            side="$height"
        fi
        run_sips --cropToHeightWidth "$side" "$side" "$source" --out "$normalized"
    fi

    for size_target in \
        "1024:$root/src/MasjidApp/Resources/AppIcon/${id}_appicon.png" \
        "1024:$root/src/MasjidApp/Platforms/Android/Resources/mipmap/${id}_appicon.png" \
        "120:$root/src/MasjidApp/Platforms/iOS/AlternateIcons/${id}_icon@2x.png" \
        "180:$root/src/MasjidApp/Platforms/iOS/AlternateIcons/${id}_icon@3x.png"; do
        size="${size_target%%:*}"
        target="${size_target#*:}"
        generated_icon="$temporary_directory/${id}_${size}_$(basename -- "$target")"
        run_sips --resampleHeightWidth "$size" "$size" "$normalized" --out "$generated_icon"
        sync_icon "$generated_icon" "$target"
    done
done

"$up_to_date" || exit 1