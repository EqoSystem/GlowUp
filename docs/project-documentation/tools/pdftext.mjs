// Prints the text of a PDF, page by page: node tools/pdftext.mjs <file.pdf> [firstPage] [lastPage]
import { getDocument } from "pdfjs-dist/legacy/build/pdf.mjs";
import { readFileSync } from "node:fs";
const [file, first = "1", last] = process.argv.slice(2);
const doc = await getDocument({ data: new Uint8Array(readFileSync(file)), verbosity: 0 }).promise;
const end = Math.min(doc.numPages, Number(last || doc.numPages));
console.log(`# ${file}: ${doc.numPages} pages`);
for (let p = Number(first); p <= end; p++) {
  const page = await doc.getPage(p);
  const content = await page.getTextContent();
  let text = "", lastY = null;
  for (const item of content.items) {
    const y = item.transform?.[5];
    if (lastY !== null && Math.abs(y - lastY) > 2) text += "\n";
    text += item.str; lastY = y;
  }
  console.log(`\n--- page ${p}\n${text}`);
}
