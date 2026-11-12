import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const main = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');

// main.js is a module with top-level await and cannot be imported, so startRun is cut out of the source and run against stubs.
function loadStartRun() {
  const from = main.indexOf('function startRun(choice) {');
  assert.notEqual(from, -1, 'main.js defines startRun');
  const source = main.slice(from, main.indexOf('\n}\n', from) + 2);
  const calls = [];
  const world = {
    startScreen: { hidden: false },
    behindStart: [{ inert: true }, { inert: true }],
    helperTextarea: { tabIndex: 0 },
    gear: { hidden: true },
    document: { body: { classList: { add: (c) => calls.push(`body.${c}`) } } },
    menu: { started: () => calls.push('menu.started') },
    renderMenuWho: () => {},
    placeChrome: () => calls.push('placeChrome'),
    requestAnimationFrame: () => {},
    params: new URLSearchParams(),
    showError: () => {},
    exports: { Host: { StartGame: (...args) => { calls.push('StartGame'); world.started = args; return Promise.resolve(); } } },
  };
  const run = new Function(...Object.keys(world), `${source}\nreturn startRun;`)(...Object.values(world));
  return { run, world, calls };
}

test('starting a run hides the start screen, frees the page behind it and then starts the game', () => {
  const { run, world, calls } = loadStartRun();
  run({ handle: 'alice.example', feed: '' });
  assert.equal(world.startScreen.hidden, true);
  assert.deepEqual(world.behindStart.map((n) => n.inert), [false, false]);
  assert.equal(world.gear.hidden, false);
  assert.equal(world.started[0], 'alice.example');
  assert.ok(calls.indexOf('body.playing') < calls.indexOf('StartGame'), 'the playing class goes on before the game starts');
});
