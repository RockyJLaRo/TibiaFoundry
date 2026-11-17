// Unit tests of the TibiaData client (src/js/tibiadata.js): node --test website/tests/unit
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const website = path.resolve(here, "..", "..");
const require = createRequire(import.meta.url);
const create = require(path.join(website, "src", "js", "tibiadata.js"));
const registry = JSON.parse(readFileSync(path.join(website, "src", "registry.json"), "utf8"));
const fixtures = path.join(website, "tests", "fixtures", "tibiadata");
const manifest = JSON.parse(readFileSync(path.join(fixtures, "manifest.json"), "utf8"));
const BASE = registry.api.base;

function fixture(apiPath) {
  const entry = manifest.files[apiPath];
  if (!entry) throw new Error("no fixture for " + apiPath);
  return { status: entry.status, body: readFileSync(path.join(fixtures, entry.file), "utf8") };
}

function response(status, body, headers = {}) {
  return { status, text: async () => body, headers: { get: (k) => headers[k] ?? null } };
}

/** A fake fetch that serves fixtures (or a scripted list of answers) and records every call. */
function fakeFetch(script) {
  const calls = [];
  const fn = async (url, init) => {
    calls.push(url);
    if (init && init.signal && init.signal.aborted) throw Object.assign(new Error("aborted"), { name: "AbortError" });
    if (script && script.length) {
      const next = script.shift();
      if (next instanceof Error) throw next;
      if (typeof next === "function") return next(url, init);
      return next;
    }
    const f = fixture(url.slice(BASE.length));
    return response(f.status, f.body);
  };
  fn.calls = calls;
  return fn;
}

function memoryStorage() {
  const m = new Map();
  return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), removeItem: (k) => m.delete(k),
           key: (i) => [...m.keys()][i] ?? null, get length() { return m.size; }, _m: m };
}

function client(opts = {}) {
  let clock = opts.start ?? 1_000_000;
  const api = create({
    config: registry.api, fetch: opts.fetch ?? fakeFetch(), storage: opts.storage ?? memoryStorage(),
    now: () => clock, sleep: (ms, signal) => new Promise((res, rej) => {
      if (signal && signal.aborted) return rej(Object.assign(new Error("x"), { kind: "aborted" }));
      res();
    }),
  });
  api.advance = (s) => { clock += s * 1000; };
  return api;
}

test("every implemented endpoint parses its recorded fixture and passes its schema check", async () => {
  const f = registry.fixtures;
  const calls = [
    ["worlds", []], ["world", [f.world]], ["character", [f.character]], ["guilds", [f.world]], ["guild", [f.guild]],
    ["highscores", [f.world, "experience", "all", 1]], ["highscores", ["all", "experience", "all", 1]],
    ["houses", [f.house.world, f.house.town]], ["house", [f.house.world, f.house.id]], ["killstatistics", [f.world]],
    ["creatures", []], ["creature", [f.creature]], ["spells", []], ["spell", [f.spell]], ["boostablebosses", []],
    ["fansites", []], ["news", []], ["newsticker", []], ["newsarchive", [90]], ["newsarticle", [f.news]],
  ];
  const api = client();
  for (const [name, args] of calls) {
    const r = await api.get(name, args);
    assert.equal(r.fromCache, false, name);
    assert.ok(r.data && typeof r.data === "object", name);
  }
  assert.equal(api.stats.network, calls.length);
});

test("endpoint paths encode names (spaces, quotes) and clamp numbers", () => {
  const api = client();
  assert.equal(api.endpoints.character("Roc ky")[0], "character/Roc%20ky");
  assert.equal(api.endpoints.guild("Crusaders of Might and Magic")[0], "guild/Crusaders%20of%20Might%20and%20Magic");
  assert.equal(api.endpoints.houses("Antica", "Ab'Dendriel")[0], "houses/Antica/Ab'Dendriel");
  assert.equal(api.endpoints.character("../worlds")[0], "character/..%2Fworlds");
  assert.equal(api.endpoints.highscores("Antica", "experience", "all", -3)[0], "highscores/Antica/experience/all/1");
  assert.equal(api.endpoints.newsarchive(99999)[0], "news/archive/3650");
  assert.equal(api.endpoints.newsarticle("9050<script>")[0], "news/id/9050");
});

test("a cached response is reused until its cache time has passed", async () => {
  const fetch = fakeFetch();
  const api = client({ fetch });
  await api.get("worlds", []);
  const second = await api.get("worlds", []);
  assert.equal(second.fromCache, true);
  assert.equal(fetch.calls.length, 1);
  api.advance(registry.api.cacheSeconds.worlds + 1);
  const third = await api.get("worlds", []);
  assert.equal(third.fromCache, false);
  assert.equal(fetch.calls.length, 2);
});

test("our cache times are never shorter than TibiaData's own (https://tibiadata.com/faq/)", () => {
  const tibiadata = { worlds: 60, world: 60, guild: 120, guilds: 120, character: 300, house: 300, houses: 300, killstatistics: 300,
    news: 300, newsticker: 300, newsarchive: 300, newsarticle: 300, boostablebosses: 900, creature: 900, creatures: 900,
    fansites: 900, highscores: 900, spell: 900, spells: 900 };
  for (const [kind, seconds] of Object.entries(tibiadata)) assert.ok(registry.api.cacheSeconds[kind] >= seconds, kind);
});

test("the cache survives a reload through sessionStorage", async () => {
  const storage = memoryStorage();
  await client({ storage }).get("spells", []);
  const fetch = fakeFetch();
  const r = await client({ storage, fetch }).get("spells", []);
  assert.equal(r.fromCache, true);
  assert.equal(fetch.calls.length, 0);
});

