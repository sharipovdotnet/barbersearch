/**
 * Проверка английского каталога.
 *
 * 1. Каждый `t('русская строка')` должен иметь перевод в JSON.
 * 2. Один и тот же русский ключ не может переводиться по-разному в разных областях.
 * 3. Предупреждение: русские строки вне `t()` в UI-файлах (парсеры и словари-
 *    источники исключены явным списком).
 *
 * Запуск: `npm run check:i18n` (предупреждения не валят процесс) или
 * `node scripts/check-i18n.mjs --strict` (валят и предупреждения).
 */

import fs from 'node:fs'
import path from 'node:path'
import process from 'node:process'
import ts from 'typescript'

const root = process.cwd()
const srcRoot = path.join(root, 'src')
const messagesRoot = path.join(srcRoot, 'i18n', 'messages', 'en')
const strict = process.argv.includes('--strict')
const cyrillic = /[\u0400-\u04FF]/

const ALLOWLIST = [
  'src/i18n/',
  'src/lib/import-parse.ts',
  'src/lib/ics-map.ts',
  'src/lib/import-match.ts',
  'src/lib/barber-profile-labels.ts',
  'src/lib/barber-grade.ts',
  'src/lib/place-kind.ts',
  'src/lib/legal.ts',
  'src/lib/admin-audit.ts',
]

function walk(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name)

    if (entry.isDirectory()) {
      walk(full, out)
      continue
    }

    if (!/\.(ts|tsx)$/.test(entry.name)) continue
    if (/\.test\.(ts|tsx)$/.test(entry.name)) continue
    out.push(full)
  }

  return out
}

function rel(file) {
  return path.relative(root, file).replaceAll('\\', '/')
}

function isAllowed(file) {
  return ALLOWLIST.some((prefix) => file.startsWith(prefix))
}

const used = new Map()
const unwrapped = []

function isTranslationArgument(node) {
  let current = node

  while (current.parent) {
    const parent = current.parent

    if (
      ts.isCallExpression(parent) &&
      parent.arguments[0] === current &&
      ts.isIdentifier(parent.expression) &&
      parent.expression.text === 't'
    ) {
      return true
    }

    current = parent
  }

  return false
}

function isPropertyName(node) {
  const parent = node.parent

  return (
    parent !== undefined &&
    (ts.isPropertyAssignment(parent) || ts.isPropertyDeclaration(parent)) &&
    parent.name === node
  )
}

function isTypePosition(node) {
  const parent = node.parent

  return (
    parent !== undefined &&
    (ts.isLiteralTypeNode(parent) ||
      ts.isImportDeclaration(parent) ||
      ts.isExportDeclaration(parent))
  )
}

function noteUsed(value, file, line) {
  const list = used.get(value) ?? []
  list.push(`${file}:${line}`)
  used.set(value, list)
}

function noteUnwrapped(value, file, line, kind) {
  unwrapped.push({ file, line, kind, value: value.trim() })
}

for (const file of walk(srcRoot)) {
  const text = fs.readFileSync(file, 'utf8')
  const source = ts.createSourceFile(
    file,
    text,
    ts.ScriptTarget.Latest,
    true,
    file.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS
  )
  const relative = rel(file)

  function add(kind, node, value) {
    if (!cyrillic.test(value)) return

    const { line } = source.getLineAndCharacterOfPosition(node.getStart(source))

    if (isTranslationArgument(node)) {
      noteUsed(value, relative, line + 1)
      return
    }

    if (isPropertyName(node) || isTypePosition(node)) return
    if (isAllowed(relative)) return

    noteUnwrapped(value, relative, line + 1, kind)
  }

  function visit(node) {
    if (ts.isJsxText(node)) {
      add('jsx', node, node.getText(source))
    } else if (ts.isJsxAttribute(node) && node.initializer) {
      if (ts.isStringLiteral(node.initializer)) {
        add('attr', node.initializer, node.initializer.text)
      }
    } else if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) {
      add('string', node, node.text)
    } else if (ts.isTemplateExpression(node)) {
      const raw = node.getText(source)
      if (cyrillic.test(raw)) {
        const { line } = source.getLineAndCharacterOfPosition(
          node.getStart(source)
        )
        noteUnwrapped(raw, relative, line + 1, 'template')
      }
    }

    ts.forEachChild(node, visit)
  }

  visit(source)
}

const catalogs = new Map()
const conflicts = []

for (const entry of fs.readdirSync(messagesRoot)) {
  if (!entry.endsWith('.json')) continue

  const file = path.join(messagesRoot, entry)
  const data = JSON.parse(fs.readFileSync(file, 'utf8'))

  for (const [key, value] of Object.entries(data)) {
    if (typeof value !== 'string') {
      conflicts.push(`${entry}: ключ «${key}» не строка`)
      continue
    }

    const previous = catalogs.get(key)

    if (previous && previous.value !== value) {
      conflicts.push(
        `«${key}»: ${previous.file} → «${previous.value}», ${entry} → «${value}»`
      )
      continue
    }

    if (!previous) {
      catalogs.set(key, { value, file: entry })
    }
  }
}

const missing = [...used.entries()]
  .filter(([source]) => !catalogs.has(source))
  .sort((a, b) => a[0].localeCompare(b[0], 'ru'))

const unused = [...catalogs.keys()]
  .filter((key) => !used.has(key))
  .sort((a, b) => a.localeCompare(b, 'ru'))

// Текст в JSX и статических атрибутах всегда виден пользователю. Литералы внутри
// констант-словарей и парсеров — нет: их переводят через t(CONST) на месте вывода.
const visibleUnwrapped = unwrapped.filter(
  (item) => item.kind === 'jsx' || item.kind === 'attr'
)

console.log(`t() строк: ${used.size}`)
console.log(`переводов: ${catalogs.size}`)
console.log(`нет перевода: ${missing.length}`)
console.log(`не используется: ${unused.length}`)
console.log(`конфликтов: ${conflicts.length}`)
console.log(
  `русских строк вне t(): ${unwrapped.length} (видимых в JSX: ${visibleUnwrapped.length})`
)

if (missing.length > 0) {
  console.log('\nНЕТ ПЕРЕВОДА:')
  for (const [source, places] of missing) {
    console.log(`  «${source}» — ${places[0]}`)
  }
}

if (conflicts.length > 0) {
  console.log('\nКОНФЛИКТЫ:')
  for (const conflict of conflicts) console.log(`  ${conflict}`)
}

if (unwrapped.length > 0) {
  console.log('\nВНЕ t() (первые 60):')
  for (const item of unwrapped.slice(0, 60)) {
    console.log(`  ${item.file}:${item.line} [${item.kind}] ${item.value.slice(0, 120)}`)
  }
}

const failed =
  missing.length > 0 ||
  conflicts.length > 0 ||
  (strict && visibleUnwrapped.length > 0)

process.exit(failed ? 1 : 0)
