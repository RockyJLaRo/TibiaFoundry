// Tibia Foundry website: language selector, theme toggle, menu, screenshot tabs, lightbox, reveal-on-scroll and
// the analytics click events. No external scripts besides the Google tag; nothing here sends personal data.
// Texts come from window.TF_I18N, written into each page by build_site.py from src/locales/<lang>.json.
(function () {
  'use strict';
  var root = document.documentElement;
  root.classList.remove('no-js');

  // ---------------------------------------------------------------- texts: this language, else English (never a bare key)
  var I18N = window.TF_I18N || { lang: 'en', strings: {}, fallback: {}, languages: [] };
  var DEV = location.protocol === 'file:' || /^(localhost|127\.0\.0\.1|\[::1\])$/.test(location.hostname);
  var plurals = window.Intl && Intl.PluralRules ? new Intl.PluralRules(I18N.lang) : null;
  function t(key, vars) {
    var text = I18N.strings[key];
    if (text == null) {
      text = I18N.fallback[key];
      if (DEV) console.warn('[i18n] "' + key + '" is missing in ' + I18N.lang + (text == null ? ' and in English' : '; English is shown'));
      if (text == null) text = '';
    }
    // A plural text is an object of CLDR categories; vars.count picks the form.
    if (typeof text === 'object') {
      var n = vars && vars.count != null ? Number(vars.count) : 0;
      text = text[plurals ? plurals.select(n) : (n === 1 ? 'one' : 'other')] || text.other || text.many || '';
    }
    return text.replace(/\{(\w+)\}/g, function (m, name) {
      if (!vars || vars[name] == null) return m;
      return typeof vars[name] === 'number' && window.Intl ? new Intl.NumberFormat(I18N.lang).format(vars[name]) : String(vars[name]);
    });
  }
  window.TF = { t: t, lang: I18N.lang };

  function store(key, value) { try { localStorage.setItem(key, value); } catch (e) { /* storage blocked: lasts for this page */ } }
  function read(key) { try { return localStorage.getItem(key); } catch (e) { return null; } }

  // ---------------------------------------------------------------- language: selector, remembered choice, same place on the new page
  var LANG_KEY = 'tf-lang', STATE_KEY = 'tf-lang-state';
  // Opened from disk, folder links need their index.html.
  if (location.protocol === 'file:') {
    document.querySelectorAll('a[data-lang]').forEach(function (a) { a.setAttribute('href', a.getAttribute('href').replace(/\/?$/, '/') .replace(/^\.\/$/, '') + 'index.html'); });
  }
  // ?lang=en (from /en/) is an explicit choice of English.
  if (/[?&]lang=en\b/.test(location.search)) store(LANG_KEY, 'en');

  // Where the visitor is: the section in view and how far into it, the screenshot tab and the open questions.
  function snapshot() {
    var sections = document.querySelectorAll('main section[id]'), at = null, into = 0;
    for (var i = 0; i < sections.length; i++) {
      var r = sections[i].getBoundingClientRect();
      if (r.bottom > 80) { at = sections[i].id; into = r.height ? Math.max(0, (80 - r.top) / r.height) : 0; break; }
    }
    var tab = document.querySelector('[role="tab"][aria-selected="true"]');
    var open = [];
    document.querySelectorAll('.faq details').forEach(function (d, i) { if (d.open) open.push(i); });
    return { section: at, into: into, tab: tab ? tab.id : null, open: open, time: Date.now() };
  }
  function restore() {
    var raw = null;
    try { raw = sessionStorage.getItem(STATE_KEY); sessionStorage.removeItem(STATE_KEY); } catch (e) {}
    if (!raw) return;
    var s; try { s = JSON.parse(raw); } catch (e) { return; }
    if (!s || Date.now() - s.time > 30000) return;
    if (s.tab) { var tab = document.getElementById(s.tab); if (tab) tab.click(); }
    (s.open || []).forEach(function (i) { var d = document.querySelectorAll('.faq details')[i]; if (d) d.open = true; });
    if (s.section && !location.hash) {
      var el = document.getElementById(s.section);
      if (el) {
        root.style.scrollBehavior = 'auto';
        var top = el.getBoundingClientRect().top + window.pageYOffset + s.into * el.offsetHeight - 80;
        window.scrollTo(0, Math.max(0, top));
        el.querySelectorAll('.reveal').forEach(function (r) { r.classList.add('in'); });
        root.style.scrollBehavior = '';
      }
    }
  }
  // Every language link (menu, footer, offer) saves the choice and carries the place over.
  document.addEventListener('click', function (e) {
    var a = e.target.closest && e.target.closest('a[data-lang]');
    if (!a || e.ctrlKey || e.metaKey || e.shiftKey || e.button > 0) return;
    store(LANG_KEY, a.getAttribute('data-lang'));
    if (a.getAttribute('data-lang') === I18N.lang) { e.preventDefault(); closeLang(); return; }
    try { sessionStorage.setItem(STATE_KEY, JSON.stringify(snapshot())); } catch (err) {}
    var params = location.search.replace(/([?&])lang=en\b&?/, '$1').replace(/[?&]$/, '');
    if (params || location.hash) { e.preventDefault(); location.href = a.getAttribute('href') + params + location.hash; }
  });

  var langBox = document.getElementById('lang'), langBtn = document.getElementById('lang-btn'), langMenu = document.getElementById('lang-menu');
  function langLinks() { return Array.prototype.slice.call(langMenu.querySelectorAll('a')); }
  function openLang(focusIndex) {
    langMenu.hidden = false;
    langBtn.setAttribute('aria-expanded', 'true');
    var links = langLinks(), cur = links.filter(function (a) { return a.hasAttribute('aria-current'); })[0];
    (focusIndex === -1 ? links[links.length - 1] : focusIndex != null ? links[focusIndex] : cur || links[0]).focus();
  }
  function closeLang(focusButton) {
    if (!langMenu || langMenu.hidden) return;
    langMenu.hidden = true;
    langBtn.setAttribute('aria-expanded', 'false');
    if (focusButton) langBtn.focus();
  }
  if (langBtn && langMenu) {
    langBtn.addEventListener('click', function () { if (langMenu.hidden) openLang(); else closeLang(); });
    langBtn.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown') { e.preventDefault(); openLang(0); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); openLang(-1); }
    });
    langMenu.addEventListener('keydown', function (e) {
      var links = langLinks(), i = links.indexOf(document.activeElement);
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        links[(i + (e.key === 'ArrowDown' ? 1 : -1) + links.length) % links.length].focus();
      } else if (e.key === 'Home' || e.key === 'End') {
        e.preventDefault(); links[e.key === 'Home' ? 0 : links.length - 1].focus();
      } else if (e.key === 'Escape') {
        e.preventDefault(); closeLang(true);
      } else if (e.key === 'Tab') {
        closeLang();
      }
    });
    document.addEventListener('click', function (e) { if (!langBox.contains(e.target)) closeLang(); });
  }

  // A visitor whose browser prefers another of our languages, and who has not chosen one, is offered it once.
  (function offer() {
    var bar = document.getElementById('lang-suggest');
    if (!bar || read(LANG_KEY) || read('tf-lang-offer-closed')) return;
    var wanted = (navigator.languages || [navigator.language || '']).map(function (l) { return String(l).toLowerCase(); });
    var match = null;
    for (var i = 0; i < wanted.length && !match; i++) {
      I18N.languages.forEach(function (l) {
        if (match) return;
        var code = l.code.toLowerCase();
        if (wanted[i] === code || wanted[i].split('-')[0] === code.split('-')[0]) match = l;
      });
    }
    if (!match || match.code === I18N.lang) return;
    bar.setAttribute('lang', match.code);
    bar.querySelector('.lang-suggest-text').textContent = match.offer.text;
    var go = bar.querySelector('.lang-suggest-go');
    go.textContent = match.offer.go;
    go.setAttribute('href', document.querySelector('#lang-menu a[data-lang="' + match.code + '"]').getAttribute('href'));
    go.setAttribute('data-lang', match.code);
    go.setAttribute('hreflang', match.code);
    var close = bar.querySelector('.lang-suggest-close');
    close.setAttribute('aria-label', match.offer.close);
    close.title = match.offer.close;
    close.addEventListener('click', function () { bar.hidden = true; store('tf-lang-offer-closed', '1'); });
    bar.hidden = false;
  })();

  // ---------------------------------------------------------------- theme: stored choice, else the system's
  var KEY = 'tf-theme';
  function systemDark() { return window.matchMedia && matchMedia('(prefers-color-scheme: dark)').matches; }
  function current() { return root.getAttribute('data-theme') || (systemDark() ? 'dark' : 'light'); }
  function label() {
    var btn = document.getElementById('theme-toggle');
    if (btn) btn.setAttribute('aria-label', current() === 'dark' ? t('themeToLight') : t('themeToDark'));
  }
  var toggle = document.getElementById('theme-toggle');
  if (toggle) {
    toggle.addEventListener('click', function () {
      var next = current() === 'dark' ? 'light' : 'dark';
      root.setAttribute('data-theme', next);
      store(KEY, next);
      label();
    });
  }
  // Without a stored choice the page follows the system theme as it changes (CSS does it; the button's label follows).
  if (window.matchMedia) {
    var mq = matchMedia('(prefers-color-scheme: dark)');
    if (mq.addEventListener) mq.addEventListener('change', label);
  }
  label();

  // ---------------------------------------------------------------- menu (narrow screens)
  var header = document.querySelector('.site-header');
  var menuBtn = document.getElementById('menu-btn');
  function setOpen(open) {
    header.classList.toggle('open', open);
    menuBtn.setAttribute('aria-expanded', String(open));
  }
  if (menuBtn) {
    menuBtn.addEventListener('click', function () { closeLang(); setOpen(!header.classList.contains('open')); });
    header.querySelector('.nav').addEventListener('click', function (e) { if (e.target.closest('a')) setOpen(false); });
    document.addEventListener('click', function (e) { if (header.classList.contains('open') && !header.contains(e.target)) setOpen(false); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') setOpen(false); });
  }

  // ---------------------------------------------------------------- screenshot tabs
  document.querySelectorAll('[role="tablist"]').forEach(function (list) {
    var tabs = Array.prototype.slice.call(list.querySelectorAll('[role="tab"]'));
    function select(tab) {
      tabs.forEach(function (t) {
        var on = t === tab;
        t.setAttribute('aria-selected', String(on));
        t.tabIndex = on ? 0 : -1;
        document.getElementById(t.getAttribute('aria-controls')).hidden = !on;
      });
    }
    tabs.forEach(function (tab, i) {
      tab.addEventListener('click', function () { select(tab); });
      tab.addEventListener('keydown', function (e) {
        var j = e.key === 'ArrowRight' ? i + 1 : e.key === 'ArrowLeft' ? i - 1 : -1;
        if (j < 0 && e.key !== 'ArrowLeft') return;
        j = (j + tabs.length) % tabs.length;
        tabs[j].focus();
        select(tabs[j]);
        e.preventDefault();
      });
    });
  });

  // ---------------------------------------------------------------- lightbox
  var box = document.getElementById('lightbox');
  if (box && typeof box.showModal === 'function') {
    var big = box.querySelector('img'), cap = box.querySelector('p');
    document.addEventListener('click', function (e) {
      var btn = e.target.closest('[data-zoom]');
      if (!btn) return;
      // A themed screenshot zooms the variant that is visible.
      var img = Array.prototype.slice.call(btn.querySelectorAll('img')).filter(function (i) { return i.offsetParent !== null; })[0] || btn.querySelector('img');
      big.src = img.currentSrc || img.src;
      big.alt = img.alt;
      cap.textContent = btn.getAttribute('data-zoom') || img.alt;
      box.showModal();
    });
    box.addEventListener('click', function (e) { if (e.target === box || e.target.closest('.close')) box.close(); });
  }

  // ---------------------------------------------------------------- reveal on scroll (off with reduced motion)
  var items = document.querySelectorAll('.reveal');
  if ('IntersectionObserver' in window && !(window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches)) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) { if (en.isIntersecting) { en.target.classList.add('in'); io.unobserve(en.target); } });
    }, { rootMargin: '0px 0px -8% 0px' });
    items.forEach(function (el) { io.observe(el); });
  } else {
    items.forEach(function (el) { el.classList.add('in'); });
  }
  restore();

  // ---------------------------------------------------------------- analytics: one event per download, releases, repository or issues link
  var APP_VERSION = '__VERSION__';
  var EVENTS = { download: 'download_click', releases: 'releases_click', repository: 'repository_click', issues: 'issues_click' };
  document.addEventListener('click', function (e) {
    var link = e.target.closest && e.target.closest('a[data-track]');
    if (!link || typeof gtag !== 'function') return;
    var kind = link.getAttribute('data-track');
    gtag('event', EVENTS[kind] || 'outbound_click', {
      link_url: link.href, link_category: kind, link_location: link.getAttribute('data-place') || '', app_version: APP_VERSION,
      page_language: I18N.lang
    });
  });
})();
