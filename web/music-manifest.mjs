// The --music manifest of the single-file bundle: a JSON list of {role, url}, role one of menu, gameplay, boss, gameover and url
// an absolute http(s) URL of the mp3 (same shape as wwwroot/music/tracks.json, with url in place of file).
export const ROLES = ['menu', 'gameplay', 'boss', 'gameover'];

export function readMusicManifest(json, where = 'manifest') {
  const list = Array.isArray(json) ? json : json?.tracks;
  if (!Array.isArray(list)) throw new Error(`${where}: expected a list of {role, url}`);
  const out = list.map((e, i) => {
    if (!ROLES.includes(e?.role)) throw new Error(`${where}: entry ${i} needs a role of ${ROLES.join(', ')}`);
    if (typeof e.url !== 'string' || !/^https?:\/\//.test(e.url)) throw new Error(`${where}: entry ${i} (${e.role}) needs an absolute http(s) url`);
    return { role: e.role, url: e.url };
  });
  for (const r of ROLES) if (!out.some((t) => t.role === r)) throw new Error(`${where}: no track with role "${r}"`);
  return out;
}
