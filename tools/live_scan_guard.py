"""Live scan regression guard (New13 brief, item 10.0).

1. Compiles the current C# sources with Unity's compiler (tools/unity_compile.py) and runs tools/LiveScanGolden in "check" mode:
   the live scan matcher's outputs must be byte-for-byte the same as tests/live-scan-golden/golden.txt.
2. Prints `git diff --stat` for the live scan files against the baseline commit (must be empty).
3. SmartScanFlowController.cs holds the scan screen's UI AND the live matching methods. Its matching methods are compared one by one
   (source text, whitespace included) with the baseline commit: any change fails.

    python tools/live_scan_guard.py [baseline-commit]      (default: the commit tagged live-scan-baseline, else HEAD)
Exit code 1 on any difference.
"""
import os, re, subprocess, sys, tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = "GlowUp/Assets/_Project/Scripts/"
LIVE_SCAN_FILES = [SCRIPTS + p for p in [
    "Scan/ProductMatcher.cs", "Scan/ScanShortlist.cs", "Scan/ShadeCode.cs", "Scan/ProductScope.cs", "Scan/ProductRegion.cs",
    "Scan/ScanEvidence.cs", "Scan/ScanTextMerger.cs", "Scan/ScanZones.cs", "Scan/FrameQuality.cs", "Scan/FrameReader.cs",
    "Scan/PackagingMemory.cs", "Scan/PackEmbedder.cs", "Scan/TextTracer.cs", "Scan/ScanRules.cs", "Scan/ScanGuidance.cs",
    "Scan/IngredientTextParser.cs", "Scan/MlKitTextReader.cs", "Scan/MlKitBarcodeReader.cs", "Scan/OcrService.cs",
    "Data/ProductIndex.cs", "Data/TextKey.cs", "Data/ProductRepository.cs", "Data/LocalAliases.cs",
]] + ["GlowUp/Assets/Plugins/Android/GlowUpFrameReader.java"]

CONTROLLER = SCRIPTS + "Scan/SmartScanFlowController.cs"
# the live matching methods inside the scan screen's controller (the UI methods around them may change)
MATCHING_METHODS = [
    "Look", "OnReaderAnswer", "OnReaderAnswerRotated", "OnEarlyBarcode", "TakeEarlyBarcode", "TakeAnswer", "OnReading", "KeepBest",
    "StabiliseShade", "SameColour", "ProductIn", "ReadPrimaryIdentityFromOcr", "StartFirebaseProductLookup", "FirebaseProductLookup",
    "Adopt", "BrandIn", "DistinctLines", "TakeEntry", "TakeTyped", "PoolTokensFromLines", "ResetScanRankingState",
    "RotatePixels90", "RotatePixels270", "TokenMatchesWord", "CheckAndWarmBrandCandidates", "ComputeShadeMatchCounts",
    "SiblingConditionMet", "RankAndMatchCandidates", "AutoAcceptCandidate", "UpdateRankingFlow", "TryShowTimeoutShortlist",
    "ShowCandidateShortlist", "TakeBarcode", "NameFromBarcode", "StopIfNotMakeup", "StopIfNotMakeupNow", "EarlyLook", "TakeAiAnswer",
    "AdoptIfListed", "WebCheck", "UseKeptWebList", "ShadeLook", "TakePackList", "RecogniseThenFinish", "Finish",
]


def run(cmd, **kw):
    return subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, **kw)


def methods(source):
    """name -> list of full method texts (signature line to the closing brace)."""
    out = {}
    lines = source.split("\n")
    sig = re.compile(r"^\s{8}(?:(?:public|private|protected|internal|static|async|override|virtual)\s+)*[\w<>\[\],\. ]+?\s(\w+)\s*\([^;]*$")
    i = 0
    while i < len(lines):
        m = sig.match(lines[i])
        if m and not lines[i].strip().startswith(("if", "for", "while", "switch", "return", "else", "//")):
            name = m.group(1)
            depth, j, started = 0, i, False
            while j < len(lines):
                depth += lines[j].count("{") - lines[j].count("}")
                if "{" in lines[j] or "=>" in lines[j]:
                    started = True
                if started and depth <= 0 and (lines[j].rstrip().endswith("}") or lines[j].rstrip().endswith(";")):
                    break
                j += 1
            out.setdefault(name, []).append("\n".join(lines[i:j + 1]))
            i = j + 1
            continue
        i += 1
    return out


def main():
    base = sys.argv[1] if len(sys.argv) > 1 else ("live-scan-baseline" if run(["git", "rev-parse", "-q", "--verify", "live-scan-baseline"]).returncode == 0 else "HEAD")
    failed = False
    out = tempfile.mkdtemp(prefix="livescan_")
    c = run([sys.executable, "tools/unity_compile.py", out])
    print(c.stdout.strip().splitlines()[-1] if c.stdout.strip() else c.stderr)
    if c.returncode != 0:
        print(c.stdout)
        sys.exit(1)
    g = run(["dotnet", "run", "--project", "tools/LiveScanGolden", "-p:AppAssembly=" + os.path.join(out, "Assembly-CSharp.dll"), "--", "check"])
    print(g.stdout.strip().splitlines()[-1] if g.stdout.strip() else g.stderr)
    failed |= g.returncode != 0

    stat = run(["git", "diff", "--stat", base, "--"] + LIVE_SCAN_FILES).stdout.strip()
    print("git diff --stat %s -- <live scan files>: %s" % (base, stat or "(empty: no live scan file changed)"))
    failed |= bool(stat)

    old = run(["git", "show", "%s:%s" % (base, CONTROLLER)]).stdout
    with open(os.path.join(ROOT, CONTROLLER), encoding="utf-8") as f:
        new = f.read()
    mo, mn = methods(old.replace("\r\n", "\n")), methods(new.replace("\r\n", "\n"))
    changed = [n for n in MATCHING_METHODS if mo.get(n) != mn.get(n)]
    missing = [n for n in MATCHING_METHODS if n not in mo]
    if missing:
        print("note: not found in the baseline (check the list): " + ", ".join(missing))
    print("SmartScanFlowController matching methods: " + ("ALL %d IDENTICAL" % (len(MATCHING_METHODS) - len(missing)) if not changed else "CHANGED: " + ", ".join(changed)))
    failed |= bool(changed)
    print("LIVE SCAN GUARD: " + ("FAILED" if failed else "PASSED"))
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
