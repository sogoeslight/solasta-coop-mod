# Publishing Solasta Coop Mod on Nexus Mods

This is the author setup guide. GitHub Releases continue to contain only `winhttp.dll`. Nexus Mods receives a ZIP containing that one DLL.

## First publication

1. Sign in to [Nexus Mods](https://www.nexusmods.com/) and select **Upload > Upload mod**.
2. Select **Solasta: Crown of the Magister** and name the mod **Solasta Coop Mod**.
3. Choose **Gameplay** and create a draft.
4. Add an English description, installation instructions, and at least one real screenshot of F8 settings.
5. In **Files**, upload `Solasta-Coop-Mod-v0.1.20-Nexus.zip` as the initial **Main File**, version `0.1.20`.
6. Review **Permissions**, **Requirements**, and the Nexus file submission rules, then publish the page.

You can build the ZIP locally from the GitHub Release with PowerShell:

```powershell
$repo = 'sogoeslight/solasta-coop-mod'
$version = '0.1.20'
gh release download "v$version" --repo $repo --pattern 'winhttp.dll' --dir '.\nexus-build'
Compress-Archive -Path '.\nexus-build\winhttp.dll' -DestinationPath ".\Solasta-Coop-Mod-v$version-Nexus.zip" -Force
```

**Distribution notice:** The mod includes a modified Unity Doorstop loader and uses Harmony. Respect the upstream licenses and provide the corresponding loader source to users. Do not add the game's own managed assemblies to Nexus or a public GitHub repo. If the GitHub repository remains private, publish a separate corresponding-source archive or other accessible source location before distributing the binary.

## Prepare automated updates

The file uploader supports **updates to an existing Nexus Main File**, not creation of a brand-new mod page.

1. Locate the **File ID**, not just the Mod ID, on the Nexus mod's Files tab. Select **Advanced** or inspect the file details in **Manage Files**.
2. Generate a personal API key in [Nexus Mods API Access](https://www.nexusmods.com/settings/api-keys).
3. In [GitHub Actions secrets](https://github.com/sogoeslight/solasta-coop-mod/settings/secrets/actions), create a repository **secret** called `NEXUSMODS_API_KEY`, containing the key. Never paste it into an issue, commit, discussion or chat.
4. In [GitHub Actions variables](https://github.com/sogoeslight/solasta-coop-mod/settings/variables/actions), create a repository **variable** called `NEXUSMODS_FILE_ID` and set it to the numeric existing Main File ID.
5. Open [Upload release to Nexus Mods](https://github.com/sogoeslight/solasta-coop-mod/actions/workflows/nexus-upload.yml), select **Run workflow**, and type a **new** release version without the `v` prefix, such as `0.1.21`.

The workflow downloads `winhttp.dll` from that GitHub Release, creates a one-file ZIP, then uses [Nexus-Mods/upload-action](https://github.com/Nexus-Mods/upload-action) to publish it to the existing Nexus file.

**Do not run the workflow for version 0.1.20 after uploading that version manually.** That could upload an unnecessary duplicate. Start automation on the next new version.

## Notes

- The automation is manual until you explicitly enable an automatic release-to-Nexus step.
- Nexus may quarantine DLL-based uploads for inspection. Follow their moderation guidance if that happens.
- The Nexus action uses versioned GitHub Releases; it does not rebuild the mod and does not change GitHub Release assets.
- Automatic Vortex/mod-manager installation is disabled because this mod requires placing a proxy DLL beside `Solasta.exe`.
