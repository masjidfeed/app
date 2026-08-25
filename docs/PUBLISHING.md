# Publishing to the App Store and Google Play

Step-by-step release runbook for MasjidApp. iOS and Android publishing can run locally or
through GitHub Actions. Google Play requires one manual bootstrap upload before API-based
uploads work.

# TestFlight / App Store (iOS)

## Identifiers

| Thing | Value |
|---|---|
| App bundle ID | `com.goodsoftware.masjidapp` |
| Widget bundle ID | `com.goodsoftware.masjidapp.MasjidWidgetExtension` |
| Notification service bundle ID | `com.goodsoftware.masjidapp.NotificationServiceExtension` |
| App Group | `group.com.goodsoftware.masjidapp` |

---

## Step 1 - One-time Apple setup

Requires a **paid** Apple Developer Program membership and the Account Holder or Admin
role. Free/personal teams cannot upload to TestFlight.

In [Certificates, Identifiers & Profiles](https://developer.apple.com/account/resources):

1. **Identifiers ▸ +** ▸ App Groups ▸ register `group.com.goodsoftware.masjidapp`.
2. **Identifiers ▸ +** ▸ App IDs ▸ register all three bundle IDs above (explicit App IDs).
3. For the app and widget App IDs: select each ▸ Edit ▸ tick **App Groups** ▸ **Configure** ▸ select
  `group.com.goodsoftware.masjidapp` ▸ Continue ▸ Assign ▸ Done ▸ **Save** ▸ Confirm.
4. For the app ID `com.goodsoftware.masjidapp`, also enable **Push Notifications** and
  **App Attest**, then save the App ID. Release push registration requires both the
  production `aps-environment` and production App Attest entitlements.
5. **Certificates ▸ +** ▸ **Apple Distribution** ▸ Continue. Apple asks for a Certificate
   Signing Request (CSR) generated from your Mac's Keychain:

   - Open **Keychain Access** ▸ menu bar **Keychain Access ▸ Certificate Assistant ▸
     Request a Certificate From a Certificate Authority...**
   - Enter any email address as "User Email Address" (it's just a label on the
     certificate, not validated against your Apple ID - it does not need to match the
     account you publish with), leave "CA Email Address" blank, select **Saved to disk**,
     then Continue and save the `.certSigningRequest` file.
   - Back on the portal, click **Choose File**, select that file, then **Continue** ▸
     **Download** the resulting `.cer` and double-click it to install it into Keychain
     Access (this pairs it with the private key generated alongside the CSR - do this on
     the same Mac you generated the CSR on).
6. **Profiles ▸ +** ▸ **App Store** ▸ create one profile per App ID (three total).
  Download and double-click all three to install them. The notification service App ID
  needs no additional capability; its profile is required to sign the embedded extension.

In [App Store Connect](https://appstoreconnect.apple.com): **Apps ▸ +** ▸ New App, using
bundle ID `com.goodsoftware.masjidapp`.

> Changing capabilities on an App ID invalidates existing provisioning profiles. Any time
> you edit an App ID afterwards, regenerate and re-download the affected profiles, and delete the
> stale copies from `~/Library/MobileDevice/Provisioning Profiles` and
> `~/Library/Developer/Xcode/UserData/Provisioning Profiles`.

In Google Cloud and Firebase for project `masjidapp-bcfc7`:

1. Enable the [Firebase App Check API](https://console.developers.google.com/apis/api/firebaseappcheck.googleapis.com/overview?project=275056678751).
2. Open [Firebase App Check](https://console.firebase.google.com/project/masjidapp-bcfc7/appcheck/apps)
  and register iOS app `1:275056678751:ios:349c8a8a5a5d6bf67a08ea` with the
  **App Attest** provider.

Both steps are required before Release push registration can obtain the
`X-Firebase-AppCheck` token. If either is missing, registration stops before the Masjid API
receives a request; changing Apple entitlements or republishing the app does not fix it.

---

## Steps 2–6 - automated

[scripts/publish-appstore.sh](../scripts/publish-appstore.sh) runs Steps 2 through 6
for you: repo prep, version bump, archive build, verification, and upload. Only Step 1
(Apple portal setup, above) is manual.

### Run it

```bash
scripts/publish-appstore.sh
```

No arguments are needed after `az login`. By default the script downloads the Apple
certificate, provisioning profiles, and App Store Connect API key from Key Vault
`masjidfeed-publish`, imports the certificate into a temporary keychain, and removes the
private key and API key after it exits. Set `PUBLISHING_KEY_VAULT_NAME=` only to use the
legacy locally installed credential flow.

Step 1 resolves everything and prints what it picked:

| Value | Auto-detected from |
| --- | --- |
| `CODESIGN_KEY` | the sole `Apple Distribution` identity in your keychain |
| `CODESIGN_PROVISION` | newest unexpired App Store profile for `<ApplicationId>` |
| `WIDGET_CODESIGN_PROVISION` | same, for `<ApplicationId>.MasjidWidgetExtension` |
| `NOTIFICATION_SERVICE_CODESIGN_PROVISION` | same, for `<ApplicationId>.NotificationServiceExtension` |
| `API_KEY_ID` | the sole `AuthKey_<id>.p8` in altool's search paths |
| `API_ISSUER_ID` | `~/.appstoreconnect/issuer_id` |

A profile qualifies as App Store distribution when its `application-identifier` matches
`<TEAMID>.<bundle id>` and it has **no** `ProvisionedDevices` list (development and ad-hoc
profiles do). Xcode quietly generates its own `iOS Team Store Provisioning Profile: …`
duplicates, so an explicitly named profile wins over the managed one.

Detection fails loudly and lists the candidates if it finds more than one distribution
identity or API key. Export the matching variable to override any of them - see the
script's header comment for the full list, plus `XCODE_APP`, `BUILD_NUMBER` and
`APP_VERSION`.

What each step does:

- **Step 2 (repo prep):** adds `ITSAppUsesNonExemptEncryption` to
  [Platforms/iOS/Info.plist](../src/MasjidApp/Platforms/iOS/Info.plist) if it's missing.
- **Step 3 (version bump):** increments `ApplicationVersion` by 1 (override with
  `BUILD_NUMBER`); leaves `ApplicationDisplayVersion` unchanged unless `APP_VERSION` is
  set. Keeps `MasjidApp.csproj` and both extensions' `Info.plist` files in sync.
- **Step 4 (archive):** removes the complete prior Release `ios-arm64` bin/obj slice, then
  runs `dotnet publish -f net10.0-ios -c Release` for `ios-arm64` with
  `-p:ArchiveOnBuild=true`. This prevents incremental outputs from mixing AOT modules and
  dependency assemblies from different SDK/workload generations.
  If `WIDGET_CODESIGN_PROVISION` is set, it embeds the widget's own
  `embedded.mobileprovision` and re-signs both bundles with a real timestamp -
  [WidgetExtension.targets](../src/MasjidApp/Platforms/iOS/MasjidWidget/WidgetExtension.targets)
  alone only produces a dev-style `--timestamp=none` signature with no embedded profile,
  which App Store validation rejects (`ITMS-90165`). During re-signing, the profile's App
  Attest allowed-environments array is narrowed to the Release app's scalar `production`
  entitlement. If the widget profile is unset, the widget is left out of the build entirely
  (`-p:EmbedWidgetExtension=false`) so the archive still passes.
  The notification service is built as a native Swift `.appex`, embedded beside the widget,
  and signed with its own App Store profile. If that profile is missing, rich push images
  are omitted with `-p:EmbedNotificationServiceExtension=false`.
- **Step 5 (verify):** checks bundle identities and team entitlements, requires the App
  Group on the app and widget, requires production APNs and App Attest on the app, validates
  the notification-service extension point and signature, checks each embedded profile, and
  rejects AOT modules whose dependency MVIDs are absent from the packaged AOT graph. Stops
  before uploading on any failure.
- **Step 6 (upload):** if `API_KEY_ID`/`API_ISSUER_ID` are set, repackages the signed app
  as a proper `Payload/`-rooted `.ipa`, validates it, and uploads via `xcrun altool`.
  Otherwise prints the archive path and stops for a manual upload via Xcode Organizer.

Other env vars: `XCODE_APP` selects which installed Xcode to build with when more than one
is present (default `Xcode_26.5`). See the script's header comment for the full list.

### Optional - set up automated upload

Without an API key the script stops after building the archive and you upload by hand from
Xcode Organizer. To let it upload for you:

1. In App Store Connect go to **Users and Access ▸ Integrations ▸ App Store Connect API**,
   Team Keys tab.
2. Click **+**, name the key (e.g. `TestFlight Upload`), and give it the **App Manager**
   role. Developer is not sufficient to upload builds.
3. Copy the **Issuer ID** shown above the table - that is `API_ISSUER_ID`. Copy the new
   row's **Key ID** - that is `API_KEY_ID`.
4. Download the `.p8` private key. **Apple lets you download it only once.** Save it as:

   ```bash
   mkdir -p ~/.appstoreconnect/private_keys
   mv ~/Downloads/AuthKey_XXXXXXXXXX.p8 ~/.appstoreconnect/private_keys/
   chmod 600 ~/.appstoreconnect/private_keys/AuthKey_XXXXXXXXXX.p8
   ```

   The filename must stay exactly `AuthKey_<API_KEY_ID>.p8`. Never commit it.
5. Save the issuer ID next to it so the script can find it:

   ```bash
   echo 'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx' > ~/.appstoreconnect/issuer_id
   chmod 600 ~/.appstoreconnect/issuer_id
   ```

That's it - `scripts/publish-appstore.sh` now picks both up with no arguments.

The key ID and issuer ID are not secrets, but the `.p8` is - it grants API access to the
team. Keep it outside the repo (`NFR-SC-01`).


## Publishing from GitHub

[.github/workflows/publish-appstore.yml](../.github/workflows/publish-appstore.yml)
runs the same script on a `macos-26` runner. Trigger it from **Actions ▸ Publish to App
Store ▸ Run workflow**; all inputs are optional.

The workflow recreates the local environment (keychain, provisioning profiles,
`~/.appstoreconnect`) and then calls `scripts/publish-appstore.sh` unchanged, so the
script stays the single source of release logic.

Every run that reaches archive creation also uploads an Actions artifact named
`ios-crash-debug-<version>-<build>-<commit>-run<attempt>`. The artifact step runs even
when App Store validation or upload fails and retains:

- the complete `.xcarchive`, including its signed app and all dSYMs;
- a separate dSYM archive for direct use with `atos`, `dwarfdump`, or Xcode Organizer;
- the exact IPA passed to `altool` (when upload credentials were configured);
- compiled and linked app-owned DLL/PDB pairs for managed stack investigation;
- the MSBuild binary log and publish log;
- app/dSYM UUIDs, code-signing metadata, entitlements, dependency assets, SDK/workload
  versions, the source commit/tree, the uncommitted version bump, and SHA-256 checksums.

The packaging step verifies that the main app binary UUID exactly matches the main dSYM
UUID before the artifact is accepted. To investigate a crash, match the crash report's
`Binary Images` UUID to `metadata/app-uuids.txt`, then use the dSYM from that same artifact.
Do not use a dSYM rebuilt from the same commit: native/AOT addresses and UUIDs are specific
to the original publish invocation.

Artifacts use a portable 90-day retention period; private repositories can configure a
higher repository limit and increase the workflow value up to that limit. For production
builds that must remain diagnosable longer, mirror the downloaded artifact to private
durable storage before it expires. Artifact download access follows repository visibility,
so do not treat Actions artifacts in a public repository as private or attach this bundle
to a public GitHub Release: the archive contains the signed application, embedded
provisioning profiles, native symbols, and managed symbols.

It also picks `XCODE_APP` itself, since the local default does not exist on the runner.
Two things make that fiddly, and both are worth knowing before touching the `Select Xcode`
step:

- .NET for iOS validates the Xcode major/minor version *exactly*, and the required version
  is not the SDK pack's folder suffix - pack `Microsoft.iOS.Sdk.net10.0_26.5` has shipped
  revisions wanting Xcode 26.6. The step reads `_RecommendedXcodeVersion` from the restored
  pack, which is the value the check actually compares against. Which pack gets restored is
  pinned by `workloadVersion` in [global.json](../global.json); bump it in lockstep with
  your local `dotnet workload --info`, or CI and local build against different Xcodes.
- The runner exposes each Xcode under several names (`Xcode.app`, `Xcode_26.5.app`,
  `Xcode_26.5.0.app`) where all but one are symlinks. The step resolves the choice to the
  real bundle and runs `xcode-select` against it, because tooling that drops
  `DEVELOPER_DIR` falls back to the system-wide selection and would otherwise look for the
  macOS SDK inside a different Xcode - which surfaces as the thoroughly misleading
  `SDK ".../MacOSX.sdk" cannot be located` / `actool exited with code 72`.

### Key Vault release credentials

Store these values in Azure Key Vault `masjidfeed-publish`. GitHub Actions authenticates
with a short-lived OIDC token and reads them at runtime; do not create repository secrets.

| Secret | Contents |
| --- | --- |
| `ios-dist-cert-p12-base64` | Base64 of the Apple Distribution certificate **and its private key**, exported as `.p12` |
| `ios-dist-cert-password` | The password used to export that `.p12` |
| `ios-provisioning-profile-base64` | Base64 of the app's App Store `.mobileprovision` |
| `ios-widget-provisioning-profile-base64` | Base64 of the widget's App Store `.mobileprovision` |
| `ios-notification-service-profile-base64` | Base64 of the notification service extension's App Store `.mobileprovision` |
| `appstore-api-key-id` | The App Store Connect API key ID (10 characters) |
| `appstore-api-issuer-id` | The issuer UUID from the same page |
| `appstore-api-private-key-base64` | Base64 of the `AuthKey_<key id>.p8` |

After enabling or changing an app capability, regenerate the app's App Store profile and
replace `IOS_PROVISIONING_PROFILE_BASE64`. A stale profile strips the new signed
entitlements even when the repository's entitlement file declares them.

Once **App Attest Opt-In**, **App Groups**, and **Push Notifications** are enabled for
`com.goodsoftware.masjidapp`, regenerate and publish the profile in one command:

```bash
scripts/regenerate-ios-provisioning-profile.sh
```

The script uses the App Store Connect key under `~/.appstoreconnect`, creates a new
`IOS_APP_STORE` profile with the active distribution certificate, and verifies production
APNs, production App Attest, the App Group, and absence of development devices. Only after
those checks pass does it install the profile locally and replace
`IOS_PROVISIONING_PROFILE_BASE64` in `masjidfeed/app`. It does not revoke older
profiles. Apple does not expose App Attest Opt-In through the public API, so that initial
bundle-ID capability remains a one-time portal action.

Create, install, and publish the notification service profile with:

```bash
scripts/regenerate-ios-provisioning-profile.sh --notification-service
```

This mode creates `com.goodsoftware.masjidapp.NotificationServiceExtension` when needed
and replaces `IOS_NOTIFICATION_SERVICE_PROFILE_BASE64` only after verifying the generated
distribution profile targets that exact bundle ID.

### Producing the values

Every secret is base64 of a file, so the recipe is the same in each case:
`openssl base64 -A -in <file>`. Pipe to `pbcopy` rather than printing, so secrets stay out
of your terminal scrollback.

**1. The distribution certificate (`ios-dist-cert-p12-base64`).** This is the only one that
needs real work, because the `.p12` must contain the certificate *and* its private key.

> **Do not just run `security export -t identities`.** It exports **every** identity in
> the keychain - on a typical machine that includes personal certificates, private CAs and
> localhost keys, all of which would be uploaded to GitHub. Export, then narrow to the one
> identity you want.

```bash
# Pick a throwaway transport password; it becomes ios-dist-cert-password.
PW=$(openssl rand -base64 24 | tr -d '\n')

# Export everything, then keep only the Apple Distribution identity.
security export -k ~/Library/Keychains/login.keychain-db \
  -t identities -f pkcs12 -P "$PW" -o all.p12

openssl pkcs12 -in all.p12 -passin "pass:$PW" -nokeys  -legacy        > certs.pem
openssl pkcs12 -in all.p12 -passin "pass:$PW" -nocerts -legacy -noenc > keys.pem
# Split certs.pem/keys.pem, keep the cert whose subject contains
# "Apple Distribution:" plus the key whose public half matches it, then repackage:
openssl pkcs12 -export -legacy -inkey dist-key.pem -in dist-cert.pem \
  -passout "pass:$PW" -out dist.p12
```

Verify before uploading - it must print exactly one subject and one key:

```bash
openssl pkcs12 -in dist.p12 -passin "pass:$PW" -nokeys -legacy | grep subject=
openssl pkcs12 -in dist.p12 -passin "pass:$PW" -nocerts -legacy -noenc | grep -c 'BEGIN PRIVATE KEY'
```

Alternatively, export it through the GUI: **Keychain Access ▸ login ▸ My Certificates**,
expand the *Apple Distribution* row so the private key is included, right-click ▸ **Export**.

**2. The provisioning profiles.** List the installed profiles with their names, then encode
the three you need. Prefer `scripts/regenerate-ios-provisioning-profile.sh` for the app and
notification service profiles; use this manual flow for the widget profile or as a fallback:

```bash
for f in ~/Library/Developer/Xcode/UserData/Provisioning\ Profiles/*.mobileprovision; do
  echo "$(security cms -D -i "$f" | plutil -extract Name raw -)  ->  $f"
done
openssl base64 -A -in "<path to MasjidApp Provisioning>" | pbcopy
```

**3. The API key.** Already on disk from the automated-upload setup above:

```bash
openssl base64 -A -in ~/.appstoreconnect/private_keys/AuthKey_<key id>.p8 | pbcopy
```

Delete every intermediate file (`all.p12`, `dist.p12`, `certs.pem`, `keys.pem`) once the
secrets are uploaded. The `.p12` and `.p8` both grant signing and upload rights for the
whole team and must never be committed (`NFR-SC-01`).

With Azure CLI you can upload the two generated values directly:

```bash
openssl base64 -A -in dist.p12 > dist.p12.b64
az keyvault secret set --vault-name masjidfeed-publish \
  --name ios-dist-cert-p12-base64 --file dist.p12.b64
printf '%s' "$PW" > dist.p12.password
az keyvault secret set --vault-name masjidfeed-publish \
  --name ios-dist-cert-password --file dist.p12.password
```

### Build numbers

Each run bumps `ApplicationVersion` and, by default, commits that bump back to the branch
so the next run does not reuse a build number App Store Connect has already seen. Uncheck
**Commit the version bump** only if you are re-running a failed upload, and pass an
explicit **build_number** in that case.


## Step 7 - Publish the build to TestFlight

After Step 6 uploads, the build appears in
[App Store Connect](https://appstoreconnect.apple.com) ▸ your app ▸ **TestFlight** tab
with status **Processing**. Processing takes roughly 5–30 minutes; refresh until the
status becomes **Ready to Submit** (or **Ready to Test**, depending on account state).

> **`UPLOAD SUCCEEDED` does not mean the build made it.** Apple re-validates during
> processing and silently discards builds that fail, emailing an `ITMS-*` rejection up to
> an hour later - the build simply never shows up in TestFlight. To check without waiting
> for the email, query the API with the same key Step 6 uses:
> `GET /v1/apps/<app id>/builds`. An empty `data` array means still processing *or*
> rejected; a `processingState` of `VALID` means it is live in TestFlight.
>
> A past rejection was `ITMS-90032: Invalid Image Path` for the `icob_icon` alternate
> icon. Alternate icons must sit at the **bundle root**, so the `BundleResource` item in
> [MasjidApp.csproj](../src/MasjidApp/MasjidApp.csproj) needs
> `LogicalName="%(Filename)%(Extension)"` - without it the PNGs ship under
> `Platforms/iOS/AlternateIcons/`. Step 5 now verifies this before uploading.

1. If a yellow **Missing Compliance** flag appears next to the build, click it ▸ answer
   the export compliance question ▸ **Start Internal Testing** (or **Submit**). This
   should not happen once `ITSAppUsesNonExemptEncryption` is present in the shipped
   Info.plist (Step 2 sets this), but answer it if asked.
2. Under **General ▸ Test Information**, fill in **Beta App Description**,
   **Feedback Email**, and the **Privacy Policy URL** (`NFR-PR-02`) - required before any
   external group can be submitted for review.

### Internal testing (fastest - no Apple review)

Available immediately to up to 100 users who already have a role on the app in App Store
Connect (**Users and Access**).

1. TestFlight tab ▸ **Internal Testing** ▸ **+** to create a group (e.g. "Internal").
2. Add existing App Store Connect users as testers to that group.
3. Select the processed build ▸ **Add to Internal Group** (or add it during group
   creation).
4. Testers receive an email/TestFlight app notification and can install immediately -
   no Beta App Review needed.

### External testing (up to 10,000 testers - requires Apple review)

1. TestFlight tab ▸ **External Testing** ▸ **+** to create a group (e.g. "Beta").
2. Add the build to the group.
3. Add testers by email, or enable **Public Link** to let anyone with the link enroll.
4. Click **Submit for Review**. This is **Beta App Review**, a lighter review than a full
   App Store submission - typically same-day to a couple of days.
5. Once approved, the group's testers are notified and can install via the TestFlight app.

Any new build re-submitted to an external group after the first approval is usually
auto-approved unless the change is significant (e.g. new permissions).

---

## Step 8 - App privacy answers

The app requests four permissions, declared in
[Platforms/iOS/Info.plist](../src/MasjidApp/Platforms/iOS/Info.plist):

| Permission | Why |
|---|---|
| Camera | QR scan to add a masjid |
| Calendar (full access) | Save events to the device calendar |
| Location (when in use) | Qibla direction |
| Notifications | Local prayer and iqama reminders |

Answer **Data Not Collected** unless telemetry consent is enabled: location is used
transiently and never stored (`NFR-PR-01`), and the app never handles payment or card data
(`NFR-SC-03`). There is no tracking, so no ATT prompt.

[PrivacyInfo.xcprivacy](../src/MasjidApp/Platforms/iOS/Resources/PrivacyInfo.xcprivacy)
already declares the three MAUI framework required-reason APIs (file timestamp, system boot
time, disk space).

---

## Step 9 - Store listing (App Store submission only)

- App icon: generated by MAUI from `Resources/AppIcon/appicon.png`; no separate 1024px
  upload is needed from this repo.
- Screenshots: iPhone only - the app declares `UIDeviceFamily [1]` and is portrait-only.
  iPad is deliberately out of scope: iPadOS 26 deprecated `UIRequiresFullScreen`, so an
  iPad-capable app must support all four orientations and be resizable (`ITMS-90474`).
- Description, keywords, support URL, and privacy policy URL.

---

---

# Google Play Store (Android)

Google Play accepts an Android App Bundle (`.aab`), not the debug APK used by local
development. The first bundle must be uploaded manually in Play Console. The package name
is `com.goodsoftware.masjidapp` and cannot be changed or reused after the app is created.

## Step 1 - One-time Google Play setup

1. Register and complete identity verification for a
   [Google Play developer account](https://play.google.com/console/signup). Google charges
   a one-time registration fee. New personal accounts may also have device-verification
   and closed-testing requirements before production access; Play Console shows the exact
   requirements for the account.
2. In [Play Console](https://play.google.com/console), select **Home ▸ Create app**. Choose
   the default language, app name, **App**, and **Free**, add the support email, accept the
   declarations and Play App Signing terms, then create the app.
3. Use `com.goodsoftware.masjidapp` when the first bundle establishes the package name.
   Confirm that no other Play Console app already owns it.
4. Leave **Play App Signing** enabled. Google then protects the app-signing key used on
   users' devices, while the local keystore below is the replaceable *upload key* used to
   authenticate new bundles.

## Step 2 - Create and protect the upload key

Do this once. Keep the keystore and password outside the repository and back both up in a
private password manager or other durable secret store (`NFR-SC-01`). Do not use the debug
keystore.

```bash
scripts/setup-google-play-credentials.sh
```

The script creates `~/.config/masjidapp`, generates a random password into
`android-signing-password`, creates `masjidapp-upload.keystore` from that file, restricts
both to `600`, then verifies that the stored password opens the keystore and prints the
certificate fingerprints. Re-running it is safe: an existing keystore is never replaced
without `--force`, so it doubles as a verification command.

Generate the password into the file first and pass it with `keytool -storepass:file`. Typing
the password at a prompt or moving it through the clipboard lets the keystore and password
file diverge, which surfaces later as `keystore password was incorrect` with no way to
recover the key.

After the first upload, go to **Protected with Play ▸ Play Store distribution ▸ Go to Play
app signing**. Compare the printed SHA-1 and SHA-256 values with the **Upload key
certificate** on that page.
Losing an upload key requires an upload-key reset through Play; losing an unmanaged
app-signing key can make future updates impossible.

## Step 2a - Configure Google Play API access

Fastlane uses the Google Play Developer API to upload later bundles. This API cannot create
the Play app or perform its first upload, so complete the manual bootstrap in Step 6 before
running the workflow for the first time.

The Play Console no longer has a **Setup ▸ API access** page, and a developer account no
longer needs to be linked to a Google Cloud project. Create the service account entirely in
Google Cloud, then invite it into Play Console:

1. Open [Google Cloud Console](https://console.cloud.google.com/projectcreate) and create or
  select a project to own the credential.
2. In that project, enable the
  [Google Play Android Developer API](https://console.cloud.google.com/apis/library/androidpublisher.googleapis.com).
3. Open [IAM & Admin ▸ Service Accounts](https://console.cloud.google.com/iam-admin/serviceaccounts),
  select **Create service account**, name it `masjidapp-google-play`, and select **Done**.
  Skip the optional role and user-access steps; Play permissions are granted in Play
  Console, not by a Cloud IAM role.
4. On the new account choose **Actions ▸ Manage keys ▸ Add key ▸ Create new key ▸ JSON**.
  The key downloads once and cannot be re-downloaded.
5. Open [Play Console ▸ Users and permissions](https://play.google.com/console/users-and-permissions),
  select **Invite new users**, and enter the service account's `client_email`. Under **App
  permissions** add MasjidApp and grant **Release apps to testing tracks**. Grant **Release
  to production** only if this workflow will be allowed to publish to production. Select
  **Invite user**.

Install and validate the downloaded key:

```bash
scripts/setup-google-play-credentials.sh \
  --service-account ~/Downloads/<downloaded key>.json
```

The Play Console invitation is what grants access; the Cloud project-level role does not.
Once the first manual bundle exists, Fastlane can confirm the end-to-end connection:

```bash
fastlane run validate_play_store_json_key \
  json_key:"$HOME/.config/masjidapp/google-play-service-account.json"
```

## Step 3 - Complete the Play Console listing

Complete every required dashboard task before requesting production review:

- **Main store listing:** app name, short and full descriptions, support email, privacy
  policy URL, 512×512 app icon, 1024×500 feature graphic, and at least two phone
  screenshots. The existing copy in [appstore/submission.md](appstore/submission.md) is a
  starting point, but adapt it to Play's field lengths.
- **Store settings:** choose the Lifestyle category, add contact details, and select the
  countries or regions where the app will be available.
- **App content:** complete Ads, App access, Content rating, Target audience and content,
  News apps, and every other declaration shown for the account. The app has no login, no
  in-app ads, and opens configured donations in the external browser; verify these facts
  again before submission.
- **Privacy policy and Data safety:** these are required even when declaring that no user
  data is collected (`NFR-PR-02`). Review the release build and all third-party SDKs before
  submitting the form. Location is used transiently on-device for Qibla and is not stored;
  camera frames are used on-device for QR scanning; payment data is never handled by the
  app. FCM token registration and opt-in telemetry may require declarations such as device
  identifiers or diagnostics depending on the production configuration, so do not copy
  the iOS answers without checking the shipped behavior.

The permissions declared directly in
[AndroidManifest.xml](../src/MasjidApp/Platforms/Android/AndroidManifest.xml) cover camera,
approximate foreground location, notifications, vibration, reboot restoration, and
network access. The app does not directly request background or precise location.
Dependencies can contribute additional permissions during manifest merging, so use the
generated release manifest from Step 5 for the final declaration review.

## Step 4 - Set versions and release configuration

In [MasjidApp.csproj](../src/MasjidApp/MasjidApp.csproj):

- `ApplicationDisplayVersion` is the public version name, for example `1.0.1`.
- `ApplicationVersion` is Android's integer version code. It must be greater than every
  bundle previously uploaded to this Play app.

These values are shared with iOS. The App Store workflow also increments
`ApplicationVersion`, so inspect the latest committed value and the highest version code
in **Test and release ▸ Latest releases and bundles** before building. Commit intentional
version changes so the next iOS or Android release does not reuse a number.

Release builds should use the production API configuration and a production Google Maps
key. Restrict that key in Google Cloud Console to **Maps SDK for Android**, package name
`com.goodsoftware.masjidapp`, and the SHA-1 fingerprint for every certificate in
**Protected with Play ▸ Play Store distribution ▸ Go to Play app signing ▸ App signing
key**. This app uses quantum-ready hybrid signing, so register the deployment,
hybrid-classical, and hybrid-PQC SHA-1 fingerprints. These are Google's delivery
certificates, not the local **Upload key certificate**.

## Step 5 - Build and sign the app bundle

The `.aab` signing task does not support .NET's `env:` password prefix. Put the password in
a permission-restricted file outside the repository instead:

```bash
printf 'Upload-key password: ' >&2
read -s ANDROID_SIGNING_PASSWORD
printf '\n' >&2
printf '%s' "$ANDROID_SIGNING_PASSWORD" \
  > ~/.config/masjidapp/android-signing-password
chmod 600 ~/.config/masjidapp/android-signing-password
unset ANDROID_SIGNING_PASSWORD
```

Run the publishing script. It increments `ApplicationVersion`, keeps the iOS widget build
number synchronized, builds the MAUI app project directly, verifies the AAB signature and
manifest, and uploads when a service-account JSON file is present. Android Release builds
run R8 to shrink and optimize Java bytecode; its mapping file is written to
`src/MasjidApp/bin/Release/net10.0-android/mapping.txt` and embedded automatically in the
AAB for Play deobfuscation. Supply the production
Maps key; Google Play publish builds fail when it is absent. After `az login`, the script
downloads all Android release credentials from Key Vault `masjidfeed-publish` into a
temporary directory and removes them on exit:

```bash
scripts/publish-google-play.sh
```

Set `PUBLISHING_KEY_VAULT_NAME=` only to use the legacy local credential files.

The defaults are:

| Variable | Default |
| --- | --- |
| `ANDROID_KEYSTORE` | `~/.config/masjidapp/masjidapp-upload.keystore` |
| `ANDROID_KEY_ALIAS` | `masjidapp-upload` |
| `ANDROID_SIGNING_PASSWORD_FILE` | `~/.config/masjidapp/android-signing-password` |
| `GOOGLE_PLAY_JSON_KEY` | `~/.config/masjidapp/google-play-service-account.json` |
| `PUBLISHING_KEY_VAULT_NAME` | `masjidfeed-publish` |
| `GOOGLE_PLAY_TRACK` | `internal` |
| `GOOGLE_PLAY_RELEASE_STATUS` | `draft` |

Override `BUILD_NUMBER` or `APP_VERSION` when needed. A `draft` upload must be completed
in Play Console; use `GOOGLE_PLAY_RELEASE_STATUS=completed` only when the intended rollout
has already been reviewed. If the service-account JSON file is absent, the script stops
after building and verification and prints the signed AAB path.

The uploadable file is the `*-Signed.aab` under
`src/MasjidApp/bin/Release/net10.0-android/publish/`. Do not upload the unsigned bundle.
The publishing script also creates `native-debug-symbols.zip` beside the AAB and uploads it
with Fastlane so Play can symbolize native/AOT crashes and ANRs. Keep this archive with the
release artifact; upload it manually in Play Console if the AAB is uploaded outside the
script.
Verify that the bundle is signed by the expected upload certificate:

```bash
jarsigner -verify -verbose -certs \
  src/MasjidApp/bin/Release/net10.0-android/publish/*-Signed.aab

keytool -printcert -jarfile \
  src/MasjidApp/bin/Release/net10.0-android/publish/*-Signed.aab
```

The verification must report `jar verified`, and its fingerprints must match the upload
key from Step 2. Review the permissions in the merged release manifest, including entries
contributed by NuGet packages:

```bash
grep -o 'android.permission.[^" ]*' \
  src/MasjidApp/obj/Release/net10.0-android/android/AndroidManifest.xml \
  | sort -u
```

Reconcile that list with the App content and Data safety answers. Keep the generated
bundle private until it is uploaded.

## Step 6 - Upload to an internal test

1. For the **first release only**, open **Test and release ▸ Testing ▸ Internal testing**,
  create a release, and upload the signed `.aab` manually. Google Play API clients such as
  Fastlane cannot bootstrap a new app before this upload. Confirm enrollment in Play App
  Signing and verify the upload-key certificate.
2. For later releases, run `scripts/publish-google-play.sh` locally or use the GitHub
  workflow below. Both default to a draft on the internal track.
3. Resolve every error and review warning. In particular, confirm Play accepts the target
   API level generated by the pinned .NET Android workload; Google's target API policy
   changes regularly and Play Console is authoritative at release time.
4. Add release notes, save, review the release, and select **Start rollout to Internal
   testing**.
5. Add testers or a Google Group and share the opt-in link. Install from Google Play, then
   smoke-test startup, tenant selection, prayer times, notifications, Qibla permissions,
   QR scanning, both widgets, external donation links, and the embedded map.

Internal-only releases are exempt from the published Data safety section, but closed,
open, and production tracks are not. Complete the declarations before promoting the
release.

## Publishing from GitHub

[.github/workflows/publish-google-play.yml](../.github/workflows/publish-google-play.yml)
runs the same script on an Ubuntu runner. Trigger it from **Actions ▸ Publish to Google
Play ▸ Run workflow**. Choose the track and release status deliberately; the conservative
defaults are `internal` and `draft`.

The workflow retains the signed AAB, merged manifest, permissions, upload-certificate
details, logs, and checksums as a private Actions artifact for 30 days. It deliberately
does not retain an MSBuild binary log because that could contain the production Maps key
property. After a successful upload it can commit the shared app/widget build-number bump
to the selected branch, preventing the next iOS or Android release from reusing that
version code.

### Key Vault release credentials

Store these values in Azure Key Vault `masjidfeed-publish`. The workflows use GitHub OIDC
with the non-secret Actions variables `AZURE_PUBLISH_CLIENT_ID`, `AZURE_TENANT_ID`, and
`AZURE_SUBSCRIPTION_ID`; do not create repository secrets.

| Secret | Contents |
| --- | --- |
| `android-upload-keystore-base64` | Base64 of `masjidapp-upload.keystore` |
| `android-upload-keystore-password` | Password used for both the keystore and `masjidapp-upload` key |
| `google-play-service-account-json-base64` | Base64 of the Google Play service-account JSON key |
| `google-maps-api-key` | Required production Android Maps key restricted to this app |

The setup script validates the local files and uploads all four values through Azure CLI, so
no credential is typed, printed, or placed on a command line:

```bash
printf '%s' '<restricted production Maps key>' > /tmp/maps-key

scripts/setup-google-play-credentials.sh \
  --service-account ~/Downloads/<service account key>.json \
  --maps-key /tmp/maps-key \
  --push-key-vault

rm /tmp/maps-key
```

The script installs the service-account JSON and Maps key alongside the keystore, checks
that the JSON parses and carries a `client_email`, prints that address for the Play Console
invitation in Step 2a, and uploads each value from a file. Omit `--push-key-vault` to
validate the credentials without changing the vault.

Back up the original keystore, password, and service-account JSON in private durable
storage. Key Vault base64 values are transport encodings, not backups. Never commit any of
these files (`NFR-SC-01`).

## Step 7 - Promote to production

Promote the tested release through any required closed test, then create a production
release under **Test and release ▸ Production**. Review supported devices, countries,
release notes, policy declarations, pre-launch report, and automated review findings
before selecting **Start rollout to Production**. Google review can take several days,
especially for a new account or first release.

For updates, increment `ApplicationVersion`, build with the same upload key, repeat the
internal test, and use a staged production rollout where practical. Never delete the Play
Console app to rename or recreate it: the package name is permanent.

## Related

- [scripts/publish-appstore.sh](../scripts/publish-appstore.sh) - automates Steps 2–6.
- [scripts/publish-google-play.sh](../scripts/publish-google-play.sh) - builds, verifies,
  and uploads Android App Bundles.
- [scripts/setup-google-play-credentials.sh](../scripts/setup-google-play-credentials.sh) -
  creates, verifies, and publishes the Google Play signing credentials.
- [DEVELOPMENT.md](DEVELOPMENT.md) - local device deploys and debug signing.
- [scripts/deploy-ios-device.sh](../scripts/deploy-ios-device.sh) - debug install to a
  connected device (not a distribution path).
- [Microsoft: Publish a .NET MAUI Android app to Google Play](https://learn.microsoft.com/dotnet/maui/android/deployment/publish-google-play?view=net-maui-10.0)
- [Google Play: Prepare and roll out a release](https://support.google.com/googleplay/android-developer/answer/9859348)
- [Google Play: Data safety requirements](https://support.google.com/googleplay/android-developer/answer/10787469)
