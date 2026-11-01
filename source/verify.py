#!/usr/bin/env python3
"""Compile and run API, behavioral and Harmony regression checks under Mono."""
import argparse
import os
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--managed", required=True, type=Path)
    parser.add_argument("--dotnet", required=True, type=Path)
    parser.add_argument("--csc", required=True, type=Path)
    parser.add_argument("--mono", required=True, type=Path)
    parser.add_argument("--mono-config", type=Path)
    parser.add_argument("--mono-libraries", type=Path)
    args = parser.parse_args()
    source = Path(__file__).resolve().parent
    output = source.parent / "build"
    output.mkdir(exist_ok=True)
    managed = args.managed.resolve()
    fixtures = {
        "VerifyApi": [],
        "VerifyTransport": [source / "PhotonTransport.cs"],
        "VerifyHover": [],
        "VerifyDiagnostics": [source / "DiagnosticsLog.cs", source / "LogLocation.cs"],
        "VerifyRuntime": [source / "VerifyHover.cs"],
        "VerifyObserverHud": [source / "VerifyHover.cs", source / "VerifyRuntime.cs"],
        "VerifyCamera": [source / "VerifyHover.cs"],
        "VerifyTooltips": [],
        "VerifyConnectionRetry": [source / "RetryBudget.cs", source / "VerifyHover.cs", source / "VerifyRuntime.cs"],
        "VerifyTravelShare": [source / "TravelWire.cs"],
    }
    references = ["mscorlib", "System", "System.Core"]
    environment = os.environ.copy()
    # The game's corlib is compiled for Unity and cannot replace standalone Mono's own runtime corlib.
    runtime_refs = output / "runtime-references"
    runtime_refs.mkdir(exist_ok=True)
    for dll in managed.glob("*.dll"):
        if dll.stem in ("mscorlib", "System", "System.Core"):
            continue
        link = runtime_refs / dll.name
        if link.is_symlink():
            link.unlink()
        if not link.exists():
            link.symlink_to(dll)
    search = [str(runtime_refs), str(output)]
    if args.mono_libraries:
        libraries = args.mono_libraries.resolve()
        environment["LD_LIBRARY_PATH"] = str(libraries) + os.pathsep + environment.get("LD_LIBRARY_PATH", "")
        search.insert(0, str(libraries / "mono/4.5"))
    environment["MONO_PATH"] = os.pathsep.join(search)
    reports = []
    failed = []
    for name, extra in fixtures.items():
        executable = output / (name + ".exe")
        compiler = [args.dotnet, args.csc, "/nologo", "/nostdlib+", "/target:exe", "/optimize+",
                    "/main:" + name, "/out:" + str(executable)]
        compiler += ["/r:" + str(managed / (name + ".dll")) for name in references]
        compiler += [source / (name + ".cs")] + extra
        subprocess.run([str(part) for part in compiler], check=True)
        command = [str(args.mono)]
        if args.mono_config:
            command += ["--config", str(args.mono_config)]
        command.append(str(executable))
        if name == "VerifyApi":
            command.append(str(managed))
        elif name not in ("VerifyTransport", "VerifyDiagnostics"):
            command.append(str(output / "SolastaCoopView.dll"))
        result = subprocess.run(command, env=environment, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
        (output / (name + "-check.txt")).write_text(result.stdout, encoding="utf-8")
        print(name + ": " + ("PASS" if result.returncode == 0 else "FAIL"), flush=True)
        reports.append(name + "\n" + result.stdout)
        if result.returncode:
            print(result.stdout)
            failed.append(name)
    (source.parent / "validation.txt").write_text(
        "Solasta Coop View 0.1.13\nAutomated local checks; no live game connection or Unity rendering tested.\n\n" +
        "\n".join(reports), encoding="utf-8")
    raise SystemExit(bool(failed))


if __name__ == "__main__":
    main()
