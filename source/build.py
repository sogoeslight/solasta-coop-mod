#!/usr/bin/env python3
"""Build the managed payload and the Windows x64 single-file loader."""
import argparse
import os
from pathlib import Path
import subprocess


def run(args, **kwargs):
    subprocess.run([str(arg) for arg in args], check=True, **kwargs)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--managed", required=True, type=Path)
    parser.add_argument("--dotnet", required=True, type=Path)
    parser.add_argument("--csc", required=True, type=Path)
    parser.add_argument("--cc", default="x86_64-w64-mingw32-gcc")
    parser.add_argument("--sysroot", type=Path)
    parser.add_argument("--gcc-prefix", type=Path)
    args = parser.parse_args()
    source = Path(__file__).resolve().parent
    output = source.parent / "build"
    output.mkdir(exist_ok=True)
    payload = output / "SolastaCoopView.dll"
    refs = ["mscorlib", "System", "System.Core", "UnityEngine", "UnityEngine.CoreModule",
            "UnityEngine.IMGUIModule", "UnityEngine.InputLegacyModule", "UnityEngine.TextRenderingModule",
            "UnityEngine.ScreenCaptureModule", "UnityEngine.ImageConversionModule", "UnityEngine.AnimationModule", "UnityEngine.ParticleSystemModule", "UnityEngine.UI", "UnityEngine.UIModule"]
    command = [args.dotnet, args.csc, "/nologo", "/target:library", "/optimize+",
               "/deterministic+", "/nostdlib+", "/out:" + str(payload)]
    command += ["/reference:" + str(args.managed / (name + ".dll")) for name in refs]
    command += ["/reference:" + str(source / "vendor/harmony/0Harmony.dll")]
    command += [source / "CoopView.cs", source / "PhotonTransport.cs", source / "HoverRange.cs",
                source / "DiagnosticsLog.cs", source / "RuntimeDiagnostics.cs", source / "LogLocation.cs",
                source / "ObserverHud.cs", source / "ObserverPreview.cs", source / "HarmonySupport.cs", source / "CameraZoom.cs",
                source / "RetryBudget.cs", source / "ConnectionRetry.cs", source / "AutoFollow.cs", source / "HostMoveShare.cs", source / "AutoFastTravelVote.cs", source / "ItemTransferRange.cs", source / "TravelShare.cs", source / "TravelWire.cs", source / "LootShare.cs", source / "CombatSpeed.cs"]
    command += ["/resource:" + str(source / "vendor/harmony/0Harmony.dll") + ",SolastaCoop.Harmony"]
    run(command)

    blob = payload.read_bytes()
    (output / "payload.h").write_text(
        "static const unsigned char coop_payload[] = {\n" +
        ",\n".join(",".join(str(v) for v in blob[i:i + 24]) for i in range(0, len(blob), 24)) +
        "\n};\n", encoding="ascii")

    vendor = source / "doorstop"
    names = [line.strip() for line in (vendor / "src/windows/proxy/proxylist.txt").read_text().splitlines()
             if line.strip()]
    proxy = ["#include <windows.h>", "FARPROC coop_forwarders[%d];" % len(names),
             "void load_functions(void *dll) {"]
    proxy += ['coop_forwarders[%d] = GetProcAddress((HMODULE)dll, "%s");' % (i, name)
              for i, name in enumerate(names)]
    proxy += ["}"]
    proxy += ['__attribute__((naked)) void coop_export_%d(void) { __asm__ volatile("jmp *coop_forwarders+%d(%%rip)"); }'
              % (i, i * 8) for i in range(len(names))]
    (output / "proxy.c").write_text("\n".join(proxy), encoding="ascii")
    exports = output / "proxy.def"
    exports.write_text("LIBRARY winhttp\nEXPORTS\n" +
                       "\n".join("%s=coop_export_%d" % (name, i) for i, name in enumerate(names)) + "\n",
                       encoding="ascii")
    files = []
    for folder in ["src", "src/config", "src/util", "src/runtimes", "src/windows"]:
        files.extend(sorted((vendor / folder).glob("*.c")))
    command = [args.cc, "-shared", "-O2", "-fno-stack-protector", "-fno-builtin", "-DUNICODE",
               "-DVERBOSE=1", "-nostdlib", "-Wl,-e,DllEntry", "-Wl,--no-insert-timestamp",
               "-I" + str(output)]
    if args.sysroot:
        command += ["--sysroot=" + str(args.sysroot), "-I" + str(args.sysroot / "include"),
                    "-L" + str(args.sysroot / "lib")]
    if args.gcc_prefix:
        command += ["-B" + str(args.gcc_prefix) + os.sep]
    command += files + [output / "proxy.c", exports, "-lshell32", "-lkernel32", "-luser32", "-lole32", "-lgcc",
                        "-o", output / "winhttp.dll"]
    run(command)
    print("Built:", output / "winhttp.dll")


if __name__ == "__main__":
    main()
