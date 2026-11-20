import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const main = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');

// main.js is a module with top-level await and cannot be imported, so startRun is cut out of the source and run against stubs.
function loadStartRun(query = '') {
  const from = main.indexOf('function startRun(choice) {');
  assert.notEqual(from, -1, 'main.js defines startRun');
  const source = main.slice(from, main.indexOf('\n}\n', from) + 2);
  const calls = [];
  const world = {
    startPage: { hidden: false },
    behindStart: [{ inert: true }, { inert: true }],
    helperTextarea: { tabIndex: 0 },
    gear: { hidden: true },
    document: { body: { classList: { add: (c) => calls.push(`body.${c}`) } } },
    tour: { hide: () => calls.push('tour.hide') },
    feedSpeed: { begin: () => calls.push('feedSpeed.begin') },
    menu: { started: () => calls.push('menu.started') },
    renderMenuWho: () => {},
    giveGrants: () => calls.push('giveGrants'),
    placeChrome: () => calls.push('placeChrome'),
    requestAnimationFrame: () => {},
    params: new URLSearchParams(query),
    darknessParam: 'on',
    showError: () => {},
    runsStarted: 0,
    pictureReader: { drop: () => calls.push('pictureReader.drop') },
    ends: [],
    exports: { Host: { StartGame: (...args) => { calls.push('StartGame'); world.started = args; return new Promise((end) => world.ends.push(end)); } } },
  };
  const run = new Function(...Object.keys(world), `${source}\nreturn startRun;`)(...Object.values(world));
  return { run, world, calls };
}

test('the run is told the stage, the boss and whether boss gates are on, as the address says', () => {
  const read = (query) => {
    const { run, world } = loadStartRun(query);
    run({ handle: 'alice.example', feed: '' });
    return world.started.slice(-3);
  };
  assert.deepEqual(read(''), [1, 0, true]);
  assert.deepEqual(read('stage=6&boss=4&gates=off'), [6, 4, false]);
  assert.deepEqual(read('gates=OFF'), [1, 0, false]);
  assert.deepEqual(read('gates=on'), [1, 0, true]);
  assert.deepEqual(read('gates=nonsense'), [1, 0, true]);
});

test('starting a run hides the start page, frees the page behind it and then starts the game', () => {
  const { run, world, calls } = loadStartRun();
  run({ handle: 'alice.example', feed: '' });
  assert.equal(world.startPage.hidden, true);
  assert.deepEqual(world.behindStart.map((n) => n.inert), [false, false]);
  assert.equal(world.gear.hidden, false);
  assert.equal(world.started[0], 'alice.example');
  assert.ok(calls.indexOf('body.playing') < calls.indexOf('StartGame'), 'the playing class goes on before the game starts');
  assert.ok(calls.includes('giveGrants') && calls.indexOf('giveGrants') < calls.indexOf('StartGame'), 'the game knows what the sign-in may write before the run starts');
});

test('Back to start page drops the pictures still waiting for a turn before the tour comes back', () => {
  assert.match(main, /function showStartPage\(\) \{[^]*?\n  pictureReader\.drop\(\);\n[^]*?\n  tour\.show\(\);\n/);
});

// lets the StartGame promise's handlers run
const settle = () => new Promise((resolve) => setImmediate(resolve));

test('a run that ends drops the pictures still waiting for a turn', async () => {
  const { run, world, calls } = loadStartRun();
  run({ handle: 'alice.example', feed: '' });
  await settle();
  assert.ok(!calls.includes('pictureReader.drop'), 'nothing is dropped while the run is on');
  world.ends[0]();
  await settle();
  assert.equal(calls.filter((c) => c === 'pictureReader.drop').length, 1);
});

test('a run that ends after the next one started leaves the pictures of the next run alone', async () => {
  const { run, world, calls } = loadStartRun();
  run({ handle: 'alice.example', feed: '' });
  run({ handle: 'bob.example', feed: '' });
  world.ends[0]();
  await settle();
  assert.ok(!calls.includes('pictureReader.drop'));
  world.ends[1]();
  await settle();
  assert.equal(calls.filter((c) => c === 'pictureReader.drop').length, 1);
});
