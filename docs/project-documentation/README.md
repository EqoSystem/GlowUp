# GlowUp project documentation (PDF generator)

`GlowUp_Project_Documentation.pdf` is generated from the files in this folder. Nothing outside this folder is changed.

## Regenerate

Needs Node.js 20+ and Google Chrome (path in `CHROME`, default `C:/Program Files/Google/Chrome/Application/chrome.exe`).

```
npm install          # once: mermaid and pdfjs-dist (dev tools, stay in this folder)
node build.mjs       # writes build/document.html and GlowUp_Project_Documentation.pdf
node check.mjs       # renders every page to build/pages/page-NN.png for a visual check
```

`build.mjs` prints twice to fill the contents with page numbers, then prints a final copy without the hidden page markers.

## Where things are

- `src/parts/*.html` the text, one file per part, in file-name order
- `src/style.css` print layout (A4)
- `diagrams/*.mmd` Mermaid sources of the 24 figures
- `tools/pdftext.mjs` prints the text of any PDF (used to read the existing PDFs in `docs/`)

## Writing shorthand in the parts

- `{V:path}` Verified (seen in code/config/data), `{I:basis}` Inferred, `{N:what}` Not found.
  Every `{N:...}` is collected into Appendix D automatically.
- `<figure data-diagram="name"><figcaption>...</figcaption></figure>` inlines `diagrams/name.mmd` with a figure number.
- `<table data-caption="...">` gets a table number.

Never put secrets in the parts: names of keys only, never values.
