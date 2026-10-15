# Generates FTP-style directory listings for the GitHub Pages download site:
#   <root>\index.html                      landing page, latest downloads
#   <root>\downloads\index.html            "Index of /downloads/"  (all versions)
#   <root>\downloads\<version>\index.html  "Index of /downloads/<version>/" (files, size, date, SHA-256)
# Run by .github/workflows/release.yml after copying a new version into <root>\downloads\<version>\.
# Usage: pwsh installer\make-pages-index.ps1 -Root path\to\gh-pages-checkout
param([Parameter(Mandatory)][string]$Root)
$ErrorActionPreference = "Stop"
$Root = (Resolve-Path $Root).Path
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$enc = New-Object System.Text.UTF8Encoding($false)
$site = "https://github.com/smtanbin/tool-box"

function Esc($s) { [System.Net.WebUtility]::HtmlEncode([string]$s) }
function Size($b) {
    if ($b -ge 1MB) { "{0:N1} MB" -f ($b / 1MB) } elseif ($b -ge 1KB) { "{0:N0} KB" -f ($b / 1KB) } else { "$b B" }
}
function Stamp($path) {
    # Prefer the git commit date (stable); fall back to file time.
    $d = $null
    try { $d = (git -C $Root log -1 --format=%cd --date=format:"%Y-%m-%d %H:%M" -- $path 2>$null) } catch {}
    if (-not $d) { $d = (Get-Item (Join-Path $Root $path)).LastWriteTime.ToString("yyyy-MM-dd HH:mm") }
    $d
}

$icons = @{
    folder = '<svg viewBox="0 0 24 24" width="20" height="20"><path fill="#f6b73c" d="M10 4H4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-8l-2-2z"/></svg>'
    up     = '<svg viewBox="0 0 24 24" width="20" height="20"><path fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" d="M12 19V6m-6 6 6-6 6 6"/></svg>'
    pkg    = '<svg viewBox="0 0 24 24" width="20" height="20"><path fill="#0f6cbd" d="M12 2 3 6.5v11L12 22l9-4.5v-11L12 2z"/><path fill="#4cc2ff" d="M12 2 3 6.5 12 11l9-4.5L12 2z"/><path fill="#fff" fill-opacity=".35" d="M12 11v11l9-4.5v-11L12 11z"/></svg>'
    text   = '<svg viewBox="0 0 24 24" width="20" height="20"><path fill="#90a4ae" d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6zm-1 7V3.5L18.5 9H13z"/></svg>'
    copy   = '<svg viewBox="0 0 24 24" width="15" height="15"><path fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" d="M9 9h10v12H9zM5 15V3h10"/></svg>'
}

