/*
 * Allow-list HTML sanitizer for news articles from the API (content_html). Nothing from the API is ever assigned
 * to innerHTML: the markup is parsed into an inert <template>, and a new fragment is built from the elements and
 * attributes below only. Everything else is dropped:
 *   - script, style, iframe, object, embed, form, input, svg, math, link, meta, base, template... with their content;
 *   - unknown elements are unwrapped (their text kept);
 *   - every attribute except the few listed; no on* handlers, no style, no class/id from the source;
 *   - links only to http(s) (opened in a new tab with rel="noopener noreferrer nofollow"); images only from https.
 * window.TFSanitize.fragment(html, document) -> DocumentFragment; .text(html) -> plain text.
 */
(function (root) {
  "use strict";
  var DROP = /^(script|style|iframe|frame|frameset|object|embed|applet|form|input|button|select|textarea|option|svg|math|link|meta|base|template|noscript|audio|video|source|track|canvas|dialog|portal)$/;
  var ALLOWED = {
    p: [], br: [], b: [], strong: [], i: [], em: [], u: [], s: [], small: [], sub: [], sup: [], span: [], div: [],
    h2: [], h3: [], h4: [], h5: [], blockquote: [], hr: [], ul: [], ol: [], li: [], center: [],
    table: [], thead: [], tbody: [], tr: [], td: ["colspan", "rowspan"], th: ["colspan", "rowspan"],
    a: ["href", "title"], img: ["src", "alt", "width", "height", "title"]
  };
  var RENAME = { center: "div", h1: "h2" };

  function safeUrl(value, imagesOnly) {
    if (!value) return null;
    var v = String(value).trim();
    // Reject control characters and anything that is not plain http(s).
    if (/[\u0000-\u001f\u007f]/.test(v)) return null;
    var m = /^([a-z][a-z0-9+.-]*):/i.exec(v);
    if (!m) return null;                       // relative URLs: the article's base is tibia.com, not our site
    var scheme = m[1].toLowerCase();
    if (imagesOnly ? scheme !== "https" : (scheme !== "https" && scheme !== "http")) return null;
    try { return new URL(v).href; } catch (e) { return null; }
  }

  function clean(node, out, doc) {
    for (var child = node.firstChild; child; child = child.nextSibling) {
      if (child.nodeType === 3) { out.appendChild(doc.createTextNode(child.nodeValue)); continue; }
      if (child.nodeType !== 1) continue;      // comments, processing instructions
      var tag = child.nodeName.toLowerCase();
      if (DROP.test(tag)) continue;
      var target = RENAME[tag] || tag;
      if (!Object.prototype.hasOwnProperty.call(ALLOWED, target)) { clean(child, out, doc); continue; }
      var el = doc.createElement(target);
      ALLOWED[target].forEach(function (name) {
        var value = child.getAttribute(name);
        if (value == null) return;
        if (name === "href") { value = safeUrl(value, false); if (!value) return; }
        if (name === "src") { value = safeUrl(value, true); if (!value) return; }
        if ((name === "width" || name === "height" || name === "colspan" || name === "rowspan") && !/^\d{1,4}$/.test(value)) return;
        el.setAttribute(name, value);
      });
      if (target === "a") {
        if (!el.hasAttribute("href")) { clean(child, out, doc); continue; }
        el.setAttribute("target", "_blank");
        el.setAttribute("rel", "noopener noreferrer nofollow");
      }
      if (target === "img") {
        if (!el.hasAttribute("src")) continue;
        el.setAttribute("loading", "lazy");
        el.setAttribute("decoding", "async");
        el.setAttribute("referrerpolicy", "no-referrer");
        if (!el.hasAttribute("alt")) el.setAttribute("alt", "");
      }
      clean(child, el, doc);
      out.appendChild(el);
    }
    return out;
  }

  function fragment(html, doc) {
    doc = doc || root.document;
    var template = doc.createElement("template");
    // A <template>'s content is inert: no script runs and no image loads while it is parsed.
    template.innerHTML = String(html == null ? "" : html);
    return clean(template.content, doc.createDocumentFragment(), doc);
  }

  function text(html, doc) {
    doc = doc || root.document;
    var template = doc.createElement("template");
    template.innerHTML = String(html == null ? "" : html);
    return (template.content.textContent || "").replace(/\s+/g, " ").trim();
  }

  root.TFSanitize = { fragment: fragment, text: text, safeUrl: safeUrl };
})(typeof self !== "undefined" ? self : this);
