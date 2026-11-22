// Extracts the tweak catalog from a WinScript checkout (https://github.com/flick9000/winscript)
// into data/catalog.json, which Deblowin embeds at build time.
//
// Usage: node tools/extract-catalog.mjs <path-to-winscript-checkout>
//
// WinScript is GPL-3.0; the scripts it contains keep that license (see LICENSE).

import fs from "node:fs";
import path from "node:path";

const root = process.argv[2];
if (!root) {
    console.error("Usage: node tools/extract-catalog.mjs <winscript-dir>");
    process.exit(1);
}
const src = path.join(root, "app", "src");
const read = (p) => fs.readFileSync(path.join(src, p), "utf8");

// ---------- helpers ----------
// Returns index just after the bracket that closes the one at `start`.
function matchBracket(text, start) {
    const open = text[start];
    const close = { "{": "}", "[": "]", "(": ")" }[open];
    let depth = 0;
    let quote = null;
    for (let i = start; i < text.length; i++) {
        const c = text[i];
        if (quote) {
            if (c === "\\") { i++; continue; }
            if (c === quote) quote = null;
            continue;
        }
        if (c === '"' || c === "'" || c === "`") { quote = c; continue; }
        if (c === open) depth++;
        else if (c === close) {
            depth--;
            if (depth === 0) return i + 1;
        }
    }
    throw new Error("Unbalanced bracket at " + start);
}

// t["key"] evaluates to a marker we resolve per language later.
const tProxy = new Proxy({}, { get: (_, k) => ({ $t: String(k) }) });
const evalExpr = (expr) => new Function("t", `return (${expr});`)(tProxy);

// Parses JSX-ish attributes of a component tag body.
function parseAttrs(body) {
    const attrs = {};
    let i = 0;
    while (i < body.length) {
        const m = /([A-Za-z_][\w-]*)\s*=\s*/y;
        m.lastIndex = i;
        const r = m.exec(body);
        if (!r) { i++; continue; }
        const name = r[1];
        let j = m.lastIndex;
        if (body[j] === "{") {
            const end = matchBracket(body, j);
            attrs[name] = evalExpr(body.slice(j + 1, end - 1));
            i = end;
        } else if (body[j] === '"') {
            const end = body.indexOf('"', j + 1);
            attrs[name] = body.slice(j + 1, end);
            i = end + 1;
        } else {
            i = j + 1;
        }
    }
    return attrs;
}

// ---------- scripts.ts ----------
const scriptsTs = read("assets/js/scripts.ts");
function extractObject(text, marker) {
    const at = text.indexOf(marker);
    if (at < 0) throw new Error("marker not found: " + marker);
    const start = text.indexOf("{", at + marker.length - 1);
    const end = matchBracket(text, start);
    return new Function(`return (${text.slice(start, end)});`)();
}
const scripts = extractObject(scriptsTs, "const scripts: Record<string, string[]> =");
const presets = extractObject(scriptsTs, "const presets");

// ---------- locales ----------
const locales = {};
for (const lang of ["en", "pt"]) {
    locales[lang] = JSON.parse(read(`i18n/locales/${lang}.json`));
}
function tr(v) {
    if (v == null || v === "") return undefined;
    if (typeof v === "string") return { en: v, pt: v };
    if (v.$t) {
        const en = locales.en[v.$t] ?? v.$t;
        return { en, pt: locales.pt[v.$t] ?? en };
    }
    return undefined;
}
// Description key convention: "<title key>.desc" when not given explicitly.
function trDesc(explicit, titleVal) {
    const d = tr(explicit);
    if (d) return d;
    if (titleVal && titleVal.$t && locales.en[titleVal.$t + ".desc"]) {
        return tr({ $t: titleVal.$t + ".desc" });
    }
    return undefined;
}

// ---------- MainPage.astro ----------
const page = read("components/MainPage.astro");
const SKIP_TABS = new Set(["install", "script", "localizations"]);
// Items that are not tweaks or that Deblowin deliberately leaves out.
const SKIP_IDS = new Set(["none", "installmas"]);
// Items handled by the script header in WinScript (MockupCode.astro) rather than scripts.ts.
const SPECIAL = {
    restorepoint: [
        "Write-Host '-- Creating a restore point' -ForegroundColor Magenta",
        'Enable-ComputerRestore -Drive "$env:SystemDrive\\"',
        'Checkpoint-Computer -Description "Deblowin" -RestorePointType "MODIFY_SETTINGS"',
    ],
};

const tabTitleKey = {
    tools: "nav.tools", debloat: "nav.debloat", privacy: "nav.privacy",
    telemetry: "nav.telemetry", gaming: "nav.gaming", performance: "nav.performance",
    miscellanous: "nav.miscellaneous",
};

