# Windows CI and GitHub releases

The workflow `.github/workflows/windows-release.yml` builds `winhttp.dll` on a Windows runner and publishes only that DLL as an Actions artifact or a versioned GitHub Release.

## Private compilation references

Proprietary Solasta game assemblies must **never** be committed to the public mod repository or included in a public release. Use the separate private repository `sogoeslight/solasta-build-refs`. The archive `SolastaManagedRefs.zip` should contain the minimal managed DLL references from a legally installed copy of Solasta.

**Check the applicable game license before storing game DLLs in any third-party cloud service.** An alternative is compiling locally using the game's installed `Solasta_Data/Managed` folder.

### One-time setup

1. Keep `sogoeslight/solasta-build-refs` **Private**.
2. Open its Releases page, edit the pre-created `ci-build-refs` draft, attach `SolastaManagedRefs.zip`, then **publish the release inside the private repository**. Publishing a release in a private repository does **not** make its files public. Publishing is useful because a read-only token can download the asset.
3. Open https://github.com/settings/personal-access-tokens/new and create a **fine-grained personal access token**:
   - Resource owner: `sogoeslight`.
   - Repository access: **Only select repositories**, selecting `solasta-build-refs` only.
   - Repository permissions: **Contents: Read-only**, leave other permissions disabled.
   - Choose a suitable expiration and renew the token before it expires.
4. Open https://github.com/sogoeslight/solasta-coop-mod/settings/secrets/actions and add a repository **secret** named `SOLASTA_REFS_TOKEN` with that token. Never put it in a Git commit, issue, chat, artifact or workflow log.
5. Trigger **Windows build and release** in the main repository (version field blank) and confirm success. On a successful run, the runner downloads private references from `sogoeslight/solasta-build-refs`.

The `SOLASTA_REFS_TOKEN` secret is used **only** for the reference download step; compilation and publishing use the normal `GITHUB_TOKEN`.

### Temporary transition behavior

Until `SOLASTA_REFS_TOKEN` is configured, CI can temporarily use the old `ci-build-refs` draft in the main repo **only while that repo remains private**. If the main repo becomes public without the secret, the workflow deliberately fails instead of downloading from the old repository.

**Before changing the main repo to Public:** verify a successful build using the new private source, and manually delete the old `ci-build-refs` draft and its archive from the main repo. Do not publicize the repository while the old archive remains accessible there.

### Updates after a Solasta patch

If referenced assemblies change, recreate `SolastaManagedRefs.zip` from your game installation, replace the private release asset in `solasta-build-refs`, and rebuild `winhttp.dll`. Game API changes may also require code updates.

The command below requires GitHub CLI authentication with write access to the **private reference repository**:

```powershell
gh release upload ci-build-refs "$env:USERPROFILE\Downloads\SolastaManagedRefs.zip" --repo sogoeslight/solasta-build-refs --clobber
```

## Build and release

Open https://github.com/sogoeslight/solasta-coop-mod/actions/workflows/windows-release.yml and select **Run workflow**. Leave `version` empty for a temporary Actions artifact, or enter a new tag such as `v0.1.21` to create a release. Commits to `main` automatically build and validate the DLL.

A successful build saves only `winhttp.dll` in the Actions artifact. GitHub's download UI wraps the artifact in a ZIP; the GitHub Release directly exposes `winhttp.dll`.

Static validation and compilation do not replace real in-game co-op testing.
