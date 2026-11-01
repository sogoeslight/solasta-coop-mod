# Cloud builds and GitHub Releases

The workflow at `.github/workflows/windows-release.yml` can compile `winhttp.dll` on a GitHub-hosted Windows x64 runner, save a downloadable Actions artifact, and publish a prerelease for a version tag.

**Prerequisite:** Solasta's managed reference assemblies are not included in this repository or preinstalled on a GitHub runner. The owner must provide their own legally usable private compilation references. They never appear in output artifacts. Keep the repository private and do not distribute third-party game assemblies.

## One-time setup: prepare reference archive

Run this in PowerShell on the Windows PC where Solasta is installed. Adjust the game path if necessary.

```powershell
$ErrorActionPreference = "Stop"
$managed = "B:\Games\Steam\steamapps\common\Slasta_COTM\Solasta_Data\Managed"
$work = Join-Path $env:TEMP "Solasta-CI-References"
$zip = Join-Path $env:USERPROFILE "Downloads\SolastaManagedRefs.zip"

$names = @(
    "Assembly-CSharp",
    "mscorlib",
    "System",
    "System.Core",
    "UnityEngine",
    "UnityEngine.CoreModule",
    "UnityEngine.IMGUIModule",
    "UnityEngine.InputLegacyModule",
    "UnityEngine.TextRenderingModule",
    "UnityEngine.ScreenCaptureModule",
    "UnityEngine.ImageConversionModule",
    "UnityEngine.AnimationModule",
    "UnityEngine.ParticleSystemModule",
    "UnityEngine.UI",
    "UnityEngine.UIModule"
)

if (-not (Test-Path $managed)) { throw "Missing game directory: $managed" }
if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null

foreach ($name in $names) {
    $file = Join-Path $managed "$name.dll"
    if (-not (Test-Path $file)) { throw "Missing $file" }
    Copy-Item -LiteralPath $file -Destination $work
}

Compress-Archive -Path (Join-Path $work "*.dll") -DestinationPath $zip -Force
Write-Host "Created: $zip"
```

## Upload private references once

Keep the repository **private**, check the game's license, and never upload this archive to a public release. If you are allowed to store the references on GitHub, create a **draft** release, using GitHub CLI after `gh auth login`:

```powershell
$repo = "sogoeslight/solasta-coop-mod"
$zip = Join-Path $env:USERPROFILE "Downloads\SolastaManagedRefs.zip"

gh release create ci-build-refs $zip --repo $repo --draft --title "Private CI build references" --notes "Build-only references. Do not publish."
```

For a future game update, rebuild the archive and replace that draft's asset:

```powershell
gh release upload ci-build-refs $zip --repo sogoeslight/solasta-coop-mod --clobber
```

The workflow downloads the private asset with its ephemeral `GITHUB_TOKEN`. You do not need a personal access token. The draft release must stay unpublished.

**Alternative with no game DLL upload:** set up a Windows self-hosted Actions runner on your game PC and adapt the workflow's reference path to its local `Solasta_Data\Managed`. This is safer if you are unsure about the game's license.

## Manual cloud build

Open [Actions → Windows build and release](https://github.com/sogoeslight/solasta-coop-mod/actions/workflows/windows-release.yml), click **Run workflow** on `main`, and leave **version** blank.

On success, download the artifact named `solasta-coop-mod-windows-x64`. It contains **only `winhttp.dll`**. GitHub Actions packages artifacts as a downloadable archive, but the build does not generate a second mod ZIP or checksums. Artifacts have 30-day retention.

## Publish a version

**From the UI:** Run the workflow, entering a fresh version such as `v0.1.18`. Only after a successful compilation does the workflow create and publish a prerelease.

**With Git tags:** Push a tag starting with `v`:

```powershell
git checkout main
git pull
git tag v0.1.19
git push origin v0.1.19
```

A successful tag-triggered build creates a GitHub prerelease and uploads **only `winhttp.dll`**. Existing release assets are never overwritten.

## Limitations and troubleshooting

- Static source checks cannot verify compilation or actual co-op behavior.
- A failing C# or GCC build blocks release publication; inspect the Action run logs.
- `ci-build-refs` must be a private draft release containing exactly `SolastaManagedRefs.zip`.
- After Solasta updates, compare the installed managed DLLs against the archived compilation references. If `Assembly-CSharp.dll` or any referenced Unity/system DLL has changed, rebuild `SolastaManagedRefs.zip`, replace the private draft asset with `gh release upload ci-build-refs $zip --repo sogoeslight/solasta-coop-mod --clobber`, and rebuild the mod. Resource-only updates generally do not require refreshing compilation references. Changes to game APIs may also require updating the mod's C# code, and compilation alone does not guarantee compatibility.
- Do not commit or distribute Solasta-managed game DLLs.
- This is an experimental project. Keep backups of your game saves and test both clients on the same build.
