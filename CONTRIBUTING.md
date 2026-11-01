# Contributing

This is an experimental source project. Please start with a description of the symptom, exact Solasta and mod versions, and steps to reproduce. For multiplayer issues include logs from **both** players with matching timestamps, but remove personal data and credentials before uploading.

- Keep game-specific assemblies and proprietary assets out of Git.
- Preserve third-party LICENSE, UPSTREAM and MODIFICATIONS files when updating vendored components.
- Make code changes narrowly scoped to a feature and document new toggles/default values.
- Run `python source/source_sanity_check.py` for static checks. Actual builds require Windows, .NET SDK 8, MinGW-w64 and a locally installed Solasta game.
- Verify gameplay changes with a backed-up save and identical mod build on host and client.
- Before opening a pull request, state whether it was only statically checked, successfully built, or tested in a multiplayer game.
