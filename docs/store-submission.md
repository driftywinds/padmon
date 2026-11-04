# Submitting padmon to Microsoft Store

This is the practical path from the repository workflow to a published MSIX
submission. The workflow produces the Store package; Partner Center validates,
signs, and distributes it.

## 1. What the workflow produces

Every run of **Build and release padmon** now builds:

| File | Purpose |
| --- | --- |
| `Padmon_<version>_x64.msix` | The single-architecture MSIX package. Useful for inspection and local signing. |
| `Padmon_<version>_x64.msixupload` | The recommended upload file for Partner Center. It contains the `.msix` and an optional `.appxsym` symbol package. |
| `padmon-win-x64.zip` | Portable folder build. |
| `Padmon-portable.exe` | Portable single-file launcher. |

The MSIX files are in the **padmon-msix** Actions artifact and are attached to a
tagged GitHub release. A package built from a tag such as `v1.0.0` gets package
version `1.0.0.0`.

The workflow does **not** sign the MSIX. That is correct for Store submission:
Microsoft Store re-signs accepted packages. The unsigned `.msix` is not intended
to be double-clicked on a normal PC; use the Store or a locally trusted
development certificate for that.

## 2. Reserve the app in Partner Center

1. Sign in at <https://partner.microsoft.com/dashboard>.
2. Enrol in the **Windows & Xbox** / **Apps and games** programme if you have not
   already. A developer account may require identity verification and a
   registration fee; use the fee shown in your account, not an old guide.
3. Select **New product** → **MSIX or PWA app**.
4. Reserve the name **padmon**. If it is unavailable, choose the Store display
   name you want. The display name does not have to be the package identity name.
5. Open the product's **Product identity** page. Copy these three values exactly:

   - **Package/Identity/Name** — for example `driftywinds.padmon`
   - **Package/Identity/Publisher** — a full subject such as
     `CN=12345678-1234-1234-1234-123456789abc`
   - **Package/Properties/PublisherDisplayName**

   The Publisher value must include `CN=`. Do not substitute the publisher
   display name for it.

## 3. Put the identity into GitHub

In the repository, open **Settings** → **Secrets and variables** → **Actions** →
**Variables** and add:

| Variable | Value |
| --- | --- |
| `MSIX_IDENTITY_NAME` | `Package/Identity/Name` from Partner Center |
| `MSIX_PUBLISHER` | `Package/Identity/Publisher`, including `CN=` |
| `MSIX_PUBLISHER_DISPLAY_NAME` | `Package/Properties/PublisherDisplayName` |

The workflow has preview fallbacks (`driftywinds.padmon` and
`CN=padmon-preview`) so it can build locally. A package built with those
fallbacks is **not** Store-ready. Confirm the variables are set before making a
release.

## 4. Build the submission package

1. Open **Actions** → **Build and release padmon** → **Run workflow**.
2. Enter a version such as `v1.0.0` in the optional version field. This creates
   a GitHub release and uploads the MSIX alongside the portable builds.
3. Leave the version empty to build Actions artifacts only.
4. Wait for the run, then download the **padmon-msix** artifact.

The upload file is:

```text
Padmon_1.0.0.0_x64.msixupload
```

Use the `.msixupload` in Partner Center. The `.msix` is there if you need to
inspect the package or sign it for a local test.

### Version rules

- Use three or four numeric parts: `1.0.0` becomes `1.0.0.0`; `1.2.3.4` is kept.
- The package version must increase for every Store update.
- Avoid prerelease tags for Store submissions. `v1.0.0` is better than
  `v1.0.0-beta`.
- The workflow strips a leading `v`; do not put the `v` in the package identity.

## 5. Create the submission

In Partner Center:

1. Open the padmon product and select **Start submission**.
2. **Packages** → upload `Padmon_1.0.0.0_x64.msixupload`.
3. Confirm Partner Center detects one x64 package and no validation errors.
4. Complete **Store listing**:
   - **Display name:** padmon
   - **Short description / description:** use the README wording. A short draft:
     > See every controller connected to Windows, remember the ones you have
     > used before, and inspect buttons, sticks, triggers, and raw axes in one
     > local app.
   - **Screenshots:** capture at **1920×1080** (or at least 1366×768). The
     existing repository screenshots are 1180×760, so capture fresh ones from a
     maximised window. Include the Controllers, Tester, and History views.
   - **Store logo:** the package contains `StoreLogo.png`; Partner Center may
     also ask for listing artwork. Keep the dark background and cyan mark.
   - **Support contact:** `https://github.com/driftywinds/padmon/issues`
   - **Privacy policy:** `https://github.com/driftywinds/padmon/blob/main/PRIVACY.md`
     (or publish that file on a dedicated page).
