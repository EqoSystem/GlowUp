# ScannerEval: how well does the label reader split a label into ingredients?

Runs the app's own `IngredientTextParser` (the compiled `Assembly-CSharp.dll`, not a copy) on labelled examples and prints ingredient-level
**precision** (of the names it returned, how many are on the pack) and **recall** (of the names on the pack, how many it returned), plus
**flag recall** (of the expected names the app's built-in table rates caution or harmful, how many were found).

## Run

From the repository root, with the .NET 8 SDK installed and the Unity project compiled once (Unity writes `GlowUp/Library/ScriptAssemblies/Assembly-CSharp.dll`):

```
dotnet run --project tools/ScannerEval -- tests/fixtures/ingredient-labels
dotnet run --project tools/ScannerEval -- tests/fixtures/ingredient-labels --min-precision 0.9 --min-recall 0.9
```

The second form exits with code 1 when the **real** examples (or, if there are none, the made-up ones) fall below the minimums.
Another Unity install: add `-p:UnityManaged="D:/Unity/2022.3.62f3/Editor/Data/Managed"`.

The first call into the app code reaches a Unity native function that does not exist outside the Editor; the tool says so on the first line and carries on.

## What it measures, and what it does not

It measures the step **after** the camera and the text reader: OCR text in, list of ingredient names out. It does **not** measure glare, curved or
dark labels, or how well ML Kit reads a pack. The camera side is covered by the frame checks (`FrameQuality`: dark, moving, glare, blurry frames are
thrown away) and by merging several frames (`ScanTextMerger`), which this tool does not exercise.

`tests/fixtures/ingredient-labels/synthetic.json` holds made-up label texts in the usual layouts (wrapped lines, capitals, `May contain`, `+/-`,
`Aqua/Water/Eau`, free-of claims, OCR letter confusions). The reader scores 1.00 on them, which only shows that it copes with the layouts they were
written to contain. **Do not quote these numbers as the scanner's accuracy.**

## Adding real examples (this is what turns the number into a result)

1. Photograph a real pack the way a user would, scan it in the app, and copy the text the reader got (the scan result keeps the label text; or use the Unity console log).
2. Type the ingredient list **from the pack**, as printed (not from a website).
3. Add a file `tests/fixtures/ingredient-labels/real-<product>.json`:

```json
{ "id": "real-brand-product", "kind": "real", "note": "front, daylight, flat label",
  "ocrText": "text exactly as the reader got it",
  "expected": ["Water", "Glycerin"], "mayContain": ["CI 77491"] }
```

Do not put products or ingredient lists in a fixture that you did not read yourself from a pack.

## A realistic target (a proposal, not a measurement)

With about 20 real packs, flat or lightly curved, in normal light: ingredient-level recall and precision of **0.90 or better**, and flag recall of
**0.95 or better** for a scan the app accepted as complete. A promise of 100 % of flagged ingredients cannot be kept with OCR; what the app can do is
say clearly when it is unsure (names it does not know are shown as "Not checked", never as safe; a frame with glare or blur is not used).
