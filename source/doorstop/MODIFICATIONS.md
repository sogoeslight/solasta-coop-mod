# Local changes for Solasta Coop View

Based on the commit in UPSTREAM.txt. Modified on 2026-10-21.

- src/bootstrap.c: loads the build-generated embedded managed payload instead of
  reading a target assembly from disk; fixes a narrow/wide fopen mode literal.
- src/windows/config.c: uses an enabled embedded-payload configuration, without
  reading doorstop_config.ini or accepting an external target assembly.
- src/windows/entrypoint.c: does not require the virtual payload filename to exist.
- src/crt.h and src/windows/wincrt.h: avoid MinGW macro redefinition warnings.
- src/windows/wincrt.c: uses the DWORD type required by ReadFile for byte count.
- The external build.py generates ABI-preserving x64 naked jump forwarders rather
  than the upstream zero-argument C proxy wrappers. It also generates payload.h.

The complete corresponding loader source is included. Rebuild with build.py and
the prerequisites described in README.ru.md. No proprietary game binaries are
required for the native loader itself; the managed module is compiled against
Unity assemblies supplied with the user's game.

## Version 0.1.5 logging changes

- src/windows/logger.h: replaced direct writes with a synchronized logger using local format buffers and UTF-8 byte counts.
- src/windows/coop_logging.c: buffers early messages and starts a worker without waiting in DllMain. The worker resolves the Windows Downloads Known Folder outside the loader lock, writes versioned logs under SolastaCoopView, limits each file to 1 MiB, and retains twenty logs of this version.
- build.py: links the Windows COM library and the static GCC stack-probe helper required by local log buffers. No extra compiler runtime DLL is required.

## Version 0.1.6

- src/windows/coop_logging.c: updated the native log filename and retention pattern to version 0.1.6.

## Version 0.1.7

- src/windows/coop_logging.c: updated native log filenames and the retention pattern to version 0.1.7.

## Versions 0.1.8 and 0.1.9

- src/windows/coop_logging.c: updated native log filenames and retention patterns to the corresponding version.
- Managed payload updates are embedded by build.py; no additional native loader behavior changes.

## Version 0.1.10

- Update native per-version loader log filenames from 0.1.9 to 0.1.10.

## Version 0.1.11

- Updated native loader log filenames to 0.1.11 to match the managed code if rebuilt. This archive is source-only and does not include a rebuilt native binary.
