#!/usr/bin/env node
// Allowlist-aware npm audit gate (no dependencies).
//
// Usage: node .github/scripts/npm-audit-gate.mjs [frontendDir=src/frontend]
//          [--audit-json <file>] [--prod-audit-json <file>]
//          [--allowlist <file>] [--today YYYY-MM-DD]
//
// Without the two --*-json options the gate runs `npm audit --json --include=dev` and
// `npm audit --json --omit=dev` in frontendDir, without the caller's NODE_ENV or npm omit/include
// settings. frontendDir/package.json is always read.
//
// Exit 0: every rule passes. Exit 1: at least one rule is broken.
// Exit 2: the input could not be checked (never a pass).
import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';

const EXIT_PASS = 0;
const EXIT_FAIL = 1;
const EXIT_UNUSABLE = 2;

const SEVERITIES = ['info', 'low', 'moderate', 'high', 'critical'];
const ALLOWLISTABLE_SEVERITIES = ['low', 'moderate', 'high'];
const GHSA_PATTERN = /GHSA-[0-9a-z]{4}-[0-9a-z]{4}-[0-9a-z]{4}/i;
const USAGE =
  'Usage: node .github/scripts/npm-audit-gate.mjs [frontendDir=src/frontend] [--audit-json <file>] ' +
  '[--prod-audit-json <file>] [--allowlist <file>] [--today YYYY-MM-DD]';

class UnusableInputError extends Error {}

// Canonical GHSA form: upper-case prefix, lower-case segments.
function normalizeGhsa(id) {
  return 'GHSA-' + id.slice(5).toLowerCase();
}

function isPlainObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isNonEmptyString(value) {
  return typeof value === 'string' && value.trim().length > 0;
}

function isIsoDate(value) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return false;
  }
  const parsed = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}

function parseArgs(argv) {
  const options = { frontendDir: null, auditJson: null, prodAuditJson: null, allowlist: null, today: null };
  const valueFlags = {
    '--audit-json': 'auditJson',
    '--prod-audit-json': 'prodAuditJson',
    '--allowlist': 'allowlist',
    '--today': 'today',
  };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--help' || arg === '-h') {
      options.help = true;
    } else if (arg in valueFlags) {
      const value = argv[i + 1];
      if (value === undefined || value.startsWith('--')) {
        throw new UnusableInputError(`${arg} needs a value. ${USAGE}`);
      }
      options[valueFlags[arg]] = value;
      i += 1;
    } else if (arg.startsWith('--')) {
      throw new UnusableInputError(`unknown option ${arg}. ${USAGE}`);
    } else if (options.frontendDir === null) {
      options.frontendDir = arg;
    } else {
      throw new UnusableInputError(`unexpected argument ${arg}. ${USAGE}`);
    }
  }
  options.frontendDir ??= 'src/frontend';
  options.allowlist ??= path.join(options.frontendDir, 'audit-allowlist.json');
  if (options.today !== null && !isIsoDate(options.today)) {
    throw new UnusableInputError(`--today must be a real date in YYYY-MM-DD form, got "${options.today}"`);
  }
  options.today ??= new Date().toISOString().slice(0, 10);
  return options;
}

function parseJson(text, label) {
  try {
    return JSON.parse(text);
  } catch (error) {
    throw new UnusableInputError(`${label} is not valid JSON (${error.message})`);
  }
}

function readJsonFile(file, label) {
  let text;
  try {
    text = readFileSync(file, 'utf8');
  } catch (error) {
    throw new UnusableInputError(`${label} cannot be read at ${file} (${error.code ?? error.message})`);
  }
  return parseJson(text, `${label} (${file})`);
}

// What each audit covers is stated on the command line. With omit=dev in effect a bare
// `npm audit` returns the production report, which is well formed and counts no dev advisory.
const FULL_AUDIT_COMMAND = 'npm audit --json --include=dev';
const PROD_AUDIT_COMMAND = 'npm audit --json --omit=dev';

// npm also takes the audit scope from these variables (NODE_ENV=production means omit=dev, and
// include wins over omit), so the caller's values are not passed on.
const AUDIT_SCOPE_VARIABLE = /^(node_env|npm_config_(omit|include|production|only|also|dev))$/i;

function auditEnvironment() {
  return Object.fromEntries(Object.entries(process.env).filter(([name]) => !AUDIT_SCOPE_VARIABLE.test(name)));
}

