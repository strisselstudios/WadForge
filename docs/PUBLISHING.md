# Publishing the repository and releases

## Create the GitHub repository

1. Sign in to GitHub as `strisselstudios`.
2. Create a new public repository named `WadForge`.
3. Do not initialize it with a README, `.gitignore`, or license.
4. Extract the GitHub-ready package to a local folder.
5. Open Developer PowerShell for Visual Studio 2022 in that folder.
6. Run:

```powershell
git init -b main
git add --all
git diff --cached --check
git commit -m "Initial public source release"
git remote add origin "https://github.com/strisselstudios/WadForge.git"
git push -u origin main
```

## Create release assets

The ready-to-run suite ZIP is not committed to the source repository. Upload it to GitHub Releases.

For release `v1.0.0`, upload:

```text
WadForge-TrenchBroom-Suite-v1.0.0.zip
WadForge-TrenchBroom-Source-v1.0.0.zip
SHA256SUMS.txt
RELEASE-NOTES-v1.0.0.md
```

The release tag should be `v1.0.0`.

## Long-name reminder

Every WAD alias manifest must remain beside the exact WAD path loaded by TrenchBroom:

```text
Example.wad
Example.wad.wadforge.json
```
