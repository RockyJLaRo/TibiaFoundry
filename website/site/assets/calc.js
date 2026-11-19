/*
 * Calculator pages (every page whose registry entry has "calc"): binds a template's form to the formulas of formulas.js.
 *
 * Template contract (see src/templates/calc-experience.html):
 *   <form data-calc="<page id>">
 *   inputs   name="<field>" data-kind="int|number|duration" (or a <select name>), required when the field must be filled;
 *            data-min / data-max (inclusive), data-gt (strictly above) with data-range-error="<text key>";
 *            data-gt-field="<other field>" with data-gt-field-error="<text key>" (must be above that field);
 *            each followed by <p data-echo="<field>"> ("read as ...") and <p data-error="<field>"> (inline error).
 *   formulas <el data-formula="<id>" data-args="a,b" or "level=from" data-prefix="from">: calls FORMULAS[id] with
 *            those fields; its outputs go to <output data-out="[prefix.]key" data-format="number|duration">, which
 *            receive the text in the page language and data-value="<raw value>". All outputs are in one aria-live region.
 *   extras   <output data-finish="<out>">: today + that many days, as a date; [data-row] rows make the copied summary.
 *   buttons  [data-copy] (copies the summary; site.js does the copying), [data-share] (copies the share link),
 *            [data-reset] (back to the template's values, no query string).
 *
 * Numbers are read and written in the page language (TF_I18N.lang), never the browser's. A field that cannot be read
 * shows an inline error and no result is shown until every field is valid. The query string holds the inputs only, as
 * plain digits (?from=100&to=200&exph=1500000&hpd=4), and is kept up to date with history.replaceState as you type.
 */
