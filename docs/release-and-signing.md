# Windows release and optional code signing

The GitHub Actions workflow at `.github/workflows/release.yml` runs when a `v*` tag is pushed. It checks that the tag version matches `chrome-extension/manifest.json`, builds the app, Inno Setup installer, and Chrome Web Store ZIP on `windows-latest`, then uploads the two distributable files both as a workflow artifact and as GitHub Release assets.

## Create a release

1. Update `chrome-extension/manifest.json` and any installer version references needed for the next release.
2. Commit and push the release-ready changes.
3. Create and push a matching tag, for example `v1.0.2` for manifest version `1.0.2`.
4. Review the Windows release workflow. The installer and extension ZIP appear in the GitHub Release and in the workflow run's artifacts.

The workflow stops before building if the tag and manifest versions differ. It does not publish to the Chrome Web Store.

## Optional installer signing

Signing is optional. With neither secret configured, the workflow states in its summary that the installer is unsigned and continues. If one secret is set without the other, signing fails clearly instead of silently producing a release that appears signed.

Add both repository or organization Actions secrets to enable signing:

- `WINDOWS_PFX_BASE64`: the complete code-signing PFX file encoded as one Base64 string. Generate it locally with PowerShell: `[Convert]::ToBase64String([IO.File]::ReadAllBytes('certificate.pfx'))`. Do not commit the PFX or generated Base64 text. The encoded certificate must fit GitHub's secret size limit.
- `WINDOWS_PFX_PASSWORD`: the PFX import password.

The workflow writes the PFX only to the runner's temporary directory, signs `BankMode-Setup.exe` with SHA-256 and a timestamp, verifies the signature, and removes the temporary PFX in a `finally` block. The extension ZIP is not Authenticode-signed. Signing secrets are not required for local builds, and this workflow does not buy a certificate or upload the extension to the Chrome Web Store.

Use a valid code-signing certificate issued to the publisher. Keep its private key and password restricted to trusted release administrators, rotate the certificate when needed, and remove both secrets to return to unsigned releases. Signing can improve publisher identification, but does not guarantee that SmartScreen warnings will disappear.