// npm audit exits non-zero when it finds something, so only its JSON output is trusted.
function runNpmAudit(frontendDir, omitDev) {
  const command = omitDev ? PROD_AUDIT_COMMAND : FULL_AUDIT_COMMAND;
  const result = spawnSync(command, {
    cwd: frontendDir,
    shell: true,
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    env: auditEnvironment(),
  });
  if (result.error) {
    throw new UnusableInputError(`"${command}" could not be started in ${frontendDir} (${result.error.message})`);
  }
  if (!isNonEmptyString(result.stdout)) {
    const detail = (result.stderr ?? '').trim().split('\n').slice(-3).join(' | ');
    throw new UnusableInputError(`"${command}" produced no output in ${frontendDir} (${detail})`);
  }
  return parseJson(result.stdout, `output of "${command}"`);
}

function validateReport(report, label) {
  if (!isPlainObject(report)) {
    throw new UnusableInputError(`${label} is not an npm audit report object`);
  }
  if (report.auditReportVersion !== 2) {
    const hint = isPlainObject(report.error) ? ` (npm error: ${report.error.code ?? report.error.summary ?? 'unknown'})` : '';
    throw new UnusableInputError(`${label} is not an npm audit report with auditReportVersion 2${hint}`);
  }
  const counts = report.metadata?.vulnerabilities;
  if (!isPlainObject(report.vulnerabilities) || !isPlainObject(counts)) {
    throw new UnusableInputError(`${label} has no vulnerabilities/metadata section`);
  }
  for (const key of [...SEVERITIES, 'total']) {
    if (!Number.isInteger(counts[key]) || counts[key] < 0) {
      throw new UnusableInputError(`${label} has no usable metadata.vulnerabilities.${key} count`);
    }
  }
  const dependencies = report.metadata.dependencies;
  for (const key of ['dev', 'total']) {
    if (!isPlainObject(dependencies) || !Number.isInteger(dependencies[key]) || dependencies[key] < 0) {
      throw new UnusableInputError(`${label} has no usable metadata.dependencies.${key} count`);
    }
  }
  const entries = Object.entries(report.vulnerabilities);
  if (entries.length !== counts.total) {
    throw new UnusableInputError(
      `${label} is inconsistent: ${entries.length} vulnerable packages listed but metadata reports ${counts.total}`,
    );
  }
  for (const [name, entry] of entries) {
    if (!isPlainObject(entry) || !SEVERITIES.includes(entry.severity) || !Array.isArray(entry.via) || entry.via.length === 0) {
      throw new UnusableInputError(`${label} has a malformed entry for package ${name}`);
    }
    if (entry.fixAvailable !== true && entry.fixAvailable !== false && !isPlainObject(entry.fixAvailable)) {
      throw new UnusableInputError(`${label} has no usable fixAvailable value for package ${name}`);
    }
  }
}

// A full report that leaves out devDependencies cannot be judged. This catches a tree that was
// resolved without them; a report filtered by omit=dev keeps its dependency counts, which is why
// the audit commands above name their scope instead of relying on this check.
function assertDevDependenciesCovered(fullReport, prodReport, manifest, manifestFile) {
  if (!isPlainObject(manifest)) {
    throw new UnusableInputError(`${manifestFile} is not a package.json object`);
  }
  const declared = isPlainObject(manifest.devDependencies) ? Object.keys(manifest.devDependencies).length : 0;
  const full = fullReport.metadata.dependencies;
  const prod = prodReport.metadata.dependencies;
  if (declared > 0 && full.dev === 0) {
    throw new UnusableInputError(
      `${manifestFile} declares ${declared} devDependencies but the full audit report counts no dev dependency, so devDependencies were not audited`,
    );
  }
  if (full.total < prod.total) {
    throw new UnusableInputError(
      `the full audit report covers fewer dependencies (${full.total}) than the production audit report (${prod.total})`,
    );
  }
}

