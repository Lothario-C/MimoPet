#!/usr/bin/env node
/*
 * MiMo Desktop 思考强度补丁器
 *
 *   node patch.mjs gen      从本地 provider 的 /v1/models 生成 efforts.json
 *   node patch.mjs apply    打补丁（幂等，可安全重放）
 *   node patch.mjs verify   检查补丁是否在位
 *   node patch.mjs undo     还原为官方原始文件
 *
 * 环境变量：
 *   MIMO_RELEASES  releases 目录（默认 %APPDATA%\Xiaomi MiMo\runtime-res\releases）
 *   MIMO_CONFIG    mimocode.jsonc 路径
 *   MIMO_PROVIDER  生成档位表使用的 provider 段（默认 ofm）
 *
 * 定位方式：只依赖稳定的代码特征，不硬编码混淆函数名与字节偏移。
 * 重放方式：以磁盘现状为基底，先剥离我方注入再重新注入。因此客户端
 *           热更新带来的官方新代码不会被退回旧版。
 */
import fs from 'node:fs'
import path from 'node:path'
import os from 'node:os'

const APPDATA = process.env.APPDATA || path.join(os.homedir(), 'AppData', 'Roaming')
const REL = process.env.MIMO_RELEASES || path.join(APPDATA, 'Xiaomi MiMo', 'runtime-res', 'releases')
const CONFIG = process.env.MIMO_CONFIG || path.join(os.homedir(), '.config', 'mimocode', 'mimocode.jsonc')
const HERE = path.dirname(fs.realpathSync(process.argv[1]))
const TABLE = path.join(HERE, 'efforts.json')
const PRISTINE = path.join(HERE, 'pristine')
const PROVIDER = process.env.MIMO_PROVIDER || 'ofm'
const MARK = '/*__MIMO_EFFORTS__*/'

const read = f => fs.readFileSync(f, 'utf8')
const write = (f, s) => fs.writeFileSync(f, s)

/* ---------------- 文件发现 ---------------- */
function walk(dir, acc = []) {
  if (!fs.existsSync(dir)) return acc
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name === '.mimo-effort-backup') continue
    const p = path.join(dir, e.name)
    if (e.isDirectory()) walk(p, acc)
    else if (/\.(mjs|cjs|js)$/.test(e.name) && fs.statSync(p).size > 150_000) acc.push(p)
  }
  return acc
}
function findBy(needle, label) {
  const hits = walk(REL).filter(f => read(f).includes(needle))
  if (!hits.length) throw new Error(`定位失败：找不到含「${label}」的文件（MiMo 结构可能已变，请更新签名）`)
  return hits.sort((a, b) => fs.statSync(b).size - fs.statSync(a).size)[0]
}

/* ---------------- 备份基准（仅服务 undo） ----------------
   pristine 保存「最近一次看到的、不含我方标记的官方内容」。
   文件已打过补丁时保留更早的基准；客户端热更新送来干净新文件时刷新基准。 */
function snapshotOfficial(file, cur) {
  if (cur.includes(MARK)) return
  fs.mkdirSync(PRISTINE, { recursive: true })
  write(path.join(PRISTINE, path.basename(file)), cur)
}

/* ---------------- 剥离：还原回官方形态 ----------------
   必须用正则字面量。写成 new RegExp(`${MARK}...`) 时，MARK 里的 `/*`
   会被当作量词，导致静默失配、补丁层层叠加。 */
const RX_TABLE = /\/\*__MIMO_EFFORTS__\*\/globalThis\.__MIMO_EFFORTS__=\{[\s\S]*?\};/g
const RX_LOOKUP = /(effortVariantsFor\(n\)\{)const __T=\(globalThis\.__MIMO_EFFORTS__\|\|\{\}\)\[[^\]]+\];if\(__T&&__T\.length>=2\)return __T;/g
const RX_ENGINE = /\n {2}const __E = globalThis\.__MIMO_EFFORTS__ \|\| \{\};\n(?: {2}.*\n){0,8}? {2}\}\n(?= {2}if \(!model\.capabilities\.reasoning\))/
const RX_RELAX = /function (\w+)\(t\)\{if\(typeof t=="string"\)return (\w+)\[t\]\?\?t\}/g

function strip(src) {
  return src
    .replace(RX_TABLE, '')
    .replace(RX_LOOKUP, '$1')
    .replace(RX_ENGINE, '\n')
}

/* think 白名单被放宽成 `M[t]??t` 后，数组名 L 无法从放宽形式反推，
   只能从 pristine 基准里取同名片段。取不到就保留放宽形式（不影响功能）。 */
function restoreRelax(src, file) {
  if (!RX_RELAX.test(src)) return src
  RX_RELAX.lastIndex = 0
  const p = path.join(PRISTINE, path.basename(file))
  if (!fs.existsSync(p)) return src
  const orig = read(p)
  return src.replace(RX_RELAX, (m0, fn, map) => {
    const re = new RegExp(`function ${fn}\\(t\\)\\{if\\(typeof t=="string"\\)return (\\w+)\\.includes\\(t\\)\\?t:${map}\\[t\\]\\}`)
    const x = re.exec(orig)
    return x ? x[0] : m0
  })
}

