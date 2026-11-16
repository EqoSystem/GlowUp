"""Compile the Unity project's C# assemblies with Unity's own Roslyn compiler, outside the Editor.

Reads the file list, references and defines from the .csproj files Unity writes (GlowUp/Assembly-CSharp.csproj and
GlowUp/Assembly-CSharp-Editor.csproj), so it checks exactly what Unity would compile. New .cs files that are not yet in
the .csproj (Unity regenerates it only when the Editor runs) are picked up from Assets/_Project too.

    python tools/unity_compile.py <out_dir> [--editor]

Writes <out_dir>/Assembly-CSharp.dll (and Assembly-CSharp-Editor.dll with --editor). Exit code 1 on a compile error.
"""
import os, re, subprocess, sys, xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = os.path.join(ROOT, "GlowUp")
UNITY = os.environ.get("UNITY_EDITOR_DATA", r"C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Data")
DOTNET = os.path.join(UNITY, "NetCoreRuntime", "dotnet.exe")
CSC = os.path.join(UNITY, "DotNetSdkRoslyn", "csc.dll")


def read_csproj(name):
    tree = ET.parse(os.path.join(PROJECT, name))
    ns = {"m": tree.getroot().tag.split("}")[0].strip("{")} if tree.getroot().tag.startswith("{") else {}
    q = (lambda t: "m:" + t) if ns else (lambda t: t)
    files = [c.get("Include") for c in tree.iter() if c.tag.endswith("Compile") and c.get("Include")]
    refs = []
    for r in tree.iter():
        if r.tag.endswith("HintPath") and r.text:
            refs.append(r.text)
    defines = ""
    for d in tree.iter():
        if d.tag.endswith("DefineConstants") and d.text:
            defines = d.text
            break
    return files, refs, defines


def compile_one(csproj, out_dll, extra_refs=(), extra_files=()):
    files, refs, defines = read_csproj(csproj)
    files = list(dict.fromkeys(files + list(extra_files)))
    refs = [r for r in refs if not os.path.basename(r).startswith(os.path.splitext(os.path.basename(out_dll))[0] + ".")]
    # an assembly Unity built itself (Library/ScriptAssemblies) is replaced by ours when we built it
    refs = [r for r in refs if not any(os.path.basename(r) == os.path.basename(e) for e in extra_refs)] + list(extra_refs)
    rsp = out_dll + ".rsp"
    with open(rsp, "w", encoding="utf-8") as f:
        f.write("-target:library\n-nologo\n-langversion:9.0\n-nostdlib+\n-noconfig\n-deterministic\n-warn:0\n-nowarn:0169,USG0001,CS0618\n")
        f.write("-out:\"%s\"\n" % out_dll)
        f.write("-define:%s\n" % defines)
        for r in refs:
            path = r if os.path.isabs(r) else os.path.join(PROJECT, r)
            f.write("-r:\"%s\"\n" % path)
        for c in files:
            f.write("\"%s\"\n" % (c if os.path.isabs(c) else os.path.join(PROJECT, c)))
    p = subprocess.run([DOTNET, CSC, "@" + rsp], capture_output=True, text=True)
    errors = [l for l in (p.stdout + p.stderr).splitlines() if ": error " in l]
    for l in errors[:60]:
        print(l)
    return p.returncode == 0 and not errors


def new_files(listed, under, editor):
    listed_norm = {os.path.normcase(os.path.normpath(os.path.join(PROJECT, f))) for f in listed}
    out = []
    base = os.path.join(PROJECT, under)
    for d, _, fs in os.walk(base):
        is_editor = os.sep + "Editor" in d
        if is_editor != editor:
            continue
        for f in fs:
            if f.endswith(".cs"):
                full = os.path.normcase(os.path.normpath(os.path.join(d, f)))
                if full not in listed_norm:
                    out.append(os.path.join(d, f))
    return out


def main():
    out = os.path.abspath(sys.argv[1])
    os.makedirs(out, exist_ok=True)
    runtime_files, _, _ = read_csproj("Assembly-CSharp.csproj")
    extra = new_files(runtime_files, os.path.join("Assets", "_Project"), editor=False)
    dll = os.path.join(out, "Assembly-CSharp.dll")
    ok = compile_one("Assembly-CSharp.csproj", dll, extra_files=extra)
    print(("OK " if ok else "FAILED ") + dll + (" (+%d new files)" % len(extra) if extra else ""))
    if ok and "--editor" in sys.argv:
        editor_files, _, _ = read_csproj("Assembly-CSharp-Editor.csproj")
        extra_e = new_files(editor_files, os.path.join("Assets", "_Project"), editor=True)
        edll = os.path.join(out, "Assembly-CSharp-Editor.dll")
        ok = compile_one("Assembly-CSharp-Editor.csproj", edll, extra_refs=[dll], extra_files=extra_e)
        print(("OK " if ok else "FAILED ") + edll + (" (+%d new files)" % len(extra_e) if extra_e else ""))
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
