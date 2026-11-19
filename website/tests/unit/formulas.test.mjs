// Unit tests of the formula registry and number parsing (src/js/formulas.js): node --test website/tests/unit/formulas.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const website = path.resolve(here, "..", "..");
const require = createRequire(import.meta.url);
const { parseNumber, parseDuration, formatNumber, FORMULAS } = require(path.join(website, "src", "js", "formulas.js"));
const registry = JSON.parse(readFileSync(path.join(website, "src", "formulas.json"), "utf8"));

test("parseNumber: grouping per language", () => {
  assert.deepEqual(parseNumber("1,500,000", "en"), { value: 1500000 });
  assert.deepEqual(parseNumber("1.500.000", "de"), { value: 1500000 });
  assert.deepEqual(parseNumber("1.500.000", "pt-BR"), { value: 1500000 });
  assert.deepEqual(parseNumber("1 500 000", "pl"), { value: 1500000 });
  assert.deepEqual(parseNumber("1\u00a0500\u00a0000", "pl"), { value: 1500000 });
  assert.deepEqual(parseNumber("1\u202f500\u202f000", "es"), { value: 1500000 });
});

test("parseNumber: shorthand k, kk, kkk", () => {
  assert.deepEqual(parseNumber("1.5kk", "en"), { value: 1500000 });
  assert.deepEqual(parseNumber("1,5kk", "de"), { value: 1500000 });
  assert.deepEqual(parseNumber("250k", "es"), { value: 250000 });
  assert.deepEqual(parseNumber("2kkk", "en"), { value: 2000000000 });
});

test("parseNumber: errors, never a guess", () => {
  assert.deepEqual(parseNumber("1.500.000", "en"), { error: "format" });
  assert.deepEqual(parseNumber("1,5", "en"), { error: "format" });
  assert.deepEqual(parseNumber("", "en"), { error: "empty" });
  assert.deepEqual(parseNumber("   ", "en"), { error: "empty" });
  assert.deepEqual(parseNumber("12.5", "en", { integer: true }), { error: "integer" });
  assert.deepEqual(parseNumber("abc", "en"), { error: "format" });
  assert.deepEqual(parseNumber("1,50", "en"), { error: "format" });
  assert.deepEqual(parseNumber("1,5000", "en"), { error: "format" });
  assert.deepEqual(parseNumber("1kkkk", "en"), { error: "format" });
  assert.deepEqual(parseNumber("1x500", "de"), { error: "format" });
  assert.deepEqual(parseNumber("1 500", "en"), { error: "format" });
  assert.deepEqual(parseNumber("1e999", "en"), { error: "format" });
  assert.deepEqual(parseNumber("-5", "en"), { error: "format" });
  assert.deepEqual(parseNumber("99999999999999999999", "en"), { error: "range" });
});

test("parseNumber: plain and decimal values", () => {
  assert.deepEqual(parseNumber("1500", "en"), { value: 1500 });
  assert.deepEqual(parseNumber("12,5", "pt-BR"), { value: 12.5 });
  assert.deepEqual(parseNumber("12.5", "en"), { value: 12.5 });
  assert.deepEqual(parseNumber("1.5k", "en", { integer: true }), { value: 1500 });
});

test("parseDuration", () => {
  assert.deepEqual(parseDuration("39:00"), { value: 2340 });
  assert.deepEqual(parseDuration("0:05"), { value: 5 });
  assert.deepEqual(parseDuration("02:30"), { value: 150 });
  assert.deepEqual(parseDuration("39"), { error: "format" });
  assert.deepEqual(parseDuration("1:75"), { error: "format" });
});

test("formatNumber: the site's grouping per language, readable again by parseNumber", () => {
  assert.equal(formatNumber(113695000, "en"), "113,695,000");
  assert.equal(formatNumber(113695000, "de"), "113.695.000");
  assert.equal(formatNumber(113695000, "pt-BR"), "113.695.000");
  assert.equal(formatNumber(113695000, "pl"), "113\u00a0695\u00a0000");
  assert.equal(formatNumber(113695000, "es"), "113\u00a0695\u00a0000");
  assert.equal(formatNumber(4547, "pl"), "4547");
  assert.equal(formatNumber(4547, "en"), "4,547");
  assert.equal(formatNumber(0, "de"), "0");
  assert.equal(formatNumber(132933899800, "en"), "132,933,899,800");
  assert.equal(formatNumber(1234.5, "en"), "1,234.5");
  assert.equal(formatNumber(1234.5, "de"), "1.234,5");
  // never exponent notation (String(1e-5) is "1e-5", String(1e21) is "1e+21")
  assert.equal(formatNumber(0.00001, "en"), "0.00001");
  assert.equal(formatNumber(0.00001, "de"), "0,00001");
  assert.equal(formatNumber(1e21, "en"), "1,000,000,000,000,000,000,000");
  for (const lang of ["en", "pt-BR", "pl", "es", "de"]) {
    for (const n of [0, 7, 999, 1000, 9999, 10000, 1500000, 132933899800, 12.5, 1234.75]) {
      assert.deepEqual(parseNumber(formatNumber(n, lang), lang), { value: n }, lang + " " + n);
    }
  }
});

