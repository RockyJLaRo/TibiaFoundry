// Unit tests of the global Tibia search's pure logic (src/js/search.js): matching, routes and recent searches.
// node --test website/tests/unit
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const website = path.resolve(here, "..", "..");
const require = createRequire(import.meta.url);
const create = require(path.join(website, "src", "js", "search.js"));
const registry = JSON.parse(readFileSync(path.join(website, "src", "registry.json"), "utf8"));
const fixtures = path.join(website, "tests", "fixtures", "tibiadata");
const load = (name) => JSON.parse(readFileSync(path.join(fixtures, name + ".json"), "utf8"));

/** A Storage double; throwing: true makes every call throw like a blocked localStorage. */
function memoryStorage(throwing = false) {
  const data = new Map();
  const guard = () => { if (throwing) throw new Error("SecurityError: storage blocked"); };
  return {
    data,
    getItem(k) { guard(); return data.has(k) ? data.get(k) : null; },
    setItem(k, v) { guard(); data.set(k, String(v)); },
    removeItem(k) { guard(); data.delete(k); }
  };
}

function search(storage = memoryStorage(), extra = {}) {
  return create(Object.assign({ search: registry.search, defaultWorld: "Antica", defaultTown: "Thais", storage: () => storage }, extra));
}

test("registry.search lists the six types and every route matches a data route", () => {
  assert.deepEqual(registry.search.map((e) => e.type), ["character", "guild", "world", "creature", "spell", "house"]);
  const patterns = registry.dataRoutes.map((r) => r.pattern.split("/"));
  for (const e of registry.search) {
    const segs = e.route.split("/");
    const ok = patterns.some((p) => p.length === segs.length && p.every((s, i) => (s.startsWith(":") ? /^\{\w+\}$/.test(segs[i]) : s === segs[i])));
    assert.ok(ok, `${e.type}: ${e.route} matches no data route`);
    if (e.browse) assert.ok(registry.dataRoutes.some((r) => r.pattern === e.browse), `${e.type}: browse ${e.browse}`);
  }
});

test("normalize ignores case, accents and extra spaces", () => {
  const S = search();
  assert.equal(S.normalize("  Zé   NINJA "), "ze ninja");
  assert.equal(S.normalize("Ąćęłńóśźż"), "acelnoszz".replace("l", "ł"));   // ł has no decomposition; the others lose their marks
  assert.equal(S.normalize("Ñandú"), "nandu");
});

test("filter: prefix matches first, then substring, at most 8, accent-insensitive", () => {
  const S = search();
  const worlds = S.itemsFrom("worlds", load("worlds"));
  const ant = S.filter(worlds, "ANT");
  assert.ok(ant.length >= 2);
  assert.ok(ant.every((w) => w.name.toLowerCase().includes("ant")), ant.map((w) => w.name).join());
  assert.equal(ant[0].name, "Antica");                       // the prefix match comes before Kalanta
  assert.ok(ant.findIndex((w) => w.name === "Kalanta") > 0);
  assert.ok(S.filter(worlds, "a").length === 8, "capped at 8");
  assert.deepEqual(S.filter(worlds, "   "), [], "an empty query has no suggestions");
  const accented = [{ name: "Zé Ninja" }, { name: "Zeta" }, { name: "Mazé" }];
  assert.deepEqual(S.filter(accented, "ze").map((x) => x.name), ["Zé Ninja", "Zeta", "Mazé"]);
  assert.deepEqual(S.filter(accented, "É").map((x) => x.name), ["Zé Ninja", "Zeta", "Mazé"]);
});

test("creatures match by name and route by race; spells match the formula too", () => {
  const S = search();
  const creatures = S.itemsFrom("creatures", load("creatures"));
  const dem = S.filter(creatures, "dem");
  const demons = dem.find((c) => c.name === "Demons");
  assert.ok(demons, dem.map((c) => c.name).join());
  assert.equal(S.routeFor("creature", demons), "#/creature/demon");
  assert.ok(dem.indexOf(demons) < dem.findIndex((c) => c.name === "Brachiodemons"), "prefix before substring");
  const spells = S.itemsFrom("spells", load("spells"));
  const exura = S.filter(spells, "exura");
  assert.ok(exura.length > 0 && exura.length <= 8);
  assert.equal(exura[0].words.slice(0, 5), "exura");
  assert.equal(S.routeFor("spell", spells.find((s) => s.name === "Light Healing")), "#/spell/lighthealing");
});