function validateAllowlist(allowlist, file) {
  if (!isPlainObject(allowlist)) {
    throw new UnusableInputError(`allowlist ${file} is not a JSON object`);
  }
  if (!isIsoDate(allowlist.reviewBy)) {
    throw new UnusableInputError(`allowlist ${file} needs reviewBy as a real YYYY-MM-DD date`);
  }
  if (!Array.isArray(allowlist.exceptions)) {
    throw new UnusableInputError(`allowlist ${file} needs an exceptions array`);
  }
  const seen = new Set();
  allowlist.exceptions.forEach((entry, index) => {
    const where = `allowlist ${file} exceptions[${index}]`;
    if (!isPlainObject(entry)) {
      throw new UnusableInputError(`${where} is not an object`);
    }
    if (typeof entry.id !== 'string' || !new RegExp(`^${GHSA_PATTERN.source}$`, 'i').test(entry.id)) {
      throw new UnusableInputError(`${where} needs id as a GHSA identifier`);
    }
    for (const field of ['package', 'via', 'reason', 'exitCondition']) {
      if (!isNonEmptyString(entry[field])) {
        throw new UnusableInputError(`${where} (${entry.id}) needs a non-empty ${field}`);
      }
    }
    if (entry.scope !== 'dev') {
      throw new UnusableInputError(`${where} (${entry.id}) must have scope "dev"; production advisories cannot be allowlisted`);
    }
    if (!ALLOWLISTABLE_SEVERITIES.includes(entry.severity)) {
      throw new UnusableInputError(
        `${where} (${entry.id}) needs severity ${ALLOWLISTABLE_SEVERITIES.join('|')}; critical advisories cannot be allowlisted`,
      );
    }
    if (!isIsoDate(entry.added)) {
      throw new UnusableInputError(`${where} (${entry.id}) needs added as a real YYYY-MM-DD date`);
    }
    const key = normalizeGhsa(entry.id);
    if (seen.has(key)) {
      throw new UnusableInputError(`${where} repeats ${entry.id}`);
    }
    seen.add(key);
  });
}

function advisoryKey(via) {
  const match = typeof via.url === 'string' ? via.url.match(GHSA_PATTERN) : null;
  if (match) {
    return normalizeGhsa(match[0]);
  }
  // No GHSA id: keep it distinct so it can never match an allowlist entry.
  return `unidentified:${via.url ?? via.source ?? via.title ?? 'unknown'}`;
}

function maxSeverity(a, b) {
  return SEVERITIES.indexOf(a) >= SEVERITIES.indexOf(b) ? a : b;
}

// One record per advisory and package that carries it directly (advisory object in `via`).
// An advisory can cover several packages. Each pair is judged on its own, so one reviewed package
// never covers another and the result does not depend on the order of the report.
function collectAdvisories(report, label) {
  const records = new Map();
  for (const [packageName, entry] of Object.entries(report.vulnerabilities)) {
    for (const via of entry.via) {
      if (typeof via === 'string') {
        continue;
      }
      if (!isPlainObject(via)) {
        throw new UnusableInputError(`${label} has a malformed advisory under package ${packageName}`);
      }
      const severity = SEVERITIES.includes(via.severity) ? via.severity : entry.severity;
      const key = advisoryKey(via);
      const advisedName = typeof via.name === 'string' ? via.name : packageName;
      const pair = JSON.stringify([key, packageName, advisedName]);
      const known = records.get(pair);
      if (known) {
        known.severity = maxSeverity(known.severity, severity);
      } else {
        records.set(pair, {
          key,
          url: via.url ?? '(no url)',
          title: via.title ?? '(no title)',
          packageName,
          advisedName,
          where: advisedName === packageName ? packageName : `${packageName} (advisory for ${advisedName})`,
          severity,
          fixAvailable: entry.fixAvailable,
        });
      }
    }
  }
  return [...records.values()];
}

// Every vulnerable package must trace back to at least one advisory, or the report cannot be judged.
function assertAttributable(report, label) {
  const entries = report.vulnerabilities;
  const reachesAdvisory = (name, visiting) => {
    const entry = entries[name];
    if (!entry || visiting.has(name)) {
      return false;
    }
    visiting.add(name);
    return entry.via.some((via) => (typeof via === 'string' ? reachesAdvisory(via, visiting) : true));
  };
  for (const name of Object.keys(entries)) {
    if (!reachesAdvisory(name, new Set())) {
      throw new UnusableInputError(`${label} lists package ${name} without a traceable advisory`);
    }
  }
}

function hasNonMajorFix(fixAvailable) {
  return fixAvailable === true || (isPlainObject(fixAvailable) && fixAvailable.isSemVerMajor !== true);
}

