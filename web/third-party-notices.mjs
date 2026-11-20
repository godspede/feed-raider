// Writes the licence notices of every npm package esbuild put into a bundle, taken from the build's metafile.
import { readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';

// a package's own licence and notice files; a `licenses/` folder holds the licences of code it took from elsewhere
const LICENCE_FILE = /^(licen[cs]e|copying|notice)(-[\w.]+)?(\.(md|txt|markdown))?$/i;
const LICENCES_DIR = /^licen[cs]es$/i;
// the last node_modules/<name> or node_modules/@scope/<name> in an input path is the package that file belongs to
const PACKAGE_DIR = /^(.*node_modules\/(?:@[^/]+\/)?[^/]+)\//;
const RULE = '='.repeat(78);

/** The package directory an esbuild input path sits in, or null for the project's own source files. */
export function packageDirOf(inputPath) {
  const m = inputPath.match(PACKAGE_DIR);
  return m ? m[1] : null;
}

/** The package directories with at least one file in the metafile output `outfile`, each once, in no set order. */
export function bundledPackageDirs(metafile, outfile) {
  const output = metafile.outputs[outfile];
  if (!output) throw new Error(`the metafile has no output ${outfile}; it has ${Object.keys(metafile.outputs).join(', ')}`);
  const dirs = new Set(Object.keys(output.inputs).map(packageDirOf).filter((d) => d !== null));
  return [...dirs];
}

/** Reads a package directory: its name, version and licence field from package.json, and the text of each licence or notice file. */
export function readPackage(dir) {
  const pkg = JSON.parse(readFileSync(join(dir, 'package.json'), 'utf8'));
  const files = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isFile() && LICENCE_FILE.test(entry.name)) files.push(entry.name);
    if (entry.isDirectory() && LICENCES_DIR.test(entry.name)) {
      for (const inner of readdirSync(join(dir, entry.name))) files.push(join(entry.name, inner).replaceAll('\\', '/'));
    }
  }
  files.sort();
  const texts = files.map((file) => ({ file, text: readFileSync(join(dir, file), 'utf8').trim() }));
  return { name: pkg.name, version: pkg.version, license: pkg.license ?? null, texts };
}

/** Compares two strings by code unit, so every machine and locale sorts them the same way. */
function byCodeUnit(a, b) {
  return a < b ? -1 : a > b ? 1 : 0;
}

/** Orders packages by name, then version (two copies of one package can sit in different node_modules folders). */
function byNameThenVersion(a, b) {
  return byCodeUnit(a.name, b.name) || byCodeUnit(a.version, b.version);
}

/** One package's section: a ruled heading with name, version and licence, then each licence or notice file in full. */
function section(pkg) {
  const head = `${RULE}\n${pkg.name} ${pkg.version} (${pkg.license ?? 'no licence field'})\n${RULE}`;
  return [head, ...pkg.texts.map((t) => `--- ${t.file} ---\n${t.text}`)].join('\n\n');
}

/** The notices text for `outfile` in `metafile`, sorted by package name; throws naming each bundled package with no licence text. */
export function noticesFor(metafile, outfile, read = readPackage) {
  const packages = bundledPackageDirs(metafile, outfile).map(read);
  const bare = packages.filter((p) => !p.texts.some((t) => t.text.length > 0));
  if (bare.length > 0) {
    throw new Error(`no licence file for bundled package(s): ${bare.map((p) => `${p.name} ${p.version} (license: ${p.license})`).join('; ')}`);
  }
  packages.sort(byNameThenVersion);
  const intro = `Third-party code bundled into ${basename(outfile)}, with the licence and notice files each package ships.`;
  return [intro, ...packages.map(section)].join('\n\n') + '\n';
}

/** Writes noticesFor(metafile, outfile) to `noticesPath`; `outfile` is the output path as the metafile names it. */
export function writeNotices(metafile, outfile, noticesPath) {
  writeFileSync(noticesPath, noticesFor(metafile, outfile));
}
