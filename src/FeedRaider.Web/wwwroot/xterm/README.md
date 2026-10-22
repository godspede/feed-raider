# xterm.js, served with the page

These are the exact files the page used to load from cdn.jsdelivr.net, kept here so the terminal never depends on a CDN.
They are MIT licensed; see `LICENSE` beside them.

| file | from | sha384 |
|---|---|---|
| `xterm.min.js` | `@xterm/xterm@5.5.0/lib/xterm.min.js` (the npm `lib/xterm.js` behind a jsDelivr comment header) | `J4qzUjBl1FxyLsl/kQPQIOeINsmp17OHYXDOMpMxlKX53ZfYsL+aWHpgArvOuof9` |
| `xterm.min.css` | `@xterm/xterm@5.5.0/css/xterm.min.css` (the npm `css/xterm.css`, minified by jsDelivr) | `tStR1zLfWgsiXCF3IgfB3lBa8KmBe/lG287CL9WCeKgQYcp1bjb4/+mwN6oti4Co` |
| `addon-fit.min.js` | `@xterm/addon-fit@0.10.0/lib/addon-fit.min.js` (the npm `lib/addon-fit.js` behind a jsDelivr comment header) | `XGqKrV8Jrukp1NITJbOEHwg01tNkuXr6uB6YEj69ebpYU3v7FvoGgEg23C1Gcehk` |
| `addon-webgl.js` | `@xterm/addon-webgl@0.18.0/lib/addon-webgl.js` (the npm file as published) | `AkzdQDeATxpW8V7I2EPcwg17qAoLQMjEFHb//1QaAAIyIlbDAHGKno5+huH7RKxq` |

`web/test/page.test.mjs` checks these hashes, so a changed byte fails the tests. `.gitattributes` keeps git from rewriting
line endings here. To move to a newer xterm, replace all four files from one release and update the table and the test.
