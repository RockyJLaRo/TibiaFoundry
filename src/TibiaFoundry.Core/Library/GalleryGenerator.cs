using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Library;

/// <summary>One image of a gallery or manifest (paths relative to the output folder, with '/').</summary>
public sealed record PublishedItem
{
    public required string Name { get; init; }
    public required string File { get; init; }
    public string Category { get; init; } = "";
    public string? Id { get; init; }
    public string? ClientVersion { get; init; }
    public string? Description { get; init; }
    public string? Group { get; init; }
    public string Format { get; init; } = "png";
    public int Width { get; init; }
    public int Height { get; init; }
    public int Frames { get; init; } = 1;
    public string? Attribution { get; init; }
    public string? Source { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public bool Animated => Frames > 1;
}

public enum GalleryTheme { Auto, Light, Dark }

public sealed record GalleryOptions
{
    public string Title { get; init; } = L.T("GalleryGenerator.TibiaGallery");
    public string? Subtitle { get; init; }
    public GalleryTheme Theme { get; init; } = GalleryTheme.Auto;
    /// <summary>Accent color "#RRGGBB".</summary>
    public string Accent { get; init; } = "#2D71E1";
    /// <summary>Minimum tile width in CSS pixels.</summary>
    public int TileSize { get; init; } = 150;
    /// <summary>Credit line in the footer.</summary>
    public string? Footer { get; init; } = L.T("GalleryGenerator.TibiaGraphicsCipSoftGmbHGalleryGenerated");
}

/// <summary>
/// Writes a self-contained HTML gallery (8.8): one <c>index.html</c> with its CSS and script inside,
/// next to the images it shows. Thumbnails, full-size view, categories, search, metadata,
/// attribution, animations, responsive layout and light/dark themes; no server, database or
/// external file is needed, so it works opened from disk or on any web host. All text from assets is
/// written as JSON data and shown with <c>textContent</c>, so names can never inject markup.
/// </summary>
public static class GalleryGenerator
{
    public static string Html(IReadOnlyList<PublishedItem> items, GalleryOptions options)
    {
        string accent = System.Text.RegularExpressions.Regex.IsMatch(options.Accent, "^#[0-9A-Fa-f]{6}$") ? options.Accent : "#2D71E1";
        var data = items.Select(i => new
        {
            n = i.Name, f = i.File, c = i.Category, id = i.Id, v = i.ClientVersion, d = i.Description, g = i.Group, w = i.Width, h = i.Height,
            a = i.Animated, at = i.Attribution, t = i.Tags, m = i.Metadata,
        });
        // UnsafeRelaxedJsonEscaping is NOT used: the default encoder escapes <, > and &, so the data cannot close the script element.
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { Encoder = JavaScriptEncoder.Default, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        string theme = options.Theme switch { GalleryTheme.Light => "light", GalleryTheme.Dark => "dark", _ => "" };
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"en\"").Append(theme.Length > 0 ? $" data-theme=\"{theme}\"" : "").Append(L.T("GalleryGenerator.HeadMetaCharsetUtf8"));
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<meta name=\"generator\" content=\"").Append(WebUtility.HtmlEncode(AppIdentity.NameAndVersion)).Append("\">\n");
        sb.Append("<title>").Append(WebUtility.HtmlEncode(options.Title)).Append("</title>\n<style>\n");
        sb.Append(Css.Replace("__ACCENT__", accent).Replace("__TILE__", Math.Clamp(options.TileSize, 80, 400).ToString(CultureInfo.InvariantCulture)));
        sb.Append("</style>\n</head>\n<body>\n<header class=\"top\">\n<div class=\"wrap\">\n<div class=\"heading\"><h1>").Append(WebUtility.HtmlEncode(options.Title)).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(options.Subtitle)) sb.Append("<p class=\"sub\">").Append(WebUtility.HtmlEncode(options.Subtitle)).Append("</p>");
        sb.Append("</div>\n<div class=\"tools\"><input id=\"q\" type=\"search\" placeholder=\"Search names, IDs, tags\" aria-label=\"Search\">");
        sb.Append("<button id=\"theme\" type=\"button\" aria-label=\"Switch between light and dark\">◐</button></div>\n</div>\n");
        sb.Append("<nav class=\"wrap chips\" id=\"cats\" aria-label=\"Categories\"></nav>\n</header>\n");
        sb.Append("<main class=\"wrap\"><p id=\"count\" class=\"count\" aria-live=\"polite\"></p><ul id=\"grid\" class=\"grid\"></ul></main>\n");
        sb.Append("<dialog id=\"view\" aria-label=\"Image details\"><button class=\"close\" id=\"close\" aria-label=\"Close\">✕</button>");
        sb.Append("<div class=\"stage\"><img id=\"big\" alt=\"\"></div><div class=\"info\"><h2 id=\"vname\"></h2><p id=\"vdesc\"></p><dl id=\"vmeta\"></dl><p id=\"vattr\" class=\"attr\"></p>");
        sb.Append("<p><a id=\"vopen\" target=\"_blank\" rel=\"noopener\">Open the file</a></p></div></dialog>\n");
        sb.Append("<footer class=\"wrap foot\">").Append(WebUtility.HtmlEncode(options.Footer ?? "")).Append("</footer>\n");
        sb.Append("<script type=\"application/json\" id=\"data\">").Append(json).Append("</script>\n<script>\n").Append(Script).Append("</script>\n</body>\n</html>\n");
        return sb.ToString();
    }

    public static void Write(string folder, IReadOnlyList<PublishedItem> items, GalleryOptions options, string fileName = "index.html") =>
        AtomicFile.WriteAllText(Path.Combine(folder, fileName), Html(items, options));

    private const string Css = """
:root{--bg:#f6f7f9;--card:#fff;--text:#1d2329;--muted:#5d6874;--line:#dde2e8;--accent:__ACCENT__;--check1:#eef0f3;--check2:#e2e6ea}
@media (prefers-color-scheme:dark){:root:not([data-theme=light]){--bg:#16191d;--card:#20242a;--text:#e6e9ed;--muted:#a1abb7;--line:#343a42;--check1:#262b31;--check2:#2e343b}}
:root[data-theme=dark]{--bg:#16191d;--card:#20242a;--text:#e6e9ed;--muted:#a1abb7;--line:#343a42;--check1:#262b31;--check2:#2e343b}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
.wrap{max-width:1200px;margin:0 auto;padding:0 16px}.top{position:sticky;top:0;background:color-mix(in srgb,var(--bg) 92%,transparent);backdrop-filter:blur(8px);border-bottom:1px solid var(--line);z-index:2}
.top .wrap:first-child{display:flex;gap:16px;align-items:center;justify-content:space-between;flex-wrap:wrap;padding-top:14px;padding-bottom:10px}
h1{font-size:22px;margin:0}.sub{margin:2px 0 0;color:var(--muted)}.tools{display:flex;gap:8px;flex:1;justify-content:flex-end;min-width:220px}
input[type=search]{flex:1;max-width:340px;padding:8px 12px;border:1px solid var(--line);border-radius:8px;background:var(--card);color:var(--text);font:inherit}
button{font:inherit;color:var(--text);background:var(--card);border:1px solid var(--line);border-radius:8px;padding:6px 12px;cursor:pointer}
button:focus-visible,input:focus-visible,a:focus-visible,.tile:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
.chips{display:flex;gap:6px;flex-wrap:wrap;padding-top:4px;padding-bottom:12px}.chips button{border-radius:999px;padding:3px 12px;font-size:13px}
.chips button[aria-pressed=true]{background:var(--accent);border-color:var(--accent);color:#fff}
.count{color:var(--muted);font-size:13px;margin:14px 0 8px}.grid{list-style:none;margin:0 0 32px;padding:0;display:grid;gap:12px;grid-template-columns:repeat(auto-fill,minmax(__TILE__px,1fr))}
.tile{display:flex;flex-direction:column;width:100%;height:100%;padding:0;border-radius:10px;overflow:hidden;text-align:left;transition:transform .15s,box-shadow .15s}
.tile:hover{transform:translateY(-2px);box-shadow:0 6px 18px rgba(0,0,0,.12)}
.thumb{aspect-ratio:1;display:grid;place-items:center;background:repeating-conic-gradient(var(--check1) 0 25%,var(--check2) 0 50%) 0 0/16px 16px}
.thumb img{max-width:90%;max-height:90%;image-rendering:pixelated;image-rendering:crisp-edges}
.label{padding:8px 10px;border-top:1px solid var(--line)}.label b{display:block;font-weight:600;font-size:14px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.label span{color:var(--muted);font-size:12px}
dialog{border:1px solid var(--line);border-radius:14px;background:var(--card);color:var(--text);padding:0;max-width:min(920px,94vw);width:100%}
dialog::backdrop{background:rgba(0,0,0,.6)}dialog[open]{display:grid;grid-template-columns:minmax(0,1.3fr) minmax(0,1fr)}
.stage{display:grid;place-items:center;min-height:320px;background:repeating-conic-gradient(var(--check1) 0 25%,var(--check2) 0 50%) 0 0/20px 20px}
.stage img{max-width:100%;max-height:70vh;image-rendering:pixelated;image-rendering:crisp-edges}
.info{padding:20px 22px;overflow:auto;max-height:80vh}.info h2{margin:0 0 6px;font-size:20px}dl{display:grid;grid-template-columns:auto 1fr;gap:4px 12px;font-size:13px}
dt{color:var(--muted)}dd{margin:0;word-break:break-word}.attr{font-size:12px;color:var(--muted)}a{color:var(--accent)}
.close{position:absolute;top:10px;right:10px}.foot{color:var(--muted);font-size:12px;padding-bottom:28px}
@media (max-width:700px){dialog[open]{grid-template-columns:1fr}.stage{min-height:220px}}
@media (prefers-reduced-motion:reduce){.tile{transition:none}.tile:hover{transform:none}}
""";

    private const string Script = """
(function(){
const items=JSON.parse(document.getElementById('data').textContent);
const grid=document.getElementById('grid'),q=document.getElementById('q'),cats=document.getElementById('cats'),count=document.getElementById('count');
const view=document.getElementById('view');let current='';const u=f=>String(f).split('/').map(encodeURIComponent).join('/');
const categories=[...new Set(items.map(i=>i.c).filter(Boolean))].sort();
function chip(name,label){const b=document.createElement('button');b.type='button';b.textContent=label;b.setAttribute('aria-pressed',String(current===name));
 b.onclick=()=>{current=name;[...cats.children].forEach(c=>c.setAttribute('aria-pressed','false'));b.setAttribute('aria-pressed','true');render();};return b;}
if(categories.length>1){cats.append(chip('','All'));categories.forEach(c=>cats.append(chip(c,c)));}else{cats.hidden=true;}
function text(i){return [i.n,i.id,i.c,i.v,i.d,i.g,...(i.t||[])].filter(Boolean).join(' ').toLowerCase();}
function render(){const words=q.value.toLowerCase().split(/\s+/).filter(Boolean);grid.textContent='';let n=0;
 for(const i of items){if(current&&i.c!==current)continue;const t=text(i);if(!words.every(w=>t.includes(w)))continue;n++;
  const li=document.createElement('li');const b=document.createElement('button');b.className='tile';b.type='button';
  const th=document.createElement('div');th.className='thumb';const img=document.createElement('img');img.loading='lazy';img.src=u(i.f);img.alt=i.n;th.append(img);
  const lab=document.createElement('div');lab.className='label';const nm=document.createElement('b');nm.textContent=i.n;const sm=document.createElement('span');
  sm.textContent=[i.id?('#'+i.id):'',i.w&&i.h?(i.w+'×'+i.h):'',i.a?'animated':''].filter(Boolean).join(' · ');lab.append(nm,sm);b.append(th,lab);
  b.onclick=()=>open(i);li.append(b);grid.append(li);}
 count.textContent=n+(n===1?' image':' images');}
function open(i){document.getElementById('big').src=u(i.f);document.getElementById('big').alt=i.n;document.getElementById('vname').textContent=i.n;
 document.getElementById('vdesc').textContent=i.d||'';const dl=document.getElementById('vmeta');dl.textContent='';
 const rows=[['Category',i.c],['ID',i.id],['Client version',i.v],['Size',i.w&&i.h?(i.w+'×'+i.h+' px'):''],['Animated',i.a?'yes':''],['Group',i.g],['Tags',(i.t||[]).join(', ')],...Object.entries(i.m||{})];
 for(const [k,v] of rows){if(!v)continue;const dt=document.createElement('dt');dt.textContent=k;const dd=document.createElement('dd');dd.textContent=v;dl.append(dt,dd);}
 document.getElementById('vattr').textContent=i.at||'';const a=document.getElementById('vopen');a.href=u(i.f);
 if(typeof view.showModal==='function')view.showModal();else view.setAttribute('open','');}
document.getElementById('close').onclick=()=>view.close();view.addEventListener('click',e=>{if(e.target===view)view.close();});
q.addEventListener('input',render);
const root=document.documentElement;try{const s=localStorage.getItem('tf-gallery-theme');if(s)root.dataset.theme=s;}catch(e){}
document.getElementById('theme').onclick=()=>{const dark=root.dataset.theme?root.dataset.theme==='dark':matchMedia('(prefers-color-scheme: dark)').matches;
 root.dataset.theme=dark?'light':'dark';try{localStorage.setItem('tf-gallery-theme',root.dataset.theme);}catch(e){}};
render();})();
""";
}