/* ---------------- 注入 ---------------- */
function withTable(src, inject) {
  if (src.startsWith('#!')) {
    const nl = src.indexOf('\n')
    return src.slice(0, nl + 1) + inject + src.slice(nl + 1)
  }
  return inject + src
}

const tableJson = () => {
  if (!fs.existsSync(TABLE)) throw new Error(`缺少 ${TABLE}，请先运行: node patch.mjs gen`)
  return read(TABLE).trim()
}

function sync(file, target, label) {
  const rel = path.relative(REL, file)
  const cur = read(file)
  if (cur === target) return `· ${label} 已是最新 ${rel}`
  snapshotOfficial(file, cur)
  write(file, target)
  return `✔ ${label} 已更新 ${rel}`
}

/* ---------------- gen ---------------- */
async function gen() {
  const cfg = read(CONFIG)
  const base = new RegExp(`"${PROVIDER}"[\\s\\S]*?"baseURL"\\s*:\\s*"([^"]+)"`).exec(cfg)?.[1]
  const key = new RegExp(`"${PROVIDER}"[\\s\\S]{0,9000}?"apiKey"\\s*:\\s*"([^"]+)"`).exec(cfg)?.[1]
  if (!base) throw new Error(`配置里没有 ${PROVIDER} 的 baseURL`)
  const url = base.replace(/\/+$/, '') + '/models'
  const res = await fetch(url, { headers: key ? { Authorization: `Bearer ${key}` } : {} })
  if (!res.ok) throw new Error(`GET ${url} → HTTP ${res.status}（网关是否在运行？）`)
  const models = (await res.json()).data || []

  const providers = [...cfg.matchAll(/"(\w+)"\s*:\s*\{\s*"npm"/g)].map(m => m[1])
  const bare = {}
  for (const m of models) {
    let lv = Array.isArray(m.reasoning?.efforts) && m.reasoning.efforts.length
      ? m.reasoning.efforts.map(x => (typeof x === 'string' ? x : x.id))
      : Array.isArray(m.x_ofm_efforts) ? m.x_ofm_efforts : []
    lv = [...new Set(lv.map(x => String(x).trim()).filter(Boolean))]
    if (lv.length >= 2) bare[m.id] = lv
  }
  const table = {}
  for (const [id, lv] of Object.entries(bare)) {
    table[id] = lv                                        // 裸 id：引擎侧
    for (const p of providers) table[`${p}/${id}`] = lv   // provider/id：输入框侧
  }
  write(TABLE, JSON.stringify(table))
  console.log(`efforts.json：${Object.keys(bare).length} 个模型 / ${Object.keys(table).length} 个 key`)
  for (const probe of ['workbuddy/deepseek-v4.1-flash', `${PROVIDER}/workbuddy/deepseek-v4.1-flash`]) {
    if (table[probe]) console.log(`  ${probe} => ${table[probe].join('/')}`)
  }
}

/* ---------------- apply ---------------- */
function apply() {
  const tbl = tableJson()
  const inject = `${MARK}globalThis.__MIMO_EFFORTS__=${tbl};`
  const lines = []
  let changed = 0
  const run = msg => { if (msg.startsWith('✔')) changed++; lines.push(msg) }

  /* --- 1. 渲染层：档位菜单入口 ---
     renderer / main / engine 是三个独立 JS realm，globalThis 不共享，
     所以档位表要在每个需要的文件里各注入一份。
     在 effortVariantsFor 本体最前面查表，绕过官方 tags 闸门：官方通道
     只认 low/medium/high/xhigh/max，网关声明的 light、extra_high、
     disabled 会被它过滤掉。 */
  {
    const file = findBy('effortVariantsFor(n){', '渲染层 effortVariantsFor')
    const clean = restoreRelax(strip(read(file)), file)
    const g = /(effortVariantsFor\(n\)\{)const (\w+)=(\w+)\?\?(\w+)\(\)\.model,/.exec(clean)
    if (!g) throw new Error('渲染层：effortVariantsFor 结构变化，请更新签名')
    const target = withTable(
      clean.replace(g[0],
        `${g[1]}const __T=(globalThis.__MIMO_EFFORTS__||{})[${g[3]}??${g[4]}().model];if(__T&&__T.length>=2)return __T;const ${g[2]}=${g[3]}??${g[4]}().model,`),
      inject)
    run(sync(file, target, '渲染层'))
  }

  /* --- 2. 引擎：variants() 按表生成 providerOptions ---
     档位名原样发出，不要改写：网关 effort.js 负责映射线上字段
     （disabled 走 effortOffPatch → reasoning_effort:"none"）。
     若在客户端提前改成 none，网关查不到该 rung，会退回默认档。 */
  {
    const file = findBy('function variants(model) {', '引擎 variants()')
    const clean = restoreRelax(strip(read(file)), file)
    const eHead = 'function variants(model) {\n'
    if (!clean.includes(eHead)) throw new Error('引擎：variants() 头部结构变化')
    const target = withTable(clean.replace(eHead, eHead +
      '  const __E = globalThis.__MIMO_EFFORTS__ || {};\n' +
      '  const __lv = __E[model.id] || __E[`${model.providerID}/${model.id}`];\n' +
      '  if (Array.isArray(__lv) && __lv.length >= 2) {\n' +
      '    const __out = {};\n' +
      '    for (const __l of __lv) __out[__l] = { reasoningEffort: __l };\n' +
      '    return __out;\n' +
      '  }\n'), inject)
    run(sync(file, target, '引擎'))
  }

  /* --- 3. 放宽 think 值白名单 ---
     否则 light / extra_high 等档位选中后会被过滤成 undefined，菜单等于无效。 */
  {
    const re = /function (\w+)\(t\)\{if\(typeof t=="string"\)return (\w+)\.includes\(t\)\?t:(\w+)\[t\]\}/
    let found = 0
    for (const f of walk(REL)) {
      const src = read(f)
      const x = re.exec(src)
      if (!x) continue
      found++
      const target = src.replace(re, `function ${x[1]}(t){if(typeof t=="string")return ${x[3]}[t]??t}`)
      run(sync(f, target, `think 校验（${x[1]}）`))
    }
    if (!found) lines.push('· 未发现待放宽的 think 校验（可能已放宽）')
  }

  console.log(lines.join('\n'))
  console.log(`\n本次写入 ${changed} 个文件。完全退出 MiMo Desktop（含托盘）后重启生效。`)
}

/* ---------------- verify ---------------- */
function verify() {
  const rows = []
  try {
    const t = read(findBy('effortVariantsFor(n){', '渲染层'))
    rows.push(['渲染层 档位表赋值', t.includes(`${MARK}globalThis.__MIMO_EFFORTS__=`)])
    rows.push(['渲染层 查表前置', /effortVariantsFor\(n\)\{const __T=\(globalThis\.__MIMO_EFFORTS__/.test(t)])
    rows.push(['渲染层 无重复注入', (t.match(/globalThis\.__MIMO_EFFORTS__=/g) || []).length === 1])
  } catch { rows.push(['渲染层 定位', false]) }
  try {
    const t = read(findBy('function variants(model) {', '引擎'))
    rows.push(['引擎 档位表赋值', t.includes(`${MARK}globalThis.__MIMO_EFFORTS__=`)])
    rows.push(['引擎 variants() 查表', t.includes('const __lv = __E[model.id]')])
    rows.push(['引擎 无重复注入', (t.match(/globalThis\.__MIMO_EFFORTS__=/g) || []).length === 1])
  } catch { rows.push(['引擎 定位', false]) }
  try {
    const t = read(findBy('[t]??t}', 'think 校验'))
    rows.push(['think 校验已放宽', /function \w+\(t\)\{if\(typeof t=="string"\)return \w+\[t\]\?\?t\}/.test(t)])
  } catch { rows.push(['think 校验已放宽', false]) }

  let ok = true
  for (const [n, v] of rows) { console.log(`${v ? '✔' : '✘'} ${n}`); if (!v) ok = false }
  console.log(fs.existsSync(TABLE) ? `· efforts.json：${Object.keys(JSON.parse(read(TABLE))).length} 个 key` : '· 尚无 efforts.json')
  console.log(fs.existsSync(PRISTINE) ? `· pristine 备份：${fs.readdirSync(PRISTINE).length} 个文件` : '· 尚无 pristine 备份')
  process.exitCode = ok ? 0 : 1
}

/* ---------------- undo ---------------- */
function undo() {
  if (!fs.existsSync(PRISTINE)) throw new Error('没有 pristine 备份，无需还原')
  let n = 0
  for (const name of fs.readdirSync(PRISTINE)) {
    const target = walk(REL).find(f => path.basename(f) === name)
    if (!target) { console.log(`· 跳过 ${name}（当前安装无同名文件）`); continue }
    fs.copyFileSync(path.join(PRISTINE, name), target)
    console.log('↩ 还原', path.relative(REL, target))
    n++
  }
  console.log(n ? `已还原 ${n} 个文件（客户端更新过的文件可能回到旧版，重启会自动修正）` : '没有可还原的文件')
}

/* ---------------- CLI ---------------- */
const cmd = process.argv[2]
try {
  if (cmd === 'gen') await gen()
  else if (cmd === 'apply') apply()
  else if (cmd === 'verify') verify()
  else if (cmd === 'undo') undo()
  else console.log('用法: node patch.mjs gen | apply | verify | undo')
} catch (err) {
  console.error('错误：', err.message)
  process.exitCode = 1
}