test("routeFor builds encoded hash routes for every type", () => {
  const S = search();
  assert.equal(S.routeFor("character", "  Roc   ky "), "#/character/Roc%20ky");
  assert.equal(S.routeFor("character", "Mc'Lovin"), "#/character/Mc'Lovin");
  assert.equal(S.routeFor("character", "Zé Ninja"), "#/character/Z%C3%A9%20Ninja");
  assert.equal(S.routeFor("guild", { q: "Crusaders of Might and Magic" }), "#/guild/Crusaders%20of%20Might%20and%20Magic");
  assert.equal(S.routeFor("world", { name: "Antica" }), "#/world/Antica");
  assert.equal(S.routeFor("creature", { q: "Demons", id: "demon" }), "#/creature/demon", "a recent entry keeps the race as id");
  assert.equal(S.routeFor("spell", { q: "Light Healing", id: "lighthealing" }), "#/spell/lighthealing");
  assert.equal(S.routeFor("house", { name: "Bona" }), "#/houses/Bona/Thais");
  assert.equal(S.routeFor("house", ""), "#/houses/Antica/Thais", "no world typed: the remembered or default world");
  assert.equal(S.routeFor("character", "   "), "", "nothing to open without a name");
  assert.equal(S.routeFor("creature", "unknown text"), "#/creatures", "no race: the list to browse");
  assert.equal(S.routeFor("spell", ""), "#/spells");
  assert.equal(S.routeFor("world", ""), "#/worlds");
  assert.equal(S.routeFor("nonsense", "x"), "");
  assert.equal(S.routeFor("character", "a/b?c#d"), "#/character/a%2Fb%3Fc%23d");
});

test("house uses the remembered world (tf-world) when none is typed", () => {
  const storage = memoryStorage();
  storage.setItem("tf-world", "Bona");
  assert.equal(search(storage).routeFor("house", ""), "#/houses/Bona/Thais");
});

test("resolve finds the exact (case/accent-insensitive) item for typed text", () => {
  const S = search();
  const worlds = S.itemsFrom("worlds", load("worlds"));
  assert.equal(S.resolve(worlds, "antica").name, "Antica");
  assert.equal(S.resolve(worlds, "antic"), null);
  const spells = S.itemsFrom("spells", load("spells"));
  assert.equal(S.resolve(spells, "EXURA").name, "Light Healing");
});

test("recent searches: newest first, deduplicated, at most 8, cleared", () => {
  const storage = memoryStorage();
  const S = search(storage);
  assert.deepEqual(S.recents.read(), []);
  for (let i = 1; i <= 9; i++) S.recents.add({ type: "character", q: "Name " + i });
  let list = S.recents.read();
  assert.equal(list.length, 8);
  assert.deepEqual(list.map((r) => r.q), ["Name 9", "Name 8", "Name 7", "Name 6", "Name 5", "Name 4", "Name 3", "Name 2"]);
  S.recents.add({ type: "character", q: "  name   5 " });
  list = S.recents.read();
  assert.equal(list.length, 8);
  assert.equal(list[0].q, "name 5");
  assert.equal(list.filter((r) => S.normalize(r.q) === "name 5").length, 1, "deduplicated");
  S.recents.add({ type: "guild", q: "Name 9" });
  assert.equal(S.recents.read()[0].type, "guild", "same text, other type: a separate entry");
  S.recents.add({ type: "creature", q: "Demons", id: "demon" });
  assert.deepEqual(S.recents.read()[0], { type: "creature", q: "Demons", id: "demon" });
  assert.deepEqual(JSON.parse(storage.data.get("tf-recent-searches"))[0], { type: "creature", q: "Demons", id: "demon" });
  S.recents.clear();
  assert.deepEqual(S.recents.read(), []);
  assert.ok(!storage.data.has("tf-recent-searches"));
});

test("recent searches ignore broken or foreign stored data", () => {
  const storage = memoryStorage();
  const S = search(storage);
  for (const raw of ["not json", "{}", "null", JSON.stringify([{ type: "evil", q: "x" }, { type: "character" }, { type: "world", q: 5 }, "str"])]) {
    storage.setItem("tf-recent-searches", raw);
    assert.deepEqual(S.recents.read(), [], raw);
  }
  storage.setItem("tf-recent-searches", JSON.stringify(new Array(20).fill(0).map((_, i) => ({ type: "world", q: "W" + i }))));
  assert.equal(S.recents.read().length, 8);
});

test("blocked storage: nothing throws, recents live in memory for the page", () => {
  const S = search(memoryStorage(true));
  assert.deepEqual(S.recents.read(), []);
  S.recents.add({ type: "character", q: "Roc ky" });
  assert.deepEqual(S.recents.read(), [{ type: "character", q: "Roc ky" }]);
  S.recents.clear();
  assert.deepEqual(S.recents.read(), []);
  assert.equal(S.routeFor("house", ""), "#/houses/Antica/Thais");
  const S2 = search(null, { storage: () => { throw new Error("localStorage getter throws"); } });
  S2.recents.add({ type: "world", q: "Antica" });
  assert.equal(S2.recents.read().length, 1);
});