5. **Properties:**
   - Category: **Utilities & tools** (choose the closest system-utility
     subcategory).
   - System requirements: Windows 11 22H2 or newer, x64.
   - Declare no in-app purchases, no ads, no telemetry, and no account.
   - Export compliance: padmon uses only the platform's standard HTTPS/TLS
     stack; it does not implement custom cryptography.
6. **Age rating:** complete the questionnaire. padmon has no user-generated
   content, online interaction, gambling, or mature content. The expected result
   is suitable for all ages.
7. **Pricing and availability:** Free, all markets, Windows 11 Desktop x64.
8. **Submission options:** submit for certification. Use gradual rollout later
   for updates if you want a safety window.

## 6. Notes for certification

Partner Center has a **Notes for certification** field. This draft explains the
unusual parts of a full-trust controller monitor:

```text
padmon is a local controller monitor. It requires no account and no network
connection. To test, launch padmon and open the Controllers page; a connected
controller appears with a live tester. A controller is optional for launching
the app.

The package requests the runFullTrust restricted capability because padmon is a
WinUI 3 desktop app that reads controller input through SDL3 and Win32 HID APIs,
hosts a Win32 notification-area icon, and uses a packaged startup task for the
optional "Start with Windows" setting.

The Store build does not check GitHub and cannot replace its own files. Updates
are delivered by Microsoft Store. Settings, controller names, and history are
stored in the package's local data folder and removed when the package is
uninstalled.
```

If a reviewer cannot attach a controller, confirm that the empty state and the
rest of the app still launch and that no controller is required to complete
first-run.

Couch Control is intentionally not available in the Store package. Packaged builds
detect package identity and hide the Couch Control navigation item; the runtime also
refuses to create the input-injection engine. The portable and normal-install builds
retain the feature. There is no keyboard or mouse injection code path active in the
Store build.

## 7. After certification

When the submission is published, the Store page uses the package identity and
the listing metadata. Future releases:

1. Bump the app version, for example `v1.0.1`.
2. Run the workflow with that version.
3. Upload the new `.msixupload` in a new submission.
4. Submit. Store signs and distributes it.

Do not publish a new GitHub portable release with the same version as a Store
package if you want users to be able to tell them apart. Keep the version in
sync.

## 8. Local testing

The Store upload file should stay unsigned. To test the package locally on a
development PC:

1. Create a self-signed code-signing certificate whose subject exactly matches
   the `Publisher` in the package.
2. Trust the certificate in **Trusted People** on the test PC.
3. Sign the `.msix` with `signtool.exe`:

   ```powershell
   signtool sign /fd SHA256 /f padmon-test.pfx /p "<password>" `
     /tr http://timestamp.digicert.com /td SHA256 `
     Padmon_1.0.0.0_x64.msix
   ```

4. Install it with **App Installer** or `Add-AppxPackage`.

`MakeAppx.exe` and `SignTool.exe` are available from the Windows SDK Build Tools
package restored with the app, or from a Windows SDK installation.

The repository package script can sign a package when given a certificate:

```powershell
pwsh tools/package-msix.ps1 `
  -PublishDirectory artifacts/padmon-msix-publish `
  -OutputDirectory artifacts/padmon-msix `
  -Version 1.0.0 `
  -IdentityName "<Package/Identity/Name>" `
  -Publisher "<Package/Identity/Publisher>" `
  -PublisherDisplayName "<PublisherDisplayName>" `
  -CertificatePath .\padmon-test.pfx `
  -CertificatePassword "<password>"
```

## 9. Troubleshooting

### Partner Center rejects the identity

The `Identity Name`, `Publisher`, and `Publisher Display Name` in the package
must match the reserved product. Compare the values on the Product identity page
with the `AppxManifest.xml` inside the package. Re-run the workflow after fixing
the repository variables.

### The package is rejected for `runFullTrust`

That capability is expected for this app. Use the certification notes above. If
the reviewer asks for more detail, explain that controller HID access, the Win32
tray icon, and the startup task are desktop APIs not available to an AppContainer
app.

### The `.msix` will not install by double-clicking

It is unsigned, which is correct for Store submission. Sign it with a trusted
development certificate or upload the `.msixupload` to Partner Center.

### The package passes validation but the app fails at launch

Check that the package contains `resources.pri` at its root, not only
`Padmon.pri`. The workflow publishes the MSIX layout with
`ProjectPriFileName=resources.pri` and the package identity as the resource map,
then packages that layout. Also confirm the package contains `Padmon.exe`,
`SDL3.dll`, and the Windows App SDK native DLLs.

### Explorer still shows the old icon

Windows caches shell icons. The package and executable have the new icon;
restart Explorer or refresh the icon cache after installing a new build.