(function () {
  "use strict";
  var F = window.TFFormulas, TF = window.TF, I18N = window.TF_I18N || {};
  if (!F || !TF) return;
  var lang = I18N.lang || "en", intl = I18N.intl || "en-US";
  var dateFormat = window.Intl ? new Intl.DateTimeFormat(intl, { day: "numeric", month: "long", year: "numeric" }) : null;
  var PLAIN = /^\d+(?:\.\d+)?$/;

  function num(value) { return F.formatNumber(value, lang); }
  function duration(minutes) { return TF.t("calc.unit.hoursMinutes", { h: num(Math.floor(minutes / 60)), m: num(minutes % 60) }); }
  function hm(minutes) { return Math.floor(minutes / 60) + ":" + ("0" + (minutes % 60)).slice(-2); }
  function each(list, fn) { Array.prototype.forEach.call(list, fn); }
  function find(form, attr, name) { return form.querySelector("[" + attr + '="' + name + '"]'); }

  document.querySelectorAll("form[data-calc]").forEach(setup);

  // The "How this is calculated" block is open on wide screens, collapsed on phones.
  if (window.matchMedia && matchMedia("(min-width: 768px)").matches) each(document.querySelectorAll("details.how"), function (d) { d.open = true; });

  function setup(form) {
    var fields = Array.prototype.slice.call(form.querySelectorAll("input[name][data-kind], select[name]"));
    var calls = Array.prototype.map.call(form.querySelectorAll("[data-formula]"), function (el) {
      return {
        fn: F.FORMULAS[el.getAttribute("data-formula")],
        prefix: el.getAttribute("data-prefix") ? el.getAttribute("data-prefix") + "." : "",
        args: (el.getAttribute("data-args") || "").split(",").filter(Boolean).map(function (part) {
          var kv = part.trim().split("=");
          return { key: kv[0], field: kv[1] || kv[0] };
        })
      };
    });
    var copyBtn = form.querySelector("[data-copy]"), shareBtn = form.querySelector("[data-share]"), resetBtn = form.querySelector("[data-reset]");
    var langLinks = document.querySelectorAll("a[data-lang]");
    each(langLinks, function (a) { a.setAttribute("data-base-href", a.getAttribute("href")); });
    var state = {};

    // A field's text in the page language from a plain query or template value (digits, h:mm, option value).
    function display(field, plain) {
      var kind = field.getAttribute("data-kind");
      if (field.tagName === "SELECT" || kind === "duration" || !PLAIN.test(plain)) return plain;
      return num(Number(plain));
    }
    function setDefaults() {
      fields.forEach(function (f) {
        if (f.tagName === "SELECT") {
          var def = Array.prototype.filter.call(f.options, function (o) { return o.defaultSelected; })[0] || f.options[0];
          if (def) f.value = def.value;
        } else f.value = display(f, f.defaultValue);
      });
    }

    function read(field) {
      var kind = field.getAttribute("data-kind"), text = field.value, r;
      if (field.tagName === "SELECT") return { value: field.value, plain: field.value };
      if (kind === "duration") r = String(text).trim() === "" ? { error: "empty" } : F.parseDuration(text);
      else r = F.parseNumber(text, lang, { integer: kind === "int" });
      if (r.error === "empty" && !field.required) return { absent: true };
      if (r.error) {
        var key = r.error === "empty" ? "calc.error.empty" : r.error === "integer" ? "calc.error.integer" : r.error === "range" ? "calc.error.range"
          : kind === "duration" ? "calc.error.duration" : kind === "int" ? "calc.error.formatInt" : "calc.error.format";
        return { error: TF.t(key) };
      }
      var v = r.value, min = field.getAttribute("data-min"), max = field.getAttribute("data-max"), gt = field.getAttribute("data-gt");
      r.plain = kind === "duration" ? hm(v) : String(v);
      r.echo = TF.t("calc.readAs", { value: kind === "duration" ? duration(v) : num(v) });
      if ((min !== null && v < Number(min)) || (max !== null && v > Number(max)) || (gt !== null && v <= Number(gt))) r.error = TF.t(field.getAttribute("data-range-error"));
      return r;
    }

    function setOut(el, value, text) {
      if (value === null) { el.textContent = ""; el.removeAttribute("data-value"); return; }
      el.textContent = text;
      el.setAttribute("data-value", String(value));
    }

    function compute() {
      state = {};
      fields.forEach(function (f) { state[f.name] = read(f); });
      fields.forEach(function (f) {
        var s = state[f.name], other = f.getAttribute("data-gt-field");
        if (!s.error && other && state[other] && state[other].value !== undefined && !state[other].error && s.value <= state[other].value) s.error = TF.t(f.getAttribute("data-gt-field-error"));
      });
      var valid = true;
      fields.forEach(function (f) {
        var s = state[f.name], echo = find(form, "data-echo", f.name), err = find(form, "data-error", f.name);
        if (echo) echo.textContent = s.echo || "";
        if (err) err.textContent = s.error || "";
        if (s.error) { valid = false; f.setAttribute("aria-invalid", "true"); } else f.removeAttribute("aria-invalid");
      });
      each(form.querySelectorAll("[data-out]"), function (o) { o._set = false; });
      if (valid) calls.forEach(function (call) {
        if (!call.fn || call.args.some(function (a) { return !state[a.field] || state[a.field].value === undefined; })) return;
        var input = {};
        call.args.forEach(function (a) { input[a.key] = state[a.field].value; });
        var result = call.fn(input);
        Object.keys(result).forEach(function (k) {
          var o = find(form, "data-out", call.prefix + k);
          if (!o) return;
          setOut(o, result[k], o.getAttribute("data-format") === "duration" ? duration(result[k]) : num(result[k]));
          o._set = true;
        });
      });
      each(form.querySelectorAll("[data-out]"), function (o) { if (!o._set) setOut(o, null); });
      each(form.querySelectorAll("[data-finish]"), function (o) {
        var days = find(form, "data-out", o.getAttribute("data-finish")), n = days && days.getAttribute("data-value");
        if (!n || !dateFormat) return setOut(o, null);
        var d = new Date();
        d.setDate(d.getDate() + Number(n));
        setOut(o, d.getFullYear() + "-" + ("0" + (d.getMonth() + 1)).slice(-2) + "-" + ("0" + d.getDate()).slice(-2), dateFormat.format(d));
      });
      if (copyBtn) {
        copyBtn.disabled = !valid;
        copyBtn.setAttribute("data-copy", valid ? summary() : "");
      }
    }

    function query() {
      var parts = [];
      fields.forEach(function (f) {
        var s = state[f.name];
        if (s && !s.error && s.plain !== undefined) parts.push(encodeURIComponent(f.name) + "=" + encodeURIComponent(s.plain));
      });
      return parts.length ? "?" + parts.join("&") : "";
    }
    function shareUrl() { return location.origin + location.pathname + query(); }
    function setSearch(search) {
      try { history.replaceState(history.state, "", location.pathname + search + location.hash); } catch (e) { /* file: or blocked */ }
      // The language links open this calculator with the same inputs.
      each(langLinks, function (a) { a.setAttribute("href", a.getAttribute("data-base-href") + search); });
    }

    function summary() {
      var h1 = document.querySelector("h1"), lines = [h1 ? h1.textContent.trim() : document.title];
      lines.push(fields.map(function (f) {
        var label = form.querySelector('label[for="' + f.id + '"]');
        return (label ? label.textContent.trim() : f.name) + ": " + (f.tagName === "SELECT" ? f.options[f.selectedIndex].text : f.value.trim());
      }).join(" · "));
      each(form.querySelectorAll("[data-row]"), function (row) {
        var label = row.querySelector(".calc-label"), value = row.cloneNode(true);
        if (label) value.removeChild(value.querySelector(".calc-label"));
        var text = value.textContent.replace(/\s+/g, " ").trim();
        if (text && /\d/.test(text)) lines.push((label ? label.textContent.trim() + ": " : "") + text);
      });
      lines.push(shareUrl());
      return lines.join("\n");
    }

    // Opening a share link: plain values from the query string (anything else is shown as typed, with its error).
    setDefaults();
    var params = new URLSearchParams(location.search);
    fields.forEach(function (f) {
      if (!params.has(f.name)) return;
      var v = params.get(f.name);
      if (f.tagName === "SELECT") { if (Array.prototype.some.call(f.options, function (o) { return o.value === v; })) f.value = v; }
      else f.value = display(f, v);
    });
    compute();
    if (location.search) setSearch(location.search);

    form.addEventListener("submit", function (e) { e.preventDefault(); });
    form.addEventListener("input", function () { compute(); setSearch(query()); });
    form.addEventListener("change", function (e) { if (e.target.tagName === "SELECT") { compute(); setSearch(query()); } });
    if (shareBtn) shareBtn.addEventListener("click", function () {
      TF.copy(shareUrl()).then(function () { TF.confirmCopy(shareBtn, true); }, function () { TF.confirmCopy(shareBtn, false); });
    });
    if (resetBtn) resetBtn.addEventListener("click", function () {
      setDefaults();
      compute();
      setSearch("");
      TF.announce(TF.t("calc.resetDone"));
    });
    form.setAttribute("data-ready", "1");
  }
})();