$css = @'
:root{color-scheme:light dark;--bg:#f3f3f3;--card:#fff;--text:#1a1a1a;--muted:#5f5f5f;--accent:#0067c0;--border:rgba(0,0,0,.09);--hover:rgba(0,0,0,.045);--chip:rgba(0,103,192,.1);--shadow:0 2px 8px rgba(0,0,0,.06)}
@media (prefers-color-scheme:dark){:root{--bg:#1c1c1c;--card:#2a2a2a;--text:#fff;--muted:#a6a6a6;--accent:#60cdff;--border:rgba(255,255,255,.09);--hover:rgba(255,255,255,.06);--chip:rgba(96,205,255,.14);--shadow:none}}
*{box-sizing:border-box}
body{margin:0;background:radial-gradient(900px 500px at 0 0,color-mix(in srgb,var(--accent) 14%,transparent),transparent 60%),var(--bg);color:var(--text);font:15px/1.5 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;min-height:100vh}
main{max-width:920px;margin:0 auto;padding:40px 20px 60px}
header{display:flex;align-items:center;gap:16px;margin-bottom:26px}
header img{width:56px;height:56px}
h1{font-size:26px;margin:0;font-weight:600}
.sub{color:var(--muted);margin:2px 0 0}
.crumbs{font:13px ui-monospace,Consolas,monospace;color:var(--muted);margin:0 0 12px;display:flex;flex-wrap:wrap;gap:2px;align-items:center}
.crumbs a{color:var(--accent);text-decoration:none;padding:2px 4px;border-radius:4px}.crumbs a:hover{background:var(--hover)}
.card{background:var(--card);border:1px solid var(--border);border-radius:10px;box-shadow:var(--shadow);overflow:hidden}
.card h2{font-size:14px;margin:0;padding:14px 18px;border-bottom:1px solid var(--border);font-weight:600}
table{width:100%;border-collapse:collapse}
th{font-weight:600;font-size:12px;color:var(--muted);text-align:left;padding:10px 18px;border-bottom:1px solid var(--border);white-space:nowrap}
td{padding:11px 18px;border-bottom:1px solid var(--border);vertical-align:middle}
tr:last-child td{border-bottom:0}
tbody tr:hover{background:var(--hover)}
td.n{white-space:nowrap}td.n svg{vertical-align:middle;margin-right:12px}
td.n a{color:var(--text);text-decoration:none;font-weight:500;vertical-align:middle}td.n a:hover{color:var(--accent);text-decoration:underline}
td.m,th.m{color:var(--muted);white-space:nowrap}td.s,th.s{text-align:right;white-space:nowrap}
.mono{font:12px ui-monospace,Consolas,monospace}
.chip{display:inline-block;font-size:11px;font-weight:600;padding:1px 8px;border-radius:10px;background:var(--chip);color:var(--accent);margin-left:6px;vertical-align:1px}
.btn{display:inline-flex;align-items:center;gap:6px;border:1px solid var(--border);border-radius:6px;background:var(--card);color:var(--text);padding:4px 10px;font:inherit;font-size:12px;cursor:pointer}
.btn:hover{background:var(--hover)}
.dl{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:12px;margin-bottom:22px}
.dl a{display:flex;align-items:center;gap:14px;background:var(--card);border:1px solid var(--border);border-radius:10px;padding:16px 18px;text-decoration:none;color:var(--text);box-shadow:var(--shadow)}
.dl a:hover{border-color:var(--accent)}.dl b{display:block;font-weight:600}.dl span{color:var(--muted);font-size:13px}
.dl .big{background:var(--accent);color:#fff;border-color:transparent}.dl .big span{color:rgba(255,255,255,.85)}
footer{margin-top:26px;color:var(--muted);font-size:13px;text-align:center}
footer a{color:var(--accent);text-decoration:none}footer a:hover{text-decoration:underline}
.empty{padding:28px;text-align:center;color:var(--muted)}
@media (max-width:640px){.hide-s{display:none}td,th{padding:10px 12px}}
'@

$js = @'
<script>
document.addEventListener("click",function(e){var b=e.target.closest("[data-copy]");if(!b)return;
var t=b.getAttribute("data-copy");if(t.indexOf("http")!==0&&t.charAt(0)==="/")t=location.origin+t;
var old=b.innerHTML;function done(){b.textContent="Copied";setTimeout(function(){b.innerHTML=old},1400)}
if(navigator.clipboard){navigator.clipboard.writeText(t).then(done,done)}else{done()}});
</script>
'@

function Page($title, $heading, $subtitle, $crumbHtml, $bodyHtml, $depth) {
    $up = ("../" * $depth)
    @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>$(Esc $title)</title>
<link rel="icon" type="image/svg+xml" href="${up}icon.svg">
<style>$css</style>
</head>
<body>
<main>
<header><img src="${up}icon.svg" alt=""><div><h1>$(Esc $heading)</h1><p class="sub">$(Esc $subtitle)</p></div></header>
$crumbHtml
$bodyHtml
<footer><a href="$site">Source code</a> · <a href="$site/releases">GitHub releases and portable builds</a> · <a href="$site/blob/main/PRIVACY.md">Privacy policy</a> · <a href="https://magestica.com/">magestica.com</a><br>Free for personal and business use, not for resale · Apache 2.0 + Commons Clause</footer>
</main>
$js
</body>
</html>
"@
}

function Crumbs($parts) {
    # $parts: ordered list of @{name; href}; last one is plain text
    $h = '<nav class="crumbs">'
    for ($i = 0; $i -lt $parts.Count; $i++) {
        if ($i -gt 0) { $h += "<span>/</span>" }
        if ($i -lt $parts.Count - 1) { $h += "<a href=""$($parts[$i].href)"">$(Esc $parts[$i].name)</a>" } else { $h += "<span style=""padding:2px 4px"">$(Esc $parts[$i].name)</span>" }
    }
    $h + "</nav>"
}

# favicon / logo
$iconSrc = Join-Path $here "..\web\icon.svg"
if (Test-Path $iconSrc) { Copy-Item $iconSrc (Join-Path $Root "icon.svg") -Force }

$dl = Join-Path $Root "downloads"
New-Item -ItemType Directory -Force $dl | Out-Null
$versions = Get-ChildItem $dl -Directory | Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending
if (-not $versions) { throw "No version folders found in $dl" }
$latest = $versions[0].Name

# ---- /downloads/<version>/index.html ----
foreach ($v in $versions) {
    $vd = $v.FullName
    $sums = @{}
    $sf = Join-Path $vd "SHA256SUMS.txt"
    if (Test-Path $sf) { foreach ($l in Get-Content $sf) { if ($l -match '^([0-9a-fA-F]{64})\s+\*?(.+)$') { $sums[$Matches[2].Trim()] = $Matches[1].ToLower() } } }
    $order = @{ x64 = 0; x86 = 1; arm64 = 2 }
    $files = Get-ChildItem $vd -File | Where-Object { $_.Name -ne "index.html" } | Sort-Object { if ($_.Extension -eq ".msi") { 0 } else { 1 } }, { if ($_.Name -match '-(x64|x86|arm64)\.') { $order[$Matches[1]] } else { 9 } }, Name
    $rows = '<tr><td class="n">' + $icons.up + '<a href="../">Parent directory</a></td><td class="m hide-s"></td><td class="s"></td><td class="hide-s"></td><td></td></tr>'
    foreach ($f in $files) {
        $arch = ""; if ($f.Name -match '-(x64|x86|arm64)\.msi$') { $arch = '<span class="chip">' + $Matches[1] + '</span>' }
        $ico = if ($f.Extension -eq ".msi") { $icons.pkg } else { $icons.text }
        $hash = $sums[$f.Name]
        $hashCell = if ($hash) { "<button class=""btn mono"" data-copy=""$hash"" title=""Copy SHA-256"">$($hash.Substring(0,12))… $($icons.copy)</button>" } else { "" }
        $path = "downloads/$($v.Name)/$($f.Name)"
        $rows += "<tr><td class=""n"">$ico<a href=""$(Esc $f.Name)"">$(Esc $f.Name)</a>$arch</td><td class=""m hide-s"">$(Stamp $path)</td><td class=""s m"">$(Size $f.Length)</td><td class=""hide-s"">$hashCell</td><td class=""s""><button class=""btn"" data-copy=""/tool-box/$path"" title=""Copy direct download URL"">$($icons.copy) URL</button></td></tr>"
    }
    $body = "<div class=""card""><table><thead><tr><th>Name</th><th class=""m hide-s"">Last modified</th><th class=""s"">Size</th><th class=""hide-s"">SHA-256</th><th></th></tr></thead><tbody>$rows</tbody></table></div>"
    $crumbs = Crumbs @(@{name="tool-box"; href="../../"}, @{name="downloads"; href="../"}, @{name=$v.Name; href=""})
    $html = Page "Index of /downloads/$($v.Name)/ — My Tool Box" "Index of /downloads/$($v.Name)/" "My Tool Box $($v.Name) · direct, non-redirecting download links" $crumbs $body 2
    [IO.File]::WriteAllText((Join-Path $vd "index.html"), $html, $enc)
}

# ---- /downloads/index.html ----
$rows = '<tr><td class="n">' + $icons.up + '<a href="../">Parent directory</a></td><td class="m hide-s"></td><td class="s"></td></tr>'
foreach ($v in $versions) {
    $count = (Get-ChildItem $v.FullName -File | Where-Object Name -ne "index.html").Count
    $tag = if ($v.Name -eq $latest) { '<span class="chip">latest</span>' } else { "" }
    $rows += "<tr><td class=""n"">$($icons.folder)<a href=""$($v.Name)/"">$($v.Name)/</a>$tag</td><td class=""m hide-s"">$(Stamp "downloads/$($v.Name)")</td><td class=""s m"">$count files</td></tr>"
}
$body = "<div class=""card""><table><thead><tr><th>Name</th><th class=""m hide-s"">Last modified</th><th class=""s"">Contents</th></tr></thead><tbody>$rows</tbody></table></div>"
$crumbs = Crumbs @(@{name="tool-box"; href="../"}, @{name="downloads"; href=""})
[IO.File]::WriteAllText((Join-Path $dl "index.html"), (Page "Index of /downloads/ — My Tool Box" "Index of /downloads/" "$($versions.Count) version$(if($versions.Count -ne 1){'s'}) available" $crumbs $body 1), $enc)

# ---- /index.html ----
$cards = ""
$first = $true
foreach ($a in @(@("x64", "64-bit Windows", "Most PCs"), @("x86", "32-bit Windows", "Older PCs"), @("arm64", "Windows on ARM", "Snapdragon and other ARM PCs"))) {
    $name = "ToolBoxSetup-$latest-$($a[0]).msi"
    if (-not (Test-Path (Join-Path $dl "$latest\$name"))) { continue }
    $cls = if ($first) { ' class="big"' } else { "" }; $first = $false
    $cards += "<a$cls href=""downloads/$latest/$name"">$($icons.pkg)<div><b>$($a[1])</b><span>$($a[2]) · $latest</span></div></a>"
}
$body = "<div class=""dl"">$cards</div><div class=""card""><h2>All versions</h2><table><tbody>"
foreach ($v in $versions | Select-Object -First 5) {
    $tag = if ($v.Name -eq $latest) { '<span class="chip">latest</span>' } else { "" }
    $body += "<tr><td class=""n"">$($icons.folder)<a href=""downloads/$($v.Name)/"">$($v.Name)/</a>$tag</td><td class=""m hide-s"">$(Stamp "downloads/$($v.Name)")</td></tr>"
}
$body += "</tbody></table><div style=""padding:12px 18px;border-top:1px solid var(--border)""><a class=""btn"" href=""downloads/"" style=""text-decoration:none"">Browse all downloads</a></div></div>"
[IO.File]::WriteAllText((Join-Path $Root "index.html"), (Page "My Tool Box — Downloads" "My Tool Box" "Everyday IT tools for Windows · latest version $latest" "" $body 0), $enc)

New-Item -ItemType File -Force (Join-Path $Root ".nojekyll") | Out-Null
Write-Host "Indexes written for $($versions.Count) version(s); latest $latest"
