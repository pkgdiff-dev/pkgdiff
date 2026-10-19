// Run: node --test .github/scripts/tests/npm-audit-gate.test.mjs
import { after, test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const gate = path.resolve(here, '..', 'npm-audit-gate.mjs');
const repoRoot = path.resolve(here, '..', '..', '..');
const fixtures = path.join(here, 'fixtures', 'npm-audit-gate');
const scratch = mkdtempSync(path.join(tmpdir(), 'npm-audit-gate-'));

after(() => rmSync(scratch, { recursive: true, force: true }));

const BRACES = 'GHSA-vfj7-8cjw-p6xm';
const HTTP_CACHE = 'GHSA-ch52-4w7c-c8xp';
const TODAY = '2026-10-03';

function fixture(name) {
  return path.join(fixtures, name);
}

function readFixture(name) {
  return JSON.parse(readFileSync(fixture(name), 'utf8'));
}

// Stands in for src/frontend/: the gate reads package.json there to know that devDependencies exist.
let frontendCounter = 0;
function frontendDir(manifest) {
  const dir = path.join(scratch, `frontend-${++frontendCounter}`);
  mkdirSync(dir);
  if (manifest !== null) {
    writeFileSync(path.join(dir, 'package.json'), JSON.stringify(manifest, null, 2));
  }
  return dir;
}

const MANIFEST = {
  name: 'fixture',
  dependencies: { rxjs: '~7.8.0' },
  devDependencies: { tailwindcss: '^3.4.19', '@angular/cli': '^21.2.24' },
};
const frontend = frontendDir(MANIFEST);

// A directory that goes first on PATH and answers `npm` with fixtures/npm-audit-gate/fake-npm.mjs.
const npmShimDir = path.join(scratch, 'npm-shim');
mkdirSync(npmShimDir);
const fakeNpm = fixture('fake-npm.mjs');
writeFileSync(path.join(npmShimDir, 'npm.cmd'), `@"${process.execPath}" "${fakeNpm}" %*\r\n`);
writeFileSync(
  path.join(npmShimDir, 'npm'),
  `#!/bin/sh\nexec "${process.execPath.replaceAll('\\', '/')}" "${fakeNpm.replaceAll('\\', '/')}" "$@"\n`,
);
chmodSync(path.join(npmShimDir, 'npm'), 0o755);

// The caller's environment without anything that tells npm what to audit, plus the shim on PATH.
function environment(extra) {
  const env = {};
  for (const [name, value] of Object.entries(process.env)) {
    if (!/^(node_env|npm_config_.*)$/i.test(name)) {
      env[name] = value;
    }
  }
  const pathName = Object.keys(env).find((name) => name.toLowerCase() === 'path') ?? 'PATH';
  env[pathName] = `${npmShimDir}${path.delimiter}${env[pathName] ?? ''}`;
  return { ...env, ...extra };
}

// Runs the gate the way check-deps.sh and a developer do: it calls npm itself.
function runGateWithNpm({ env = {}, allowlist = fixture('allowlist.json'), dir = frontend } = {}) {
  const log = path.join(scratch, `npm-calls-${++variantCounter}.log`);
  const result = spawnSync(process.execPath, [gate, dir, '--allowlist', allowlist, '--today', TODAY], {
    encoding: 'utf8',
    env: environment({
      FAKE_NPM_FULL_REPORT: fixture('audit-dev-exceptions.json'),
      FAKE_NPM_PROD_REPORT: fixture('audit-prod-clean.json'),
      FAKE_NPM_LOG: log,
      ...env,
    }),
  });
  const calls = existsSync(log)
    ? readFileSync(log, 'utf8').trim().split('\n').map((line) => JSON.parse(line))
    : [];
  return { status: result.status, output: `${result.stdout}\n${result.stderr}`, calls };
}

// Rewrites metadata so a mutated report stays internally consistent, like a real npm report.
function recount(report) {
  const counts = { info: 0, low: 0, moderate: 0, high: 0, critical: 0, total: 0 };
  for (const entry of Object.values(report.vulnerabilities)) {
    counts[entry.severity] += 1;
    counts.total += 1;
  }
  report.metadata.vulnerabilities = counts;
  return report;
}

let variantCounter = 0;
function variant(baseName, mutate) {
  const data = readFixture(baseName);
  mutate(data);
  const file = path.join(scratch, `variant-${++variantCounter}.json`);
  writeFileSync(file, JSON.stringify(data, null, 2));
  return file;
}

function runGate(overrides = {}) {
  const options = {
    audit: fixture('audit-dev-exceptions.json'),
    prod: fixture('audit-prod-clean.json'),
    allowlist: fixture('allowlist.json'),
    today: TODAY,
    dir: frontend,
    ...overrides,
  };
  const result = spawnSync(
    process.execPath,
    [
      gate,
      options.dir,
      '--audit-json',
      options.audit,
      '--prod-audit-json',
      options.prod,
      '--allowlist',
      options.allowlist,
      '--today',
      options.today,
    ],
    { encoding: 'utf8' },
  );
  return { status: result.status, output: `${result.stdout}\n${result.stderr}` };
}

test('passes when only the two allowlisted dev advisories are reported and prints each with its reason', () => {
  const { status, output } = runGate();
  assert.equal(status, 0, output);
  assert.match(output, new RegExp(BRACES));
  assert.match(output, new RegExp(HTTP_CACHE));
  assert.match(output, /Fixture reason for braces\./);
  assert.match(output, /Fixture reason for http-cache-semantics\./);
  assert.match(output, /Fixture exit condition for braces\./);
  assert.match(output, /PASS/);
});

test('a dependent that only inherits the advisory may report fixAvailable=true without failing the gate', () => {
  const report = readFixture('audit-dev-exceptions.json');
  assert.equal(report.vulnerabilities['fast-glob'].fixAvailable, true);
  assert.equal(runGate().status, 0);
});

test('fails on a high advisory that is not allowlisted', () => {
  const audit = variant('audit-dev-exceptions.json', (report) => {
    report.vulnerabilities['left-pad'] = {
      name: 'left-pad',
      severity: 'high',
      isDirect: false,
      via: [
        {
          source: 1,
          name: 'left-pad',
          dependency: 'left-pad',
          title: 'left-pad fixture advisory',
          url: 'https://github.com/advisories/GHSA-aaaa-bbbb-cccc',
          severity: 'high',
          range: '*',
        },
      ],
      effects: [],
      range: '*',
      nodes: ['node_modules/left-pad'],
      fixAvailable: false,
    };
    recount(report);
  });
  const { status, output } = runGate({ audit });
  assert.equal(status, 1, output);
  assert.match(output, /FAIL \[unlisted-high\].*GHSA-aaaa-bbbb-cccc/);
  assert.doesNotMatch(output, /PASS/);
});

test('fails on a critical advisory even when its id is allowlisted', () => {
  const audit = variant('audit-dev-exceptions.json', (report) => {
    report.vulnerabilities.braces.severity = 'critical';
    report.vulnerabilities.braces.via[0].severity = 'critical';
    recount(report);
  });
  const { status, output } = runGate({ audit });
  assert.equal(status, 1, output);
  assert.match(output, new RegExp(`FAIL \\[critical\\].*${BRACES}`));
});

test('fails when the production audit reports any vulnerability', () => {
  const prod = variant('audit-prod-clean.json', (report) => {
    report.vulnerabilities.rxjs = {
      name: 'rxjs',
      severity: 'moderate',
      isDirect: true,
      via: [
        {
          source: 2,
          name: 'rxjs',
          dependency: 'rxjs',
          title: 'rxjs fixture advisory',
          url: 'https://github.com/advisories/GHSA-dddd-eeee-ffff',
          severity: 'moderate',
          range: '*',
        },
      ],
      effects: [],
      range: '*',
      nodes: ['node_modules/rxjs'],
      fixAvailable: true,
    };
    recount(report);
  });
  const { status, output } = runGate({ prod });
  assert.equal(status, 1, output);
  assert.match(output, /FAIL \[production\].*rxjs/);
});

test('fails on a stale allowlist entry whose advisory is no longer reported', () => {
  const audit = variant('audit-dev-exceptions.json', (report) => {
    delete report.vulnerabilities['http-cache-semantics'];
    delete report.vulnerabilities['make-fetch-happen'];
    delete report.vulnerabilities['@angular/cli'];
    recount(report);
  });
  const { status, output } = runGate({ audit });
  assert.equal(status, 1, output);
  assert.match(output, new RegExp(`FAIL \\[stale\\].*${HTTP_CACHE}`));
});

test('the stale failure asks for a devDependencies coverage check before the entry is removed', () => {
  const audit = variant('audit-dev-exceptions.json', (report) => {
    delete report.vulnerabilities['http-cache-semantics'];
    delete report.vulnerabilities['make-fetch-happen'];
    delete report.vulnerabilities['@angular/cli'];
    recount(report);
  });
  const { output } = runGate({ audit });
  const stale = output.split('\n').find((line) => line.startsWith('FAIL [stale]'));
  assert.ok(stale, output);
  assert.match(stale, /no longer reported/);
  assert.match(stale, /first confirm .*devDependencies.*only then remove the entry/i);
  assert.doesNotMatch(stale, /reported: remove the entry/);
});

test('fails when an allowlisted advisory can be fixed without a semver-major bump (fixAvailable true)', () => {
  const audit = variant('audit-dev-exceptions.json', (report) => {
    report.vulnerabilities.braces.fixAvailable = true;
  });
  const { status, output } = runGate({ audit });
  assert.equal(status, 1, output);
  assert.match(output, new RegExp(`FAIL \\[fix-available\\].*${BRACES}`));
});

test('fails when an allowlisted advisory has a non-major fix object', () => {
  const audit = variant('audit-dev-exceptions.json', (report) => {
    report.vulnerabilities['http-cache-semantics'].fixAvailable = {
      name: '@angular/cli',
      version: '21.2.30',
      isSemVerMajor: false,
    };
  });
  const { status, output } = runGate({ audit });
  assert.equal(status, 1, output);
  assert.match(output, new RegExp(`FAIL \\[fix-available\\].*${HTTP_CACHE}`));
});

test('fails the day after reviewBy and still passes on the reviewBy date', () => {
  const expired = runGate({ today: '2026-11-04' });
  assert.equal(expired.status, 1, expired.output);
  assert.match(expired.output, /FAIL \[expired\].*2026-11-03/);

  const lastDay = runGate({ today: '2026-11-03' });
  assert.equal(lastDay.status, 0, lastDay.output);
});

test('reports every broken rule in one run', () => {
  const audit = variant('audit-dev-exceptions.json', (report) => {
    report.vulnerabilities.braces.fixAvailable = true;
  });
  const { status, output } = runGate({ audit, today: '2027-01-01' });
  assert.equal(status, 1, output);
  assert.match(output, /FAIL \[fix-available\]/);
  assert.match(output, /FAIL \[expired\]/);
});

test('exits 2 when the audit output is not JSON', () => {
  const { status, output } = runGate({ audit: fixture('audit-not-json.txt') });
  assert.equal(status, 2, output);
  assert.match(output, /ERROR \[input\]/);
});

test('exits 2 when the production audit output is not JSON', () => {
  const { status, output } = runGate({ prod: fixture('audit-not-json.txt') });
  assert.equal(status, 2, output);
  assert.match(output, /ERROR \[input\]/);
});

test('exits 2 when npm returned an error object instead of a report', () => {
  const audit = path.join(scratch, 'npm-error.json');
  writeFileSync(audit, JSON.stringify({ error: { code: 'ENOLOCK', summary: 'requires a lockfile' } }));
  const { status, output } = runGate({ audit });
  assert.equal(status, 2, output);
  assert.match(output, /ERROR \[input\]/);
});

test('exits 2 when the allowlist file is missing', () => {
  const { status, output } = runGate({ allowlist: path.join(scratch, 'no-such-allowlist.json') });
  assert.equal(status, 2, output);
  assert.match(output, /ERROR \[input\].*allowlist/i);
});

test('exits 2 when an allowlist entry is not dev-scoped', () => {
  const allowlist = variant('allowlist.json', (data) => {
    data.exceptions[0].scope = 'prod';
  });
  const { status, output } = runGate({ allowlist });
  assert.equal(status, 2, output);
  assert.match(output, /ERROR \[input\].*scope/);
});

test('exits 2 when an allowlist entry has no reason', () => {
  const allowlist = variant('allowlist.json', (data) => {
    delete data.exceptions[1].reason;
  });
  const { status, output } = runGate({ allowlist });
  assert.equal(status, 2, output);
  assert.match(output, /ERROR \[input\].*reason/);
});

test('exits 2 when --today is not a real date', () => {
  const { status, output } = runGate({ today: '2026-13-40' });
  assert.equal(status, 2, output);
  assert.match(output, /ERROR \[input\].*--today/);
});

// ---- An allowlist entry covers one advisory on one package

// A second package that carries an advisory object; npm lists packages alphabetically.
function withSecondCarrier(report, name, ghsa, { advisedName = name, reverse = false } = {}) {
  report.vulnerabilities[name] = {
    name,
    severity: 'high',
    isDirect: false,
    via: [
      {
        source: 9,
        name: advisedName,
        dependency: advisedName,
        title: `${name} fixture advisory`,
        url: `https://github.com/advisories/${ghsa}`,
        severity: 'high',
        range: '*',
      },
    ],
    effects: [],
    range: '*',
    nodes: [`node_modules/${name}`],
    fixAvailable: false,
  };
  const names = Object.keys(report.vulnerabilities).sort();
  if (reverse) {
    names.reverse();
  }
  report.vulnerabilities = Object.fromEntries(names.map((key) => [key, report.vulnerabilities[key]]));
  recount(report);
}

function failureLines(output) {
  return output.split('\n').filter((line) => line.startsWith('FAIL ')).sort();
}

for (const extra of ['anymatch', 'picomatch']) {
  test(`an allowlisted advisory on a second package (${extra}) is an unlisted high, in either key order`, () => {
    const sorted = runGate({ audit: variant('audit-dev-exceptions.json', (report) => withSecondCarrier(report, extra, BRACES)) });
    assert.equal(sorted.status, 1, sorted.output);
    assert.match(sorted.output, new RegExp(`FAIL \\[unlisted-high\\] ${BRACES} in ${extra} `));
    assert.doesNotMatch(sorted.output, /PASS/);
    // The reviewed pair is still shown as accepted; only the second package fails.
    assert.match(sorted.output, new RegExp(`  - ${BRACES}  braces  high`));
    assert.equal(failureLines(sorted.output).length, 1, sorted.output);

    const reversed = runGate({
      audit: variant('audit-dev-exceptions.json', (report) => withSecondCarrier(report, extra, BRACES, { reverse: true })),
    });
    assert.equal(reversed.status, 1, reversed.output);
    assert.deepEqual(failureLines(reversed.output), failureLines(sorted.output));
  });
}

test('an allowlisted id that npm reports for another package only is an unlisted high', () => {
  const audit = variant('audit-dev-exceptions.json', (report) => {
    delete report.vulnerabilities.braces;
    delete report.vulnerabilities.micromatch;
    delete report.vulnerabilities['fast-glob'];
    delete report.vulnerabilities.tailwindcss;
    withSecondCarrier(report, 'picomatch', BRACES);
  });
  const { status, output } = runGate({ audit });
  assert.equal(status, 1, output);
  assert.match(output, new RegExp(`FAIL \\[unlisted-high\\] ${BRACES} in picomatch .*covers braces only`));
});

test('an advisory object that names another package than the one carrying it is not covered', () => {
  const audit = variant('audit-dev-exceptions.json', (report) => {
    report.vulnerabilities.braces.via[0].name = 'expand-brackets';
  });
  const { status, output } = runGate({ audit });
  assert.equal(status, 1, output);
  assert.match(output, new RegExp(`FAIL \\[unlisted-high\\] ${BRACES} in braces \\(advisory for expand-brackets\\)`));
});

// ---- Advisories below high

function withLowerAdvisory(report, { severity = 'moderate', fixAvailable }) {
  report.vulnerabilities.postcss = {
    name: 'postcss',
    severity,
    isDirect: true,
    via: [
      {
        source: 7,
        name: 'postcss',
        dependency: 'postcss',
        title: 'postcss fixture advisory',
        url: 'https://github.com/advisories/GHSA-dddd-eeee-ffff',
        severity,
        range: '*',
      },
    ],
    effects: [],
    range: '*',
    nodes: ['node_modules/postcss'],
    fixAvailable,
  };
  recount(report);
}

// The lines of one block of the gate output: from its heading to the next empty line.
function block(output, heading) {
  const lines = output.split('\n');
  const start = lines.findIndex((line) => line.startsWith(heading));
  if (start === -1) {
    return null;
  }
  const end = lines.indexOf('', start);
  return lines.slice(start, end === -1 ? lines.length : end);
}

test('fails on a moderate advisory that npm audit fix can resolve', () => {
  const audit = variant('audit-dev-exceptions.json', (report) => withLowerAdvisory(report, { fixAvailable: true }));
  const { status, output } = runGate({ audit });
  assert.equal(status, 1, output);
  assert.match(output, /FAIL \[fixable\] GHSA-dddd-eeee-ffff in postcss \(moderate\)/);
  assert.doesNotMatch(output, /PASS/);
});

test('fails on a low advisory whose fix is not a semver-major bump', () => {
  const audit = variant('audit-dev-exceptions.json', (report) =>
    withLowerAdvisory(report, { severity: 'low', fixAvailable: { name: 'postcss', version: '8.5.9', isSemVerMajor: false } }),
  );
  const { status, output } = runGate({ audit });
  assert.equal(status, 1, output);
  assert.match(output, /FAIL \[fixable\] GHSA-dddd-eeee-ffff in postcss \(low\).*postcss@8\.5\.9/);
});

test('a moderate advisory without any fix passes and is listed as not gated, never as an accepted exception', () => {
  const audit = variant('audit-dev-exceptions.json', (report) => withLowerAdvisory(report, { fixAvailable: false }));
  const { status, output } = runGate({ audit });
  assert.equal(status, 0, output);
  const notGated = block(output, 'Not gated (no fix available) (1):');
  assert.ok(notGated, output);
  assert.match(notGated[1], /^  - GHSA-dddd-eeee-ffff in postcss \(moderate\)/);
  const accepted = block(output, 'Accepted exceptions (2):');
  assert.ok(accepted, output);
  assert.doesNotMatch(accepted.join('\n'), /GHSA-dddd-eeee-ffff/);
});

test('a low advisory whose only fix is a semver-major bump passes under its own heading', () => {
  const audit = variant('audit-dev-exceptions.json', (report) =>
    withLowerAdvisory(report, { severity: 'low', fixAvailable: { name: 'tailwindcss', version: '4.3.3', isSemVerMajor: true } }),
  );
  const { status, output } = runGate({ audit });
  assert.equal(status, 0, output);
  const notGated = block(output, 'Not gated (only a semver-major fix) (1):');
  assert.ok(notGated, output);
  assert.match(notGated[1], /GHSA-dddd-eeee-ffff in postcss \(low\).*tailwindcss@4\.3\.3/);
  assert.equal(block(output, 'Not gated (no fix available)'), null, output);
});

// ---- What the audit covers (the gate runs npm itself; npm is the fixture stand-in)

function emptyAllowlist() {
  return variant('allowlist.json', (data) => {
    data.exceptions = [];
  });
}

test('asks npm for devDependencies explicitly and for production dependencies explicitly', () => {
  const { status, output, calls } = runGateWithNpm();
  assert.equal(status, 0, output);
  assert.equal(calls.length, 2, output);
  assert.deepEqual(calls[0].args, ['audit', '--json', '--include=dev']);
  assert.deepEqual(calls[1].args, ['audit', '--json', '--omit=dev']);
});

test('NODE_ENV=production does not hide devDependencies: the accepted exceptions are still reported', () => {
  const { status, output, calls } = runGateWithNpm({ env: { NODE_ENV: 'production' } });
  assert.equal(status, 0, output);
  assert.deepEqual(calls.map((call) => call.devAudited), [true, false]);
  assert.match(output, /7 vulnerable package\(s\)/);
  assert.match(output, new RegExp(BRACES));
  assert.doesNotMatch(output, /\[stale\]/);
});

test('NODE_ENV=production with an empty allowlist fails on the unlisted high advisories', () => {
  const { status, output } = runGateWithNpm({ env: { NODE_ENV: 'production' }, allowlist: emptyAllowlist() });
  assert.equal(status, 1, output);
  assert.match(output, new RegExp(`FAIL \\[unlisted-high\\].*${BRACES}`));
  assert.match(output, new RegExp(`FAIL \\[unlisted-high\\].*${HTTP_CACHE}`));
  assert.doesNotMatch(output, /PASS/);
});

test('npm_config_omit=dev with an empty allowlist fails on the unlisted high advisories', () => {
  const { status, output } = runGateWithNpm({ env: { npm_config_omit: 'dev' }, allowlist: emptyAllowlist() });
  assert.equal(status, 1, output);
  assert.match(output, /FAIL \[unlisted-high\]/);
  assert.doesNotMatch(output, /PASS/);
});

test('an inherited include=dev (any letter case) does not leak devDependencies into the production audit', () => {
  const { status, output, calls } = runGateWithNpm({ env: { NPM_CONFIG_INCLUDE: 'dev' } });
  assert.equal(status, 0, output);
  assert.deepEqual(calls.map((call) => call.devAudited), [true, false]);
  assert.doesNotMatch(output, /\[production\]/);
});

test('exits 2 when package.json declares devDependencies but the full report counts none', () => {
  const audit = variant('audit-prod-clean.json', () => {});
  const { status, output } = runGate({ audit, allowlist: emptyAllowlist() });
  assert.equal(status, 2, output);
  assert.match(output, /ERROR \[input\].*devDependencies/);
  assert.doesNotMatch(output, /PASS/);
});

test('exits 2 when the full report covers fewer dependencies than the production report', () => {
  const prod = variant('audit-prod-clean.json', (report) => {
    report.metadata.dependencies.total = 700;
  });
  const { status, output } = runGate({ prod });
  assert.equal(status, 2, output);
  assert.match(output, /ERROR \[input\].*fewer dependencies/);
});

test('exits 2 when a report has no dependency counts', () => {
  const audit = variant('audit-dev-exceptions.json', (report) => {
    delete report.metadata.dependencies;
  });
  const { status, output } = runGate({ audit });
  assert.equal(status, 2, output);
  assert.match(output, /ERROR \[input\].*metadata\.dependencies/);
});

test('exits 2 when package.json is missing next to the lock file', () => {
  const { status, output } = runGate({ dir: frontendDir(null) });
  assert.equal(status, 2, output);
  assert.match(output, /ERROR \[input\].*package\.json/);
});

test('a project without devDependencies passes with a report that counts none', () => {
  const audit = variant('audit-prod-clean.json', () => {});
  const dir = frontendDir({ name: 'fixture', dependencies: { rxjs: '~7.8.0' } });
  const { status, output } = runGate({ audit, allowlist: emptyAllowlist(), dir });
  assert.equal(status, 0, output);
  assert.match(output, /PASS/);
});

// Needs the registry: NPM_AUDIT_GATE_LIVE=1 node --test .github/scripts/tests/npm-audit-gate.test.mjs
test(
  'live: the real npm reports the same Frontend audit with and without NODE_ENV=production / omit=dev',
  { skip: process.env.NPM_AUDIT_GATE_LIVE === '1' ? false : 'set NPM_AUDIT_GATE_LIVE=1 (calls the npm registry)' },
  () => {
    const dir = frontendDir(JSON.parse(readFileSync(path.join(repoRoot, 'src', 'frontend', 'package.json'), 'utf8')));
    writeFileSync(path.join(dir, 'package-lock.json'), readFileSync(path.join(repoRoot, 'src', 'frontend', 'package-lock.json')));
    const run = (extra) => {
      const env = environment(extra);
      const pathName = Object.keys(env).find((name) => name.toLowerCase() === 'path');
      env[pathName] = env[pathName].slice(npmShimDir.length + path.delimiter.length);
      const result = spawnSync(process.execPath, [gate, dir, '--allowlist', emptyAllowlist()], { encoding: 'utf8', env });
      const output = `${result.stdout}\n${result.stderr}`;
      return { status: result.status, output, full: output.split('\n').find((line) => line.startsWith('Full audit')) };
    };
    const neutral = run({});
    const production = run({ NODE_ENV: 'production', npm_config_omit: 'dev' });
    assert.ok(neutral.full, neutral.output);
    assert.equal(production.full, neutral.full, production.output);
    assert.equal(production.status, neutral.status, production.output);
  },
);
