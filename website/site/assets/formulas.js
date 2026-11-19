/*
 * Calculator formulas and number input (the formula registry is src/formulas.json; build_site.py --strict checks it).
 *
 * - parseNumber: reads a number the way the page language writes it (grouping and decimal separator per language,
 *   shorthand k = 10^3, kk = 10^6, kkk = 10^9); anything it cannot read is an error code, never a guess.
 * - parseDuration: h:mm / hh:mm into minutes.
 * - formatNumber: writes a number the way parseNumber reads it in that language (the site's grouping, not the browser's
 *   Intl rules: Intl groups Spanish with "." and leaves out grouping below 10 000 in pl/es); pl/es use no-break spaces.
 * - FORMULAS: id -> pure function (input object -> result object), one per entry of formulas.json.
 *
 * Works in the browser (window.TFFormulas) and in Node for the unit tests (module.exports).
 */
(function (root, factory) {
  if (typeof module === "object" && module.exports) module.exports = factory();
  else root.TFFormulas = factory();
})(typeof self !== "undefined" ? self : this, function () {
  "use strict";

  // Group separator per language (same values as GROUP in build_site.py). Intl formats pl and es with no-break
  // spaces, so a space group accepts a regular space, U+00A0 and U+202F.
  var GROUP = { "en": ",", "pt-BR": ".", "de": ".", "pl": " ", "es": " " };
  var GROUP_CLASS = { ",": ",", ".": "\\.", " ": "[ \u00a0\u202f]" };
  var MULTIPLIER = { k: 1e3, kk: 1e6, kkk: 1e9 };
  var patterns = {};

  function pattern(lang) {
    if (!patterns[lang]) {
      var g = GROUP_CLASS[GROUP[lang]];
      var d = lang === "en" ? "\\." : ",";
      patterns[lang] = new RegExp("^(\\d{1,3}(?:" + g + "\\d{3})+|\\d+)(?:" + d + "(\\d+))?(k{1,3})?$");
    }
    return patterns[lang];
  }

  function parseNumber(text, lang, opts) {
    if (!Object.prototype.hasOwnProperty.call(GROUP, lang)) return { error: "format" };
    var s = String(text == null ? "" : text).trim().toLowerCase();
    if (s === "") return { error: "empty" };
    var m = pattern(lang).exec(s);
    if (!m) return { error: "format" };
    var value = Number(m[1].replace(/\D/g, "") + (m[2] ? "." + m[2] : ""));
    if (m[3]) value = Math.round(value * MULTIPLIER[m[3]] * 1e6) / 1e6;
    if (!isFinite(value) || value > Number.MAX_SAFE_INTEGER) return { error: "range" };
    if (opts && opts.integer && value !== Math.floor(value)) return { error: "integer" };
    return { value: value };
  }

  function parseDuration(text) {
    var m = /^(\d{1,3}):([0-5]\d)$/.exec(String(text == null ? "" : text).trim());
    if (!m) return { error: "format" };
    return { value: Number(m[1]) * 60 + Number(m[2]) };
  }

  function formatNumber(value, lang) {
    var group = GROUP[lang] === " " ? "\u00a0" : GROUP[lang] || ",";
    // Plain digits, never exponent notation (String(1e-5) is "1e-5"); grouping and separators are added below.
    var plain = Math.abs(value).toLocaleString("en-US", { useGrouping: false, maximumFractionDigits: 20 });
    var parts = plain.split(".");
    var whole = parts[0];
    if (!(group === "\u00a0" && whole.length <= 4)) whole = whole.replace(/\B(?=(\d{3})+(?!\d))/g, group);
    return (value < 0 ? "-" : "") + whole + (parts[1] ? (lang === "en" ? "." : ",") + parts[1] : "");
  }

  var FORMULAS = {
    // Total experience for a level: 50/3 * (L^3 - 6L^2 + 17L - 12). The product is always divisible by 3 (exact integer).
    levelExp: function (i) {
      var L = i.level;
      return { exp: (50 * (L * L * L - 6 * L * L + 17 * L - 12)) / 3 };
    },
    // Experience between two levels, minutes at exph experience per hour, days at hpd hours per day.
    levelPlan: function (i) {
      var exp = FORMULAS.levelExp({ level: i.to }).exp - FORMULAS.levelExp({ level: i.from }).exp;
      var minutes = Math.floor((exp * 60) / i.exph);
      return { exp: exp, minutes: minutes, days: Math.ceil(minutes / (i.hpd * 60)) };
    },
    // Party share range (TibiaWiki "Party Experience Sharing" table): min = floor(2L/3), max = floor(3L/2) + 1.
    shareRange: function (i) {
      return { min: Math.floor((2 * i.level) / 3), max: Math.floor((3 * i.level) / 2) + 1 };
    },
    // Offline time to regain stamina (all values in minutes). Logged out: 1 stamina minute per 3 offline minutes below 39:00 (2340),
    // per 6 from 39:00 to 42:00 (2520, TibiaWiki "Stamina"). regen = regeneration time; minutes = regen + the 10 minutes after
    // logout in which nothing is regained. A target at or below the current stamina needs neither.
    staminaRest: function (i) {
      var BONUS = 2340, FULL = 2520;
      if (i.target <= i.current) return { regen: 0, minutes: 0 };
      var normal = Math.max(0, Math.min(i.target, BONUS) - Math.min(i.current, BONUS));
      var bonus = Math.max(0, Math.min(i.target, FULL) - Math.max(i.current, BONUS));
      var regen = normal * 3 + bonus * 6;
      return { regen: regen, minutes: regen + 10 };
    }
  };

  return { parseNumber: parseNumber, parseDuration: parseDuration, formatNumber: formatNumber, FORMULAS: FORMULAS };
});