test("registry: every formula is complete and its tests pass", () => {
  for (const f of registry.formulas) {
    assert.equal(typeof FORMULAS[f.id], "function", f.id + " has no implementation");
    assert.ok(f.sources.length >= 1, f.id + " needs a source");
    assert.match(f.checkedOn, /^\d{4}-\d{2}-\d{2}$/, f.id + " checkedOn");
    assert.ok(f.tests.length >= 3, f.id + " needs at least 3 tests");
    assert.ok(f.tests.some((t) => t.from !== "computed"), f.id + " needs a non-computed test");
    for (const t of f.tests) assert.deepEqual(FORMULAS[f.id](t.in), t.out, f.id);
  }
});

test("registry: ids are unique", () => {
  const ids = registry.formulas.map((f) => f.id);
  assert.deepEqual(ids.filter((id, i) => ids.indexOf(id) !== i), []);
});

test("staminaRest: 3 offline minutes per stamina minute below 39:00, 6 from 39:00 to 42:00, plus the 10-minute logout delay", () => {
  assert.deepEqual(FORMULAS.staminaRest({ current: 2340, target: 2520 }), { regen: 1080, minutes: 1090 }); // 39:00 -> 42:00
  assert.deepEqual(FORMULAS.staminaRest({ current: 0, target: 2520 }), { regen: 8100, minutes: 8110 });
  assert.deepEqual(FORMULAS.staminaRest({ current: 2280, target: 2400 }), { regen: 540, minutes: 550 }); // crosses 39:00
  assert.deepEqual(FORMULAS.staminaRest({ current: 2519, target: 2520 }), { regen: 6, minutes: 16 });
  assert.deepEqual(FORMULAS.staminaRest({ current: 2339, target: 2340 }), { regen: 3, minutes: 13 });
});

test("staminaRest: a target at or below the current stamina needs no rest (no delay either)", () => {
  assert.deepEqual(FORMULAS.staminaRest({ current: 1000, target: 1000 }), { regen: 0, minutes: 0 });
  assert.deepEqual(FORMULAS.staminaRest({ current: 2520, target: 100 }), { regen: 0, minutes: 0 });
});

test("blessings: price brackets at their boundaries (levels 30/31, 119/120/121, 269/270/271)", () => {
  const price = (level) => FORMULAS.blessings({ level });
  assert.deepEqual(price(30), { regularEach: 2000, regularAll: 10000, enhancedEach: 2600, enhancedAll: 5200, inquisition: 11000, twistOfFate: 2000 });
  assert.deepEqual(price(31), { regularEach: 2200, regularAll: 11000, enhancedEach: 2860, enhancedAll: 5720, inquisition: 12100, twistOfFate: 2200 });
  assert.deepEqual(price(119), { regularEach: 19800, regularAll: 99000, enhancedEach: 25740, enhancedAll: 51480, inquisition: 108900, twistOfFate: 19800 });
  assert.deepEqual(price(120), { regularEach: 20000, regularAll: 100000, enhancedEach: 26000, enhancedAll: 52000, inquisition: 110000, twistOfFate: 20000 });
  assert.deepEqual(price(121), { regularEach: 20075, regularAll: 100375, enhancedEach: 26100, enhancedAll: 52200, inquisition: 110412, twistOfFate: 20200 });
  assert.equal(price(269).twistOfFate, 49800);
  assert.equal(price(270).twistOfFate, 50000);
  assert.equal(price(271).twistOfFate, 50000);
});

test("blessings: below level 8 the prices of the level <= 30 bracket apply; the Inquisition price rounds down; large levels stay integers", () => {
  assert.deepEqual(FORMULAS.blessings({ level: 1 }), FORMULAS.blessings({ level: 30 }));
  assert.equal(FORMULAS.blessings({ level: 271 }).inquisition, 172287); // 156,625 x 1.1 = 172,287.5
  for (const level of [1, 77, 20000]) for (const v of Object.values(FORMULAS.blessings({ level }))) assert.ok(Number.isSafeInteger(v));
});
