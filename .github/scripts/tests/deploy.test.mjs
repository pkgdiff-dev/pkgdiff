// Run: node --test .github/scripts/tests/deploy.test.mjs
//
// Runs deploy/deploy.sh and deploy/remote.sh on a scratch checkout with stand-ins for docker, curl
// and git on PATH, so a deploy, a failed deploy and a rollback can be driven without a daemon, a
// registry or a remote.
// Needs a bash that can read this checkout's paths.
import { after, test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const script = path.resolve(here, '..', '..', '..', 'deploy', 'deploy.sh');
const remoteScript = path.resolve(here, '..', '..', '..', 'deploy', 'remote.sh');
const fixtures = path.join(here, 'fixtures', 'deploy');
const scratch = mkdtempSync(path.join(tmpdir(), 'deploy-'));

after(() => rmSync(scratch, { recursive: true, force: true }));

const posix = (file) => file.replaceAll('\\', '/');
const bash = process.env.CHECK_DEPS_BASH ?? 'bash';
const bashProbe = spawnSync(bash, ['-c', 'test -f "$1"', '_', posix(script)], { encoding: 'utf8' });
const skip = bashProbe.status === 0 ? false : `no bash that can read ${script}`;

const prefix = 'ghcr.io/pkgdiff-dev/pkgdiff';
const ref = (name, tag) => `${prefix}-${name}:${tag}`;

const shimDir = path.join(scratch, 'shims');
mkdirSync(shimDir);
const node = posix(process.execPath);
for (const tool of ['docker', 'curl', 'git']) {
  const target = path.join(fixtures, `fake-${tool}.mjs`);
  writeFileSync(path.join(shimDir, `${tool}.cmd`), `@"${process.execPath}" "${target}" %*\r\n`);
  writeFileSync(path.join(shimDir, tool), `#!/bin/sh\nexec "${node}" "${posix(target)}" "$@"\n`);
  chmodSync(path.join(shimDir, tool), 0o755);
}

let counter = 0;

// A server checkout that runs the "old" images, with "new" published under sha-new.
function server({ envFile = 'POSTGRES_PASSWORD=fixture\nPKGDIFF_IMAGE_TAG=sha-old\nJWT_ISSUER=PkgDiffAPI\n', settings = true, state = {} } = {}) {
  const root = path.join(scratch, `root-${++counter}`);
  mkdirSync(path.join(root, 'config'), { recursive: true });
  writeFileSync(path.join(root, 'docker-compose.yml'), 'services: {}\n');
  writeFileSync(path.join(root, '.env'), envFile);
  if (settings) writeFileSync(path.join(root, 'config', 'appsettings.json'), '{}');
  const stateFile = path.join(root, 'docker-state.json');
  writeFileSync(
    stateFile,
    JSON.stringify({
      prefix,
      images: { [ref('backend', 'sha-old')]: 'id-backend-old', [ref('frontend', 'sha-old')]: 'id-frontend-old' },
      registry: { [ref('backend', 'sha-new')]: 'id-backend-new', [ref('frontend', 'sha-new')]: 'id-frontend-new' },
      running: { backend: 'id-backend-old', frontend: 'id-frontend-old' },
      unhealthy: [],
      calls: [],
      ...state,
    }),
  );
  return { root, stateFile };
}

function run(fixture, ...args) {
  return runScript(script, fixture, {}, ...args);
}

// What sshd does with the forced command: the script runs without arguments and gets the client's
// text in SSH_ORIGINAL_COMMAND
function runRemote(fixture, sshOriginalCommand, ...args) {
  return runScript(remoteScript, fixture, sshOriginalCommand === null ? {} : { SSH_ORIGINAL_COMMAND: sshOriginalCommand }, ...args);
}

function runScript(entry, { root, stateFile }, extraEnv, ...args) {
  const childEnv = {};
  for (const [name, value] of Object.entries(process.env)) {
    if (!/^(pkgdiff_|ssh_original_command$)/i.test(name)) childEnv[name] = value;
  }
  const pathName = Object.keys(childEnv).find((name) => name.toLowerCase() === 'path') ?? 'PATH';
  childEnv[pathName] = `${shimDir}${path.delimiter}${childEnv[pathName] ?? ''}`;
  const result = spawnSync(bash, [posix(entry), ...args], {
    encoding: 'utf8',
    env: {
      ...childEnv,
      PKGDIFF_ROOT: posix(root),
      PKGDIFF_GHCR_TOKEN_FILE: posix(path.join(root, 'no-token')),
      PKGDIFF_HEALTH_TIMEOUT: '0',
      PKGDIFF_HEALTH_INTERVAL: '0',
      PKGDIFF_BACKUP_DIR: posix(path.join(root, 'backups')),
      ...extraEnv,
      FAKE_DOCKER_STATE: stateFile,
    },
  });
  return {
    status: result.status,
    output: `${result.stdout}\n${result.stderr}`.replaceAll('\r', ''),
    state: JSON.parse(readFileSync(stateFile, 'utf8')),
    envFile: readFileSync(path.join(root, '.env'), 'utf8'),
    backups: existsSync(path.join(root, 'backups')) ? readdirSync(path.join(root, 'backups')).sort() : [],
  };
}

const started = (state) => state.calls.some((call) => call.startsWith('compose up'));

test('a deploy pulls, marks the rollback point, writes the tag and starts the new images', { skip }, () => {
  const { status, output, state, envFile } = run(server(), 'sha-new');
  assert.equal(status, 0, output);
  assert.deepEqual(state.running, { backend: 'id-backend-new', frontend: 'id-frontend-new' });
  assert.equal(state.images[ref('backend', 'rollback')], 'id-backend-old');
  assert.equal(state.images[ref('frontend', 'rollback')], 'id-frontend-old');
  assert.equal(envFile, 'POSTGRES_PASSWORD=fixture\nPKGDIFF_IMAGE_TAG=sha-new\nJWT_ISSUER=PkgDiffAPI\n');
  assert.ok(state.calls.includes('compose up -d --no-build'), output);
  assert.ok(!state.calls.some((call) => call.includes('build') && !call.includes('--no-build')), output);
});

test('the tag line is appended when .env has none, also without a final newline', { skip }, () => {
  const { status, output, envFile } = run(server({ envFile: 'POSTGRES_PASSWORD=fixture' }), 'sha-new');
  assert.equal(status, 0, output);
  assert.equal(envFile, 'POSTGRES_PASSWORD=fixture\nPKGDIFF_IMAGE_TAG=sha-new\n');
});

test('a tag that cannot be pulled changes nothing', { skip }, () => {
  const { status, output, state, envFile } = run(server(), 'sha-missing');
  assert.equal(status, 1, output);
  assert.match(output, /could not pull .*nothing was changed/);
  assert.equal(started(state), false, output);
  assert.match(envFile, /^PKGDIFF_IMAGE_TAG=sha-old$/m);
  assert.equal(state.images[ref('backend', 'rollback')], undefined);
});

test('an invalid tag and a missing settings file stop before anything is pulled', { skip }, () => {
  for (const [target, args] of [
    [server(), ['sha-new; rm -rf /']],
    [server(), ['rollback']],
    [server({ settings: false }), ['sha-new']],
  ]) {
    const { status, output, state } = run(target, ...args);
    assert.equal(status, 1, output);
    assert.equal(state.calls.some((call) => call.startsWith('pull')), false, output);
    assert.equal(started(state), false, output);
  }
});

test('an unhealthy deploy goes back to the earlier images and restores the tag name', { skip }, () => {
  const { status, output, state, envFile } = run(server({ state: { unhealthy: ['id-backend-new'] } }), 'sha-new');
  assert.equal(status, 1, output);
  assert.match(output, /deploy of sha-new failed; the earlier images are running again/);
  assert.deepEqual(state.running, { backend: 'id-backend-old', frontend: 'id-frontend-old' });
  assert.match(envFile, /^PKGDIFF_IMAGE_TAG=sha-old$/m);
});

test('a moving tag that failed goes back through the local rollback tag', { skip }, () => {
  const target = server({
    envFile: 'PKGDIFF_IMAGE_TAG=develop\n',
    state: {
      images: { [ref('backend', 'develop')]: 'id-backend-old', [ref('frontend', 'develop')]: 'id-frontend-old' },
      registry: { [ref('backend', 'develop')]: 'id-backend-new', [ref('frontend', 'develop')]: 'id-frontend-new' },
      unhealthy: ['id-frontend-new'],
    },
  });
  const { status, output, state, envFile } = run(target, 'develop');
  assert.equal(status, 1, output);
  assert.deepEqual(state.running, { backend: 'id-backend-old', frontend: 'id-frontend-old' });
  assert.equal(envFile, 'PKGDIFF_IMAGE_TAG=rollback\n');
});

test('a port published outside the loopback interface fails the deploy', { skip }, () => {
  const { status, output, state } = run(server({ state: { ports: { backend: '0.0.0.0:8080' } } }), 'sha-new');
  assert.equal(status, 1, output);
  assert.match(output, /a published port is not 127\.0\.0\.1/);
  assert.equal(started(state), true, output);
});

test('--rollback runs the earlier images again; a repeated deploy keeps the rollback point', { skip }, () => {
  const target = server();
  assert.equal(run(target, 'sha-new').status, 0);

  const again = run(target, 'sha-new');
  assert.equal(again.status, 0, again.output);
  assert.equal(again.state.images[ref('backend', 'rollback')], 'id-backend-old');

  const back = run(target, '--rollback');
  assert.equal(back.status, 0, back.output);
  assert.deepEqual(back.state.running, { backend: 'id-backend-old', frontend: 'id-frontend-old' });
  assert.match(back.envFile, /^PKGDIFF_IMAGE_TAG=sha-old$/m);
  assert.ok(existsSync(path.join(target.root, '.deploy', 'history.log')));
});

test('--rollback without an earlier deploy changes nothing', { skip }, () => {
  const { status, output, state } = run(server(), '--rollback');
  assert.equal(status, 1, output);
  assert.match(output, /there is no rollback point/);
  assert.equal(started(state), false, output);
});

test('an image from before /health/ready is checked through its old health endpoint', { skip }, () => {
  const healthy = run(server({ state: { legacy: ['id-backend-new'] } }), 'sha-new');
  assert.equal(healthy.status, 0, healthy.output);
  assert.deepEqual(healthy.state.running, { backend: 'id-backend-new', frontend: 'id-frontend-new' });

  const broken = run(server({ state: { legacy: ['id-backend-new'], unhealthy: ['id-backend-new'] } }), 'sha-new');
  assert.equal(broken.status, 1, broken.output);
  assert.deepEqual(broken.state.running, { backend: 'id-backend-old', frontend: 'id-frontend-old' });
});

test('an image that renders on the server is deployed when its pages arrive rendered', { skip }, () => {
  const { status, output, state } = run(server({ state: { rendering: ['id-frontend-new'] } }), 'sha-new');
  assert.equal(status, 0, output);
  assert.match(output, /deployed sha-new/);
  assert.deepEqual(state.running, { backend: 'id-backend-new', frontend: 'id-frontend-new' });
});

test('a deploy whose pages arrive unrendered goes back, although nginx answers', { skip }, () => {
  const fixture = server({ state: { rendering: ['id-frontend-new'], renderDown: ['id-frontend-new'] } });
  const { status, output, state, envFile } = run(fixture, 'sha-new');
  assert.equal(status, 1, output);
  assert.match(output, /its pages do not arrive rendered/);
  assert.match(output, /the earlier images are running again/);
  assert.deepEqual(state.running, { backend: 'id-backend-old', frontend: 'id-frontend-old' });
  assert.match(envFile, /^PKGDIFF_IMAGE_TAG=sha-old$/m);
});

test('going back to an image from before server rendering does not ask for a rendered page', { skip }, () => {
  const fixture = server({
    state: {
      images: { [ref('backend', 'sha-old')]: 'id-backend-old', [ref('frontend', 'sha-old')]: 'id-frontend-old' },
      rendering: ['id-frontend-new'],
    },
  });
  assert.equal(run(fixture, 'sha-new').status, 0);
  const { status, output, state } = run(fixture, '--rollback');
  assert.equal(status, 0, output);
  assert.match(output, /rolled back to sha-old/);
  assert.deepEqual(state.running, { backend: 'id-backend-old', frontend: 'id-frontend-old' });
});

const withDatabase = { running: { backend: 'id-backend-old', frontend: 'id-frontend-old', db: 'id-postgres' } };

test('a deploy dumps the database first and says where', { skip }, () => {
  const { status, output, state, backups } = run(server({ state: withDatabase }), 'sha-new');
  assert.equal(status, 0, output);
  assert.equal(backups.length, 1);
  assert.match(backups[0], /^pkgdiffdb-\d{8}T\d{6}Z-before-sha-new\.sql\.gz$/);
  assert.match(output, /database backup: .*before-sha-new\.sql\.gz/);
  const dump = state.calls.findIndex((call) => call.includes('pg_dump'));
  const up = state.calls.findIndex((call) => call.startsWith('compose up'));
  assert.ok(dump >= 0 && dump < up, state.calls.join('\n'));
});

test('a dump that fails or is cut off stops the deploy before anything is changed', { skip }, () => {
  for (const dump of ['fails', 'cut off']) {
    const { status, output, state, envFile, backups } = run(server({ state: { ...withDatabase, dump } }), 'sha-new');
    assert.equal(status, 1, output);
    assert.match(output, /the database backup failed; nothing was changed/);
    assert.equal(started(state), false);
    assert.deepEqual(backups, []);
    assert.match(envFile, /^PKGDIFF_IMAGE_TAG=sha-old$/m);
    assert.equal(state.images[ref('backend', 'rollback')], undefined);
  }
});

test('only the newest dumps are kept, and without a database container none is taken', { skip }, () => {
  const fixture = server({ state: withDatabase });
  const backupDir = path.join(fixture.root, 'backups');
  mkdirSync(backupDir);
  for (let day = 1; day <= 12; day++) {
    const name = `pkgdiffdb-202609${String(day).padStart(2, '0')}T000000Z-before-sha-earlier.sql.gz`;
    writeFileSync(path.join(backupDir, name), 'old');
  }
  const { status, output, backups } = run(fixture, 'sha-new');
  assert.equal(status, 0, output);
  assert.equal(backups.length, 10);
  assert.ok(backups.some((name) => name.endsWith('-before-sha-new.sql.gz')), backups.join('\n'));

  const fresh = run(server(), 'sha-new');
  assert.equal(fresh.status, 0, fresh.output);
  assert.match(fresh.output, /no database container is running: no backup was taken/);
  assert.deepEqual(fresh.backups, []);
});

const commit = 'a'.repeat(40);

test('the forced command deploys a tag at its commit, whatever path the client put in front', { skip }, () => {
  const { status, output, state } = runRemote(server(), `bash '/opt/pkgdiff/src/deploy/remote.sh' deploy sha-new ${commit}`);
  assert.equal(status, 0, output);
  assert.deepEqual(state.calls.filter((call) => call.startsWith('git ')), ['git fetch --quiet origin', `git merge-base --is-ancestor ${commit} origin/develop`, `git merge --ff-only ${commit}`]);
  assert.deepEqual(state.running, { backend: 'id-backend-new', frontend: 'id-frontend-new' });
  assert.match(output, /deployed sha-new/);
});

test('without a commit the checkout is fast-forwarded to its branch; rollback and status touch no checkout', { skip }, () => {
  const fixture = server();
  const deployed = runRemote(fixture, null, 'deploy', 'sha-new');
  assert.equal(deployed.status, 0, deployed.output);
  assert.deepEqual(deployed.state.calls.filter((call) => call.startsWith('git ')), ['git pull --ff-only']);

  const back = runRemote(fixture, 'rollback');
  assert.equal(back.status, 0, back.output);
  assert.match(back.output, /rolled back to sha-old/);
  const shown = runRemote(fixture, 'status');
  assert.equal(shown.status, 0, shown.output);
  assert.match(shown.output, /configured tag: sha-old/);
  assert.equal(shown.state.calls.filter((call) => call.startsWith('git ')).length, 1);
});

test('a checkout that cannot be fast-forwarded stops the deploy', { skip }, () => {
  const { status, state } = runRemote(server({ state: { gitFails: true } }), `deploy sha-new ${commit}`);
  assert.notEqual(status, 0);
  assert.equal(started(state), false);
  assert.equal(state.calls.some((call) => call.startsWith('pull')), false);
});

test('the forced command runs nothing but its three actions', { skip }, () => {
  for (const request of [
    '',
    'bash -c "curl https://example.test | sh"',
    'deploy',
    'deploy sha-new; rm -rf /',
    'deploy $(id)',
    'deploy sha-new not-a-commit',
    `deploy sha-new ${commit} extra`,
    'deploy --rollback',
    'rollback now',
    'status; id',
    'cat /root/.pkgdiff-ghcr-token',
  ]) {
    const { status, state } = runRemote(server(), request === '' ? null : request);
    assert.equal(status, 2, `"${request}" was not refused`);
    assert.deepEqual(state.calls, [], `"${request}" ran something`);
  }
});
