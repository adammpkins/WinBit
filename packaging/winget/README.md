# Winget packaging

Staged manifests for distributing WinBit through the Windows Package Manager (`winget`).

## Layout

```
packaging/winget/manifests/a/adammpkins/WinBit/<version>/
├── adammpkins.WinBit.yaml                  (version manifest)
├── adammpkins.WinBit.locale.en-US.yaml     (default locale)
└── adammpkins.WinBit.installer.yaml        (installer)
```

The folder structure mirrors `microsoft/winget-pkgs` exactly, so a published version's folder can be copied into a fork of that repo without any path rewriting.

`PackageIdentifier` is **`adammpkins.WinBit`** — locked in once the first version is merged upstream. End users will install via `winget install adammpkins.WinBit` or `winget install winbit` (via the `Moniker`).

## Status: not yet submitted

The current MSIX in GitHub Releases (`WinBit-v0.1.1-x64.msix` and earlier) is signed with a **self-signed certificate** (`CN=Adam M. Perkins`, issuer = subject). `microsoft/winget-pkgs` validation rejects MSIX installers whose signing chain doesn't terminate in a publicly trusted root, and `winget install`-ing such a package on a fresh machine fails with a signature error before the install begins. The v0.1.1 manifests here are staged for documentation and as a template for the first real submission.

The first submission will happen after **SignPath Foundation** issues a free OSS code-signing cert and the release workflow signs a new MSIX with it. Application status is tracked in the project plan; the SignPath approval window is typically 1–2 weeks.

## How to submit a new version (after SignPath is live)

Prerequisites:
- The new MSIX is published to GitHub Releases and signed by SignPath
- `winget` CLI installed locally (Windows Package Manager)
- `wingetcreate` installed: `winget install Microsoft.WingetCreate`
- A fork of `microsoft/winget-pkgs` on your GitHub account

Steps:

1. **Author the new version's manifest** — copy `0.1.1/` to `<new-version>/` under the same `manifests/a/adammpkins/WinBit/` path; update `PackageVersion`, `InstallerUrl`, `InstallerSha256`, `SignatureSha256`, and `PackageFamilyName`. Or use `wingetcreate update`:

   ```pwsh
   wingetcreate update adammpkins.WinBit `
     --version <new-version> `
     --urls https://github.com/adammpkins/WinBit/releases/download/v<new-version>/WinBit-v<new-version>-x64.msix
   ```

   `wingetcreate` auto-computes the hashes and family name.

2. **Validate locally:**

   ```pwsh
   winget validate packaging/winget/manifests/a/adammpkins/WinBit/<new-version>/
   ```

3. **Sandbox-install end-to-end** (proves it actually works, not just that the YAML parses):

   ```pwsh
   # In a clone of microsoft/winget-pkgs:
   .\Tools\SandboxTest.ps1 manifests\a\adammpkins\WinBit\<new-version>
   ```

4. **Open the PR** to `microsoft/winget-pkgs`. Title format: `New version: adammpkins.WinBit version <X.Y.Z>`. Validators run automatically; address any reviewer feedback.

5. After merge, `winget install adammpkins.WinBit` will resolve on any Windows 11 machine.

## Long term: automate per-release submissions

Once a manual submission has succeeded once, swap the manual flow for [`vedantmgoyal2009/winget-releaser@v2`](https://github.com/vedantmgoyal2009/winget-releaser) added to `.github/workflows/release.yml`. On every `v*` tag, after the SignPath-signed MSIX is uploaded, the action:

- Pulls the URL + computes hashes
- Forks `winget-pkgs` if needed
- Opens the update PR automatically

Future releases ship to winget with no manual steps.