test("simultaneous requests for the same URL share one network request", async () => {
  let release;
  const gate = new Promise((r) => (release = r));
  const fetch = fakeFetch([async (url) => { await gate; const f = fixture("creatures"); return response(200, f.body); }]);
  const api = client({ fetch });
  const a = api.get("creatures", []), b = api.get("creatures", []), c = api.get("creatures", []);
  release();
  const results = await Promise.all([a, b, c]);
  assert.equal(fetch.calls.length, 1);
  assert.equal(api.stats.shared, 2);
  results.forEach((r) => assert.ok(r.data.creatures));
});

test("a cancelled request rejects as aborted and cancels the network request when nobody else waits", async () => {
  let seenSignal;
  const fetch = fakeFetch([(url, init) => new Promise((res, rej) => {
    seenSignal = init.signal;
    init.signal.addEventListener("abort", () => rej(Object.assign(new Error("aborted"), { name: "AbortError" })));
  })]);
  const api = client({ fetch });
  const controller = new AbortController();
  const p = api.get("character", ["Roc ky"], { signal: controller.signal });
  controller.abort();
  await assert.rejects(p, (e) => e.kind === "aborted");
  assert.equal(seenSignal.aborted, true);
});

test("cancelling one of two callers keeps the shared request alive for the other", async () => {
  let release;
  const gate = new Promise((r) => (release = r));
  const fetch = fakeFetch([async (url, init) => { await gate; if (init.signal.aborted) throw new Error("aborted"); return response(200, fixture("spells").body); }]);
  const api = client({ fetch });
  const c1 = new AbortController();
  const p1 = api.get("spells", [], { signal: c1.signal });
  const p2 = api.get("spells", []);
  c1.abort();
  release();
  await assert.rejects(p1, (e) => e.kind === "aborted");
  assert.ok((await p2).data.spells);
});

test("an unknown character is 'notfound' and is not retried", async () => {
  const fetch = fakeFetch();
  const api = client({ fetch });
  await assert.rejects(api.get("character", [registry.fixtures.missingCharacter]), (e) => e.kind === "notfound" && e.status === 404);
  assert.equal(fetch.calls.length, 1);
});

test("an invalid parameter (400) is 'invalid' and is not retried", async () => {
  const fetch = fakeFetch([response(400, JSON.stringify({ information: { status: { http_code: 400, message: "invalid" } } }))]);
  const api = client({ fetch });
  await assert.rejects(api.get("world", ["x"]), (e) => e.kind === "invalid");
  assert.equal(fetch.calls.length, 1);
});

test("a network failure is retried once, then reported as 'network'", async () => {
  const fetch = fakeFetch([new TypeError("Failed to fetch"), new TypeError("Failed to fetch")]);
  const api = client({ fetch });
  await assert.rejects(api.get("worlds", []), (e) => e.kind === "network");
  assert.equal(fetch.calls.length, 2);
});

test("a server error is retried once and succeeds on the retry", async () => {
  const fetch = fakeFetch([response(503, "")]);
  const api = client({ fetch });
  const r = await api.get("worlds", []);
  assert.ok(r.data.worlds);
  assert.equal(fetch.calls.length, 2);
});

test("rate limiting (429) is reported as 'ratelimit' after one retry", async () => {
  const fetch = fakeFetch([response(429, "", { "Retry-After": "1" }), response(429, "", { "Retry-After": "1" })]);
  const api = client({ fetch });
  await assert.rejects(api.get("highscores", ["Antica", "experience", "all", 1]), (e) => e.kind === "ratelimit");
  assert.equal(fetch.calls.length, 2);
});

test("a response that is not JSON, or lacks the expected fields, is 'malformed' and never cached", async () => {
  const fetch = fakeFetch([response(200, "<html>oops</html>"), response(200, JSON.stringify({ worlds: { nothing: true } }))]);
  const api = client({ fetch });
  await assert.rejects(api.get("worlds", []), (e) => e.kind === "malformed");
  await assert.rejects(api.get("worlds", []), (e) => e.kind === "malformed");
  assert.equal(fetch.calls.length, 2);
});

test("an error status inside a 200 answer is honoured", async () => {
  const fetch = fakeFetch([response(200, JSON.stringify({ information: { status: { http_code: 404, message: "could not find character" } } }))]);
  await assert.rejects(client({ fetch }).get("character", ["x"]), (e) => e.kind === "notfound");
});

test("when TibiaData is down, the last good answer is returned and marked stale", async () => {
  const storage = memoryStorage();
  const api = client({ storage });
  await api.get("news", []);
  api.advance(registry.api.cacheSeconds.news + 5);
  const down = client({ storage, fetch: fakeFetch([response(502, ""), response(502, "")]), start: 1_000_000 + (registry.api.cacheSeconds.news + 5) * 1000 });
  const r = await down.get("news", []);
  assert.equal(r.stale, true);
  assert.equal(r.fromCache, true);
  assert.equal(r.error.kind, "server");
  assert.ok(Array.isArray(r.data.news));
});

test("force bypasses the cache (manual refresh)", async () => {
  const fetch = fakeFetch();
  const api = client({ fetch });
  await api.get("news", []);
  await api.get("news", [], { force: true });
  assert.equal(fetch.calls.length, 2);
});

test("requests go only to the configured TibiaData v5 base", async () => {
  const fetch = fakeFetch();
  const api = client({ fetch });
  await api.get("spell", ["lighthealing"]);
  assert.ok(fetch.calls.every((u) => u.startsWith("https://api.tibiadata.com/v5/")));
  assert.equal(registry.api.version, "v5");
});