/// <summary>JSON, CSV and Markdown lists of published images (8.8).</summary>
public static class ManifestWriter
{
    public static string Json(IReadOnlyList<PublishedItem> items, string title, ExportProfile? profile = null) =>
        JsonSerializer.Serialize(new
        {
            title, generator = AppIdentity.NameAndVersion, created = DateTime.UtcNow, profile = profile?.Name, website = profile?.Website,
            count = items.Count, items = items.Select(i => new
            {
                name = i.Name, file = i.File, category = i.Category, id = i.Id, clientVersion = i.ClientVersion, description = i.Description, group = i.Group,
                format = i.Format, width = i.Width, height = i.Height, frames = i.Frames, animated = i.Animated, attribution = i.Attribution, source = i.Source,
                tags = i.Tags, metadata = i.Metadata,
            }),
        }, LibraryJson.Options);

    public static string Csv(IReadOnlyList<PublishedItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("name,file,category,id,client_version,format,width,height,frames,group,description,tags,attribution");
        foreach (var i in items)
            sb.AppendLine(string.Join(",", new[]
            {
                i.Name, i.File, i.Category, i.Id, i.ClientVersion, i.Format, i.Width.ToString(CultureInfo.InvariantCulture), i.Height.ToString(CultureInfo.InvariantCulture),
                i.Frames.ToString(CultureInfo.InvariantCulture), i.Group, i.Description, string.Join("; ", i.Tags), i.Attribution,
            }.Select(CsvCell)));
        return sb.ToString();
    }

