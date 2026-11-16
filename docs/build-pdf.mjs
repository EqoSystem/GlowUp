import { execFileSync } from "node:child_process";
import { readdir, readFile, mkdir, writeFile, rm } from "node:fs/promises";
import path from "node:path";
import os from "node:os";
import { fileURLToPath } from "node:url";

const docsDir = path.dirname(fileURLToPath(import.meta.url));
const output = path.join(docsDir, "GlowUp-Technical-Documentation.pdf");
const mode = process.argv[2] || "--scan";

const secretPatterns = [
  ["private-key block", /-----BEGIN [A-Z ]*PRIVATE KEY-----/g],
  ["API key value", /\bAIza[0-9A-Za-z_-]{30,}\b/g],
  ["provider token value", /\b(?:sk-[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|xox[baprs]-[A-Za-z0-9-]{20,})\b/g],
  ["JWT-like value", /\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b/g],
  ["credential assignment", /\b(?:api[_-]?key|access[_-]?token|password|private[_-]?key|client[_-]?secret|cron[_-]?secret)\b\s*[:=]\s*["'`][^"'`\r\n]{8,}["'`]/gi],
  ["bearer credential", /\bBearer\s+[A-Za-z0-9._~-]{20,}/gi]
];

async function scanMarkdown() {
  const files = (await readdir(docsDir, { withFileTypes: true }))
    .filter((entry) => entry.isFile() && entry.name.endsWith(".md"))
    .map((entry) => entry.name)
    .sort();
  const matches = [];

  for (const file of files) {
    const text = await readFile(path.join(docsDir, file), "utf8");
    const lines = text.split(/\r?\n/);
    lines.forEach((line, index) => {
      for (const [label, pattern] of secretPatterns) {
        pattern.lastIndex = 0;
        if (pattern.test(line)) matches.push({ file, line: index + 1, label });
      }
    });
  }

  console.log(`Scanned ${files.length} Markdown file(s) in ${docsDir}.`);
  if (matches.length === 0) {
    console.log("Potential secret matches: none.");
  } else {
    console.log("Potential secret matches (values are deliberately not printed):");
    for (const match of matches) console.log(`${match.file}:${match.line} — ${match.label}`);
  }
  return matches;
}

if (mode === "--scan") {
  const matches = await scanMarkdown();
  process.exitCode = matches.length ? 1 : 0;
} else if (mode === "--confirm-no-secrets") {
  const matches = await scanMarkdown();
  if (matches.length) {
    console.error("PDF build stopped. Review the redacted matches, remove any real secret, then scan again.");
    process.exitCode = 1;
  } else {
    await buildPdf();
  }
} else {
  console.error("Usage: node docs/build-pdf.mjs --scan");
  console.error("       node docs/build-pdf.mjs --confirm-no-secrets");
  process.exitCode = 2;
}

async function buildPdf() {
  const sources = [];
  for (let chapter = 0; chapter <= 11; chapter++) {
    const name = `${String(chapter).padStart(2, "0")}-${[
      "outline", "overview", "frameworks", "backend", "auth", "database",
      "keys-and-secrets", "security", "mobile-fallbacks", "mobile-ui",
      "admin-website", "flows"
    ][chapter]}.md`;
    sources.push({ name, text: await readFile(path.join(docsDir, name), "utf8") });
  }

  const tempDir = await mkdir(path.join(os.tmpdir(), `glowup-docs-${process.pid}`), { recursive: true })
    .then(() => path.join(os.tmpdir(), `glowup-docs-${process.pid}`));
  const inputParts = [];
  let diagramNumber = 0;

  try {
    for (const source of sources) {
      let text = source.text;
      const diagrams = [...text.matchAll(/^```mermaid\s*\r?\n([\s\S]*?)^```[ \t]*$/gm)];
      for (const diagram of diagrams) {
        diagramNumber += 1;
        const mermaidPath = path.join(tempDir, `diagram-${diagramNumber}.mmd`);
        const imagePath = path.join(tempDir, `diagram-${diagramNumber}.png`);
        await writeFile(mermaidPath, diagram[1], "utf8");
        execFileSync("mmdc", ["-i", mermaidPath, "-o", imagePath, "-b", "white"], { stdio: "inherit" });
        text = text.replace(diagram[0], `![Diagram ${diagramNumber}](diagram-${diagramNumber}.png)`);
      }
      inputParts.push(text);
    }

    const headerPath = path.join(tempDir, "header.tex");
    await writeFile(headerPath, [
      "\\usepackage{fancyhdr}",
      "\\pagestyle{fancy}",
      "\\fancyhf{}",
      "\\fancyfoot[C]{\\thepage}",
      "\\renewcommand{\\headrulewidth}{0pt}",
      ""
    ].join("\n"), "utf8");

    const titleAndContents = [
      "\\begin{titlepage}",
      "\\centering",
      "\\vspace*{\\fill}",
      "{\\Huge\\bfseries GlowUp\\\\Technical Documentation\\par}",
      "\\vspace{1.5cm}",
      "{\\Large Research / Capstone Project\\par}",
      "\\vspace{1cm}",
      "{\\large Unity mobile application, admin website and Firestore\\par}",
      "\\vspace*{\\fill}",
      "\\end{titlepage}",
      "\\pagenumbering{roman}",
      "\\tableofcontents",
      "\\clearpage",
      "\\pagenumbering{arabic}",
      "\\setcounter{page}{1}",
      ""
    ].join("\n");
    const combined = `${titleAndContents}\n${inputParts.join("\n\n\\clearpage\n\n")}`;
    const combinedPath = path.join(tempDir, "combined.md");
    await writeFile(combinedPath, combined, "utf8");

    execFileSync("pandoc", [
      combinedPath,
      "--from=markdown+raw_tex",
      "--standalone",
      "--number-sections",
      "--pdf-engine=xelatex",
      `--include-in-header=${headerPath}`,
      `--resource-path=${tempDir}`,
      "-V", "geometry:margin=0.8in",
      "-o", output
    ], { stdio: "inherit" });
    console.log(`Created ${output}`);
  } finally {
    await rm(tempDir, { recursive: true, force: true });
  }
}
