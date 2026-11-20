// The --music manifest of the single-file bundle: a JSON list of {role, url}, role one of start, gameplay, boss, gameover and url
// an absolute http(s) URL of the mp3 (same shape as wwwroot/music/tracks.json, with url in place of file). A boss track may also
// carry `boss`, the lower-case name of the one boss it belongs to; without it, it is a shared track for any boss.
const ROLES = ['start', 'gameplay', 'boss', 'gameover'];

export function readMusicManifest(json, where = 'manifest') {
  if (!Array.isArray(json)) throw new Error(`${where}: expected a list of {role, url}`);
  const out = json.map((e, i) => {
    if (!ROLES.includes(e?.role)) throw new Error(`${where}: entry ${i} needs a role of ${ROLES.join(', ')}`);
    if (typeof e.url !== 'string' || !/^https?:\/\//.test(e.url)) throw new Error(`${where}: entry ${i} (${e.role}) needs an absolute http(s) url`);
    if (e.boss !== undefined && (e.role !== 'boss' || typeof e.boss !== 'string' || !/^[a-z]+$/.test(e.boss))) {
      throw new Error(`${where}: entry ${i} (${e.role}) has a boss field that is not a lower-case boss name on a boss track`);
    }
    return e.boss === undefined ? { role: e.role, url: e.url } : { role: e.role, url: e.url, boss: e.boss };
  });
  for (const r of ROLES) if (!out.some((t) => t.role === r)) throw new Error(`${where}: no track with role "${r}"`);
  return out;
}