    /// <summary>A CSV cell: quoted when needed, and text a spreadsheet would run as a formula is prefixed with an apostrophe.</summary>
    public static string CsvCell(string? value)
    {
        value ??= "";
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string Markdown(IReadOnlyList<PublishedItem> items, string title, string? footer = null)
    {
        static string Md(string? s) => (s ?? "").Replace("|", "\\|").Replace("\n", " ").Replace("[", "\\[").Replace("]", "\\]");
        var sb = new StringBuilder();
        sb.AppendLine("# " + Md(title)).AppendLine();
        sb.AppendLine(L.F("GalleryGenerator.0Generated1YyyyMMDd", L.Count(items.Count, "Noun.image"), DateTime.Now, AppIdentity.NameAndVersion)).AppendLine();
        foreach (var g in items.GroupBy(i => i.Category).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine("## " + Md(string.IsNullOrEmpty(g.Key) ? "Images" : g.Key)).AppendLine();
            sb.AppendLine(L.T("GalleryGenerator.ImageNameIDSizeFile")).AppendLine("|---|---|---|---|---|");
            foreach (var i in g)
                sb.AppendLine($"| ![{Md(i.Name)}]({Uri.EscapeDataString(i.File).Replace("%2F", "/")}) | {Md(i.Name)} | {Md(i.Id)} | {i.Width}×{i.Height} | `{i.File.Replace("`", "'")}` |");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(footer)) sb.AppendLine("*" + Md(footer) + "*");
        return sb.ToString();
    }
}