const categories = [];
const missing = [];
let current = null;
const tagRe = /<(Tab|ScriptToggle|ScriptGroup|GroupToggle|DebloatGroup)\b/g;
let m;
while ((m = tagRe.exec(page))) {
    // Find end of the opening tag: first ">" at bracket depth 0.
    let i = m.index + m[0].length;
    while (i < page.length) {
        if (page[i] === "{") { i = matchBracket(page, i); continue; }
        if (page[i] === '"') { i = page.indexOf('"', i + 1) + 1; continue; }
        if (page[i] === ">") break;
        i++;
    }
    const body = page.slice(m.index + m[0].length, i).replace(/\/$/, "");
    const attrs = parseAttrs(body);
    tagRe.lastIndex = i;

    if (m[1] === "Tab") {
        current = SKIP_TABS.has(attrs.tab) ? null : {
            id: attrs.tab === "miscellanous" ? "misc" : attrs.tab,
            title: tr({ $t: tabTitleKey[attrs.tab] }),
            groups: [],
        };
        if (current) categories.push(current);
        continue;
    }
    if (!current) continue;

    const mkItem = (it, radioGroup) => {
        const id = it.inputId ?? it.appId;
        if (SKIP_IDS.has(id)) return null;
        const commands = scripts[id] ?? SPECIAL[id];
        if (!commands) { missing.push(id); return null; }
        const item = { id, title: tr(it.title), commands };
        const desc = trDesc(it.description ?? it.desc, it.title);
        if (desc) item.desc = desc;
        if (radioGroup) item.radioGroup = radioGroup;
        return item;
    };

    if (m[1] === "ScriptToggle") {
        const item = mkItem(attrs);
        if (item) current.groups.push({ id: item.id, title: item.title, desc: item.desc, items: [item], single: true });
    } else if (m[1] === "ScriptGroup" || m[1] === "GroupToggle") {
        const list = attrs.tools ?? attrs.scripts ?? [];
        const items = list
            .map((it) => mkItem(it, it.radio ? `${current.id}.${it.group}` : undefined))
            .filter(Boolean);
        if (!items.length) continue;
        current.groups.push({
            id: attrs.id ?? items[0].id,
            title: tr(attrs.title),
            desc: tr(attrs.description),
            exclusive: items.some((x) => x.radioGroup) || undefined,
            items,
        });
    } else if (m[1] === "DebloatGroup") {
        // Per-app removal: each app becomes one Remove-AppxPackage command.
        const items = attrs.apps.map((a) => ({
            id: `appx:${a.appId}`,
            title: tr(a.title),
            desc: tr(a.desc),
            commands: [
                `Write-Host '-- Removing ${a.title.replace(/'/g, "''")}' -ForegroundColor Magenta`,
                `Get-AppxPackage "${a.appId}" | Remove-AppxPackage`,
            ],
        }));
        current.groups.push({
            id: `debloat-${attrs.id}`,
            title: tr(attrs.title),
            desc: tr(attrs.desc),
            items,
        });
    }
}

// Expand "group-xxx" preset entries into their item ids.
const allGroups = categories.flatMap((c) => c.groups);
const expandPreset = (ids) =>
    ids.flatMap((id) => {
        if (id.startsWith("group-")) {
            const g = allGroups.find((x) => x.id === id.slice(6));
            return g ? g.items.map((x) => x.id) : [];
        }
        if (["thirdparty", "msapps", "extensions"].includes(id)) {
            const g = allGroups.find((x) => x.id === "debloat-" + id);
            return g ? g.items.map((x) => x.id) : [];
        }
        return [id];
    });
const knownIds = new Set(allGroups.flatMap((g) => g.items.map((x) => x.id)));
const outPresets = {};
for (const [k, ids] of Object.entries(presets)) {
    outPresets[k] = [...new Set(expandPreset(ids))].filter((id) => knownIds.has(id));
}

const catalog = {
    source: "https://github.com/flick9000/winscript",
    generated: new Date().toISOString(),
    categories,
    presets: outPresets,
};

import { fileURLToPath } from "node:url";
const outPath = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "data", "catalog.json");
fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(outPath, JSON.stringify(catalog, null, 1));

const count = allGroups.reduce((n, g) => n + g.items.length, 0);
console.log(`categories=${categories.length} groups=${allGroups.length} items=${count}`);
console.log("presets:", Object.fromEntries(Object.entries(outPresets).map(([k, v]) => [k, v.length])));
if (missing.length) console.log("no script for:", [...new Set(missing)].join(", "));
console.log("written", outPath);