function describeFix(fixAvailable) {
  if (fixAvailable === true) {
    return 'npm audit fix';
  }
  return `${fixAvailable.name ?? 'a dependency'}@${fixAvailable.version ?? '?'}`;
}

function evaluate({ fullReport, prodReport, allowlist, today }) {
  const failures = [];
  const accepted = [];
  // Below high and not allowlisted: reported, never accepted. Nobody reviewed these.
  const notGated = { noFix: [], majorOnly: [] };
  const exceptions = new Map(allowlist.exceptions.map((entry) => [normalizeGhsa(entry.id), entry]));
  const advisories = collectAdvisories(fullReport, 'full audit report');

  // (a) Nothing may reach production dependencies.
  const prodCounts = prodReport.metadata.vulnerabilities;
  if (prodCounts.total > 0) {
    const packages = Object.entries(prodReport.vulnerabilities)
      .map(([name, entry]) => `${name} (${entry.severity})`)
      .join(', ');
    failures.push(`[production] npm audit --omit=dev reports ${prodCounts.total} vulnerable package(s): ${packages}`);
  }

  // (b) Critical is never allowlistable.
  const criticalAdvisories = advisories.filter((advisory) => advisory.severity === 'critical');
  for (const advisory of criticalAdvisories) {
    failures.push(
      `[critical] ${advisory.key} in ${advisory.where} is critical and cannot be allowlisted: ${advisory.title} (${advisory.url})`,
    );
  }
  if (fullReport.metadata.vulnerabilities.critical > 0 && criticalAdvisories.length === 0) {
    failures.push(`[critical] the report counts ${fullReport.metadata.vulnerabilities.critical} critical package(s)`);
  }

  for (const advisory of advisories) {
    if (advisory.severity === 'critical') {
      continue;
    }
    // An entry covers its advisory on its own package only: the id alone is not enough.
    const entry = exceptions.get(advisory.key);
    const exception =
      entry && entry.package === advisory.packageName && entry.package === advisory.advisedName ? entry : null;
    if (!exception) {
      if (advisory.severity === 'high') {
        // (c) Every high advisory needs an allowlist entry for the package that carries it.
        const scope = entry ? ` (the allowlist entry for this id covers ${entry.package} only)` : '';
        failures.push(
          `[unlisted-high] ${advisory.key} in ${advisory.where} is high and not allowlisted${scope}: ${advisory.title} (${advisory.url})`,
        );
      } else if (hasNonMajorFix(advisory.fixAvailable)) {
        // (c) Below high, an advisory is tolerated only while no fix exists within the current majors.
        failures.push(
          `[fixable] ${advisory.key} in ${advisory.where} (${advisory.severity}) has a fix that needs no semver-major bump ` +
            `(${describeFix(advisory.fixAvailable)}): apply it. ${advisory.title} (${advisory.url})`,
        );
      } else if (advisory.fixAvailable === false) {
        notGated.noFix.push(`${advisory.key} in ${advisory.where} (${advisory.severity}): ${advisory.title}`);
      } else {
        notGated.majorOnly.push(
          `${advisory.key} in ${advisory.where} (${advisory.severity}): ${advisory.title} - fix: ${describeFix(advisory.fixAvailable)}`,
        );
      }
      continue;
    }
    // (d) An exception ends the moment a fix exists that does not need a semver-major bump.
    if (hasNonMajorFix(advisory.fixAvailable)) {
      failures.push(
        `[fix-available] ${advisory.key} can now be fixed without a semver-major bump ` +
          `(${advisory.packageName} via ${describeFix(advisory.fixAvailable)}): apply the fix and remove the allowlist entry`,
      );
      continue;
    }
    accepted.push({ exception, advisory });
  }

  // (d) Stale entries must be removed, but only after a report that really covered devDependencies.
  // An id that is still reported, for another package, is already an [unlisted-high] above.
  const reportedIds = new Set(advisories.map((advisory) => advisory.key));
  for (const [key, exception] of exceptions) {
    if (!reportedIds.has(key)) {
      failures.push(
        `[stale] ${exception.id} (${exception.package}) is allowlisted but no longer reported. ` +
          'First confirm that this audit covered devDependencies (the "Full audit" line above names its source and its ' +
          'dev dependency count; a report made with NODE_ENV=production or omit=dev lists none of their advisories); ' +
          'only then remove the entry.',
      );
    }
  }

  // (e) Exceptions are time-boxed.
  if (today > allowlist.reviewBy) {
    failures.push(`[expired] the allowlist review date ${allowlist.reviewBy} has passed (today ${today}): re-review every exception`);
  }

  return { failures, accepted, notGated, advisoryCount: reportedIds.size };
}

