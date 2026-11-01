# Publishing this repository to GitHub

The provided project ZIP includes the **entire local Git repository**, including its `.git` directory, **11 reconstructed commits** and the `v0.1.18-source` tag. The `.bundle` download is an alternative that preserves the same history.

## Publish with GitHub CLI (Windows PowerShell)

1. Install the GitHub CLI and authenticate to **your** account:

```powershell
winget install --id GitHub.cli -e
gh auth login
```

2. Extract the Git-repository ZIP, open PowerShell **inside the folder containing `.git` and `README.md`**, then create the remote repository and push all commits:

```powershell
# Default is private, change to --public only after reviewing source and licenses.
gh repo create solasta-coop-view --private --source=. --remote=origin --push
git push origin v0.1.18-source
gh repo view --web
```

To use your own existing repository, instead run `git remote add origin https://github.com/YOUR_ACCOUNT/YOUR_REPO.git` and `git push -u origin main --tags` (ensure it is empty before pushing; no force push).

## Restore from the Git bundle instead of ZIP

```powershell
git clone .\Solasta-Coop-View-v0.1.18-history.bundle solasta-coop-view
cd solasta-coop-view
git log --oneline --max-count=12
```

If the bundle clone creates a remote pointing to the local bundle, remove it (`git remote remove origin`) before using `gh repo create`.

## Notes

- This is a **reconstruction** of feature snapshots, not the authors' original history. Commit identity is an import placeholder, not a GitHub account.
- **Do not include `Managed.zip`, proprietary Solasta game DLLs, game saves, logs, or tokens** in your commits.
- There is **no GitHub release artifact** and no certified, tested `winhttp.dll`. The repository is source-only.
- GitHub CLI documentation: https://cli.github.com/manual/gh_repo_create
