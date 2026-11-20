import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { chromeLayout, leftPanel, FIELD_MAX_COLS, SIDE_MAX_COLS, SIDE_MIN_COLS, SIDE_GAP_COLS } from '../../src/FeedRaider.Web/wwwroot/page.js';

const tuning = readFileSync(new URL('../../src/FeedRaider.Engine/Raider/Tuning.cs', import.meta.url), 'utf8');
// The value of one `public const int` in Tuning.cs, found by its name wherever it stands on its line.
function tuned(name) {
  const m = new RegExp(`\\b${name}\\s*=\\s*(\\d+)`).exec(tuning);
  assert.ok(m, `Tuning.cs declares ${name}`);
  return Number(m[1]);
}

test('side panel: each page constant equals its twin in the engine\'s Tuning', () => {
  assert.equal(FIELD_MAX_COLS, tuned('FieldMaxCols'), 'FIELD_MAX_COLS');
  assert.equal(SIDE_MAX_COLS, tuned('SidePanelMaxCols'), 'SIDE_MAX_COLS');
  assert.equal(SIDE_MIN_COLS, tuned('SidePanelMinCols'), 'SIDE_MIN_COLS');
  assert.equal(SIDE_GAP_COLS, tuned('SideGapCols'), 'SIDE_GAP_COLS');
});

// The engine's rule (FieldLayout.Compute and FieldLayout.PanelCols), with the engine's own numbers read from Tuning.cs.
function engineLayout(cols) {
  const pCols = Math.min(cols, tuned('FieldMaxCols')) & ~1;
  const col0 = Math.trunc((cols - pCols) / 2);
  const w = Math.min(tuned('SidePanelMaxCols'), col0 - tuned('SideGapCols') - 1);
  return { pCols, col0, panelCols: w >= tuned('SidePanelMinCols') ? w : 0 };
}

const NARROWEST_COLS = 20, WIDEST_COLS = 260;   // from a phone held upright to a wide monitor with small cells
const CELL_W = 8, CELL_H = 17, ROWS = 40;        // whole-pixel cells, so every expected position is exact

test('side panel: the feed log and the how-to-play panel stand where the engine puts them, at every width', () => {
  for (let cols = NARROWEST_COLS; cols <= WIDEST_COLS; cols++) {
    const { pCols, col0, panelCols } = engineLayout(cols);
    const gap = tuned('SideGapCols');
    const l = chromeLayout({ cols, rows: ROWS, cellW: CELL_W, cellH: CELL_H, left: 0, top: 0 });
    assert.equal(l.fieldRight, (col0 + pCols) * CELL_W, `${cols} columns: the field's right edge`);
    if (panelCols === 0) {
      assert.equal(leftPanel(cols), null, `${cols} columns: no feed log`);
      assert.equal(l.side, null, `${cols} columns: no how-to-play panel`);
      continue;
    }
    assert.deepEqual(leftPanel(cols), { col: col0 - gap - panelCols, cols: panelCols }, `${cols} columns: the feed log`);
    assert.equal(l.side.cols, panelCols, `${cols} columns: the panel is as wide as the feed log`);
    assert.equal(l.side.x, (col0 + pCols + gap) * CELL_W, `${cols} columns: the panel's left edge`);
    assert.equal(l.side.w, panelCols * CELL_W, `${cols} columns: the panel's width`);
  }
});
