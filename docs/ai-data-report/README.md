# GlowUp AI, data and AR report (PDF generator)

`GlowUp_AI_Data_Report.pdf` is generated from the files in this folder.

## Regenerate

Needs Node.js 20+ and Google Chrome (path in `CHROME`, default `C:/Program Files/Google/Chrome/Application/chrome.exe`).

```
npm install            # once: mermaid and pdfjs-dist (dev tools, stay in this folder)
node build.mjs         # writes build/document.html and GlowUp_AI_Data_Report.pdf
node check.mjs 1.0     # renders every page to build/pages/page-NN.png
node tools/sheet.mjs   # puts the pages on contact sheets build/sheet-N.png for a quick look
```

## Where things are

- `src/parts/*.html` the text, one file per part, in file-name order; `src/style.css` the print layout (A4)
- `diagrams/*.mmd` Mermaid sources of the figures
- `proposed.firestore.rules` hardened catalogue rules: a PROPOSAL, not published
- `ar-plan.md` audit and plan of the AR upgrade; `decisions.md` decisions taken without asking; `CHANGES_EXPLAINED.md` the plain-language summary

Shorthand in the parts: `{V:path}` Verified, `{I:basis}` Inferred, `{N:what}` Not found (collected in Part 9 automatically).
Never put secrets in the parts: names of keys only, never values.
