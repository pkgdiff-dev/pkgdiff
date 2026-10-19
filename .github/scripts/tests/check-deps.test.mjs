// Run: node --test .github/scripts/tests/check-deps.test.mjs
//
// Runs .github/scripts/check-deps.sh on a scratch checkout with stand-ins for npm and docker on PATH, so
// the npm and Trivy checks can be driven without a registry or a daemon. The scratch checkout has
// no Backend, so the three NuGet-side checks always report "could not check"; the assertions are
// about the npm and Trivy lines only. Needs a bash that can read this checkout's paths.
import { after, test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const script = path.resolve(here, '..', 'check-deps.sh');
const fixtures = path.join(here, 'fixtures', 'npm-audit-gate');
const scratch = mkdtempSync(path.join(tmpdir(), 'check-deps-'));

after(() => rmSync(scratch, { recursive: true, force: true }));

const posix = (file) => file.replaceAll('\\', '/');
const bash = process.env.CHECK_DEPS_BASH ?? 'bash';
const bashProbe = spawnSync(bash, ['-c', 'test -f "$1"', '_', posix(script)], { encoding: 'utf8' });
const skip = bashProbe.status === 0 ? false : `no bash that can read ${script}`;

function fixture(name) {
  return path.join(fixtures, name);
}

function readFixture(name) {
  return JSON.parse(readFileSync(fixture(name), 'utf8'));
}

let counter = 0;
function scratchFile(name, content) {
  const file = path.join(scratch, `${++counter}-${name}`);
  writeFileSync(file, content);
  return file;
}

// A checkout with the Frontend manifests only.
function checkout(allowlist) {
  const root = path.join(scratch, `root-${++counter}`);
  mkdirSync(path.join(root, 'src', 'frontend'), { recursive: true });
  writeFileSync(
    path.join(root, 'src', 'frontend', 'package.json'),
    JSON.stringify({ name: 'fixture', devDependencies: { tailwindcss: '^3.4.19', '@angular/cli': '^21.2.24' } }),
  );
  writeFileSync(path.join(root, 'src', 'frontend', 'package-lock.json'), '{}');
  writeFileSync(path.join(root, 'src', 'frontend', 'audit-allowlist.json'), JSON.stringify(allowlist));
  return root;
}

const shimDir = path.join(scratch, 'shims');
mkdirSync(shimDir);
const node = posix(process.execPath);
writeFileSync(path.join(shimDir, 'npm.cmd'), `@"${process.execPath}" "${fixture('fake-npm.mjs')}" %*\r\n`);
writeFileSync(path.join(shimDir, 'npm'), `#!/bin/sh\nexec "${node}" "${posix(fixture('fake-npm.mjs'))}" "$@"\n`);
// docker stand-in: every image exists; `docker run` prints FAKE_DOCKER_RUN_OUTPUT and exits FAKE_DOCKER_RUN_RC.
writeFileSync(
  path.join(shimDir, 'docker'),
  [
    '#!/bin/sh',
    'case "$1" in',
    '  image) exit 0 ;;',
    '  run) cat "$FAKE_DOCKER_RUN_OUTPUT"; exit "$FAKE_DOCKER_RUN_RC" ;;',
    '  *) exit 0 ;;',
    'esac',
    '',
  ].join('\n'),
);
for (const name of ['npm', 'docker']) {
  chmodSync(path.join(shimDir, name), 0o755);
}

function runCheckDeps({ env = {}, allowlist = readFixture('allowlist.json'), full = fixture('audit-dev-exceptions.json') } = {}) {
  const childEnv = {};
  for (const [name, value] of Object.entries(process.env)) {
    if (!/^(node_env|npm_config_.*|trivy|backend_image|frontend_image|pkgdiff_root|check_deps_docker)$/i.test(name)) {
      childEnv[name] = value;
    }
  }
  const pathName = Object.keys(childEnv).find((name) => name.toLowerCase() === 'path') ?? 'PATH';
  childEnv[pathName] = `${shimDir}${path.delimiter}${childEnv[pathName] ?? ''}`;
  const result = spawnSync(bash, [posix(script)], {
    encoding: 'utf8',
    // A findings result prints the whole Trivy report as evidence, once per image.
    maxBuffer: 64 * 1024 * 1024,
    env: {
      ...childEnv,
      PKGDIFF_ROOT: posix(checkout(allowlist)),
      CHECK_DEPS_DOCKER: '0',
      FAKE_NPM_FULL_REPORT: full,
      FAKE_NPM_PROD_REPORT: fixture('audit-prod-clean.json'),
      ...env,
    },
  });
  const output = `${result.stdout}\n${result.stderr}`.replaceAll('\r', '');
  return { status: result.status, output, lines: output.split('\n') };
}

const emptyAllowlist = () => ({ ...readFixture('allowlist.json'), exceptions: [] });

test('the npm checks pass on the fixture tree and name the two accepted exceptions', { skip }, () => {
  const { output, lines } = runCheckDeps();
  assert.ok(lines.includes('PASS npm-audit-prod'), output);
  assert.ok(lines.includes('PASS npm-audit-high'), output);
  assert.equal(lines.filter((line) => line.startsWith('    accepted exception: ')).length, 2, output);
});

test('an advisory that is only "not gated" is never shown as an accepted exception', { skip }, () => {
  const report = readFixture('audit-dev-exceptions.json');
  report.vulnerabilities.postcss = {
    name: 'postcss',
    severity: 'moderate',
    isDirect: true,
    via: [
      {
        source: 7,
        name: 'postcss',
        dependency: 'postcss',
        title: 'postcss fixture advisory',
        url: 'https://github.com/advisories/GHSA-dddd-eeee-ffff',
        severity: 'moderate',
        range: '*',
      },
    ],
    effects: [],
    range: '*',
    nodes: ['node_modules/postcss'],
    fixAvailable: false,
  };
  report.metadata.vulnerabilities.moderate += 1;
  report.metadata.vulnerabilities.total += 1;
  const { output, lines } = runCheckDeps({ full: scratchFile('audit.json', JSON.stringify(report)) });
  assert.ok(lines.includes('PASS npm-audit-high'), output);
  const accepted = lines.filter((line) => line.startsWith('    accepted exception: '));
  assert.equal(accepted.length, 2, output);
  assert.doesNotMatch(accepted.join('\n'), /GHSA-dddd-eeee-ffff/);
  assert.ok(
    lines.some((line) => line.startsWith('    not gated (no fix available): GHSA-dddd-eeee-ffff in postcss (moderate)')),
    output,
  );
});

// ---- Trivy: "the scan found something" and "the scan did not run" are different results

const TRIVY_FINDINGS = [
  '2026-10-03T18:44:06Z\tINFO\tDetected OS\tfamily="debian" version="13.7"',
  '',
  'Report Summary',
  '',
  'fixture-image (debian 13.7)',
  '===========================',
  'Total: 5 (HIGH: 5, CRITICAL: 0)',
  '',
].join('\n');
const TRIVY_CLEAN = [
  '2026-10-03T18:44:06Z\tINFO\tDetected OS\tfamily="ubuntu" version="24.04"',
  '2026-10-03T18:44:06Z\tINFO\t[ubuntu] Detecting vulnerabilities...\tos_version="24.04" pkg_num=97',
  '',
].join('\n');
const DOCKER_COULD_NOT_START = [
  "Unable to find image 'aquasec/trivy:0.74.0@sha256:62b1' locally",
  'docker: Error response from daemon: failed to resolve reference: dial tcp: lookup registry-1.docker.io: no such host',
  '',
].join('\n');
const TRIVY_FATAL = [
  '2026-10-03T18:44:06Z\tINFO\t[vulndb] Downloading vulnerability DB...',
  '2026-10-03T18:44:09Z\tFATAL\tFatal error\trun error: init error: DB error: failed to download vulnerability DB',
  '',
].join('\n');

function runTrivy(output, rc) {
  const result = runCheckDeps({
    env: {
      TRIVY: '1',
      BACKEND_IMAGE: 'fixture-backend',
      FRONTEND_IMAGE: 'fixture-frontend',
      FAKE_DOCKER_RUN_OUTPUT: posix(scratchFile('docker-run.log', output)),
      FAKE_DOCKER_RUN_RC: String(rc),
    },
  });
  const summary = result.lines.find((line) => line.startsWith('check-deps: ') && line.includes(' passed, '));
  const unchecked = result.lines.filter((line) => /^FAIL [a-z-]+: could not check - /.test(line)).length;
  return { ...result, summary, unchecked, trivy: result.lines.filter((line) => /^(PASS|FAIL) trivy-/.test(line)) };
}

for (const rc of [125, 126, 127]) {
  test(`a scanner that never started (docker run exit ${rc}) is "could not check", not a finding`, { skip }, () => {
    const { status, output, trivy, summary, unchecked } = runTrivy(DOCKER_COULD_NOT_START, rc);
    assert.equal(status, 1, output);
    assert.equal(trivy.length, 2, output);
    for (const line of trivy) {
      assert.match(line, new RegExp(`^FAIL trivy-(backend|frontend): could not check - trivy did not complete \\(exit ${rc}\\)`));
    }
    assert.doesNotMatch(output, /fixable HIGH\/CRITICAL findings/);
    assert.ok(summary.endsWith(`(${unchecked} could not be checked)`), output);
    // sdk-image-pin and the two NuGet checks cannot run on the scratch checkout either.
    assert.equal(unchecked, 5, output);
  });
}

test('a scan that stopped with a fatal error and no report is "could not check"', { skip }, () => {
  const { output, trivy } = runTrivy(TRIVY_FATAL, 1);
  assert.match(trivy[0], /^FAIL trivy-backend: could not check - trivy did not complete \(exit 1\): .*FATAL/, output);
  assert.doesNotMatch(output, /fixable HIGH\/CRITICAL findings/);
});

test('exit 1 without a parsable report is "could not check"', { skip }, () => {
  const { output, trivy, unchecked } = runTrivy('something unexpected\n', 1);
  assert.match(trivy[0], /^FAIL trivy-backend: could not check - trivy did not complete \(exit 1\)/, output);
  assert.equal(unchecked, 5, output);
});

test('exit 1 with a report is a finding and is not counted as "could not check"', { skip }, () => {
  const { status, output, trivy, summary, unchecked } = runTrivy(TRIVY_FINDINGS, 1);
  assert.equal(status, 1, output);
  assert.equal(
    trivy[0],
    'FAIL trivy-backend: fixable HIGH/CRITICAL findings in fixture-backend: Total: 5 (HIGH: 5, CRITICAL: 0)',
    output,
  );
  assert.equal(unchecked, 3, output);
  assert.ok(summary.endsWith('(3 could not be checked)'), output);
});

// The report of an image that was not rebuilt for months is hundreds of kilobytes, with the
// "Total:" line near the top. Reading it through a pipe into `grep -q` under `set -o pipefail`
// made the result depend on the size of the report: the writer was killed by SIGPIPE.
function largeTrivyFindings(eol) {
  const rows = [];
  for (let row = 0; row < 3000; row += 1) {
    rows.push(
      `│ libfixture${row} │ CVE-2026-${10000 + row} │ HIGH │ fixed │ 1.0.${row}-1 │ 1.0.${row}-2 │ fixture advisory ${row}: a row about as long as one in a real report │`,
    );
  }
  return [
    '2026-10-03T18:44:06Z\tINFO\tDetected OS\tfamily="debian" version="13.7"',
    '',
    'Report Summary',
    '',
    'fixture-image (debian 13.7)',
    '===========================',
    'Total: 3000 (HIGH: 3000, CRITICAL: 0)',
    '',
    ...rows,
    '',
  ].join(eol);
}

for (const [label, eol] of [
  ['LF', '\n'],
  ['CRLF', '\r\n'],
]) {
  test(`a report larger than 256 KB (${label} line ends) is a finding on every run`, { skip }, () => {
    const report = largeTrivyFindings(eol);
    assert.ok(Buffer.byteLength(report) > 256 * 1024, `the fixture is ${Buffer.byteLength(report)} bytes`);
    for (let run = 1; run <= 3; run += 1) {
      const { status, trivy, summary, unchecked } = runTrivy(report, 1);
      assert.equal(status, 1, `run ${run}`);
      assert.deepEqual(
        trivy,
        [
          'FAIL trivy-backend: fixable HIGH/CRITICAL findings in fixture-backend: Total: 3000 (HIGH: 3000, CRITICAL: 0)',
          'FAIL trivy-frontend: fixable HIGH/CRITICAL findings in fixture-frontend: Total: 3000 (HIGH: 3000, CRITICAL: 0)',
        ],
        `run ${run}`,
      );
      assert.equal(unchecked, 3, `run ${run}: ${summary}`);
    }
  });
}

test('exit 0 with a recognised OS passes', { skip }, () => {
  const { output, trivy } = runTrivy(TRIVY_CLEAN, 0);
  assert.deepEqual(trivy, ['PASS trivy-backend', 'PASS trivy-frontend'], output);
});

test('NODE_ENV=production in the caller does not turn an unlisted high advisory into a pass', { skip }, () => {
  const { status, output, lines } = runCheckDeps({ env: { NODE_ENV: 'production' }, allowlist: emptyAllowlist() });
  assert.equal(status, 1, output);
  assert.ok(!lines.includes('PASS npm-audit-high'), output);
  assert.match(output, /^FAIL npm-audit-high: rule broken \(npm-audit-gate exit 1\), 2 violation\(s\)/m);
});

test('an inherited include=dev does not make the production audit report devDependencies', { skip }, () => {
  const { output, lines } = runCheckDeps({ env: { NPM_CONFIG_INCLUDE: 'dev' } });
  assert.ok(lines.includes('PASS npm-audit-prod'), output);
  assert.ok(lines.includes('PASS npm-audit-high'), output);
});