function printResult({ options, fullReport, prodReport, allowlist, result }) {
  const full = fullReport.metadata.vulnerabilities;
  const tree = fullReport.metadata.dependencies;
  const prodSource = options.prodAuditJson ? `file ${options.prodAuditJson}` : PROD_AUDIT_COMMAND;
  const fullSource = options.auditJson ? `file ${options.auditJson}` : FULL_AUDIT_COMMAND;
  console.log(`npm audit gate - ${options.frontendDir} (today ${options.today}, review by ${allowlist.reviewBy})`);
  console.log(`Production audit (${prodSource}): ${prodReport.metadata.vulnerabilities.total} vulnerable package(s)`);
  console.log(
    `Full audit (${fullSource}): ${full.total} vulnerable package(s) (${full.critical} critical, ${full.high} high, ` +
      `${full.moderate} moderate, ${full.low} low) from ${result.advisoryCount} advisory(ies); ` +
      `the tree has ${tree.dev} dev dependencies of ${tree.total}`,
  );
  console.log('');
  console.log(`Accepted exceptions (${result.accepted.length}):`);
  for (const { exception, advisory } of result.accepted) {
    console.log(`  - ${exception.id}  ${exception.package}  ${advisory.severity}  scope ${exception.scope}  added ${exception.added}`);
    console.log(`      title:  ${advisory.title}`);
    console.log(`      path:   ${exception.via}`);
    console.log(`      reason: ${exception.reason}`);
    console.log(`      exit:   ${exception.exitCondition}`);
  }
  // Separate blocks: check-deps.sh labels its evidence by these headings.
  const notGatedBlocks = [
    ['Not gated (no fix available)', result.notGated.noFix],
    ['Not gated (only a semver-major fix)', result.notGated.majorOnly],
  ];
  for (const [heading, lines] of notGatedBlocks) {
    if (lines.length > 0) {
      console.log('');
      console.log(`${heading} (${lines.length}):`);
      for (const line of lines) {
        console.log(`  - ${line}`);
      }
    }
  }
  console.log('');
  if (result.failures.length === 0) {
    console.log(
      'PASS: no production advisory, no critical advisory, every high advisory is a reviewed exception, ' +
        'and no lower advisory has a fix within the current majors.',
    );
    return;
  }
  for (const failure of result.failures) {
    console.error(`FAIL ${failure}`);
  }
  console.error(`npm audit gate failed: ${result.failures.length} rule violation(s).`);
}

function main() {
  const options = parseArgs(process.argv.slice(2));
  if (options.help) {
    console.log(USAGE);
    return EXIT_PASS;
  }
  const allowlist = readJsonFile(options.allowlist, 'allowlist');
  validateAllowlist(allowlist, options.allowlist);

  const fullReport = options.auditJson
    ? readJsonFile(options.auditJson, 'audit JSON')
    : runNpmAudit(options.frontendDir, false);
  validateReport(fullReport, 'full audit report');
  assertAttributable(fullReport, 'full audit report');

  const prodReport = options.prodAuditJson
    ? readJsonFile(options.prodAuditJson, 'production audit JSON')
    : runNpmAudit(options.frontendDir, true);
  validateReport(prodReport, 'production audit report');

  const manifestFile = path.join(options.frontendDir, 'package.json');
  assertDevDependenciesCovered(fullReport, prodReport, readJsonFile(manifestFile, 'package manifest'), manifestFile);

  const result = evaluate({ fullReport, prodReport, allowlist, today: options.today });
  printResult({ options, fullReport, prodReport, allowlist, result });
  return result.failures.length === 0 ? EXIT_PASS : EXIT_FAIL;
}

try {
  process.exitCode = main();
} catch (error) {
  if (error instanceof UnusableInputError) {
    console.error(`ERROR [input] ${error.message}`);
    console.error('npm audit gate could not check the tree; this is not a pass.');
  } else {
    console.error(`ERROR [input] unexpected failure: ${error?.stack ?? error}`);
  }
  process.exitCode = EXIT_UNUSABLE;
}
