// Stand-in for docker in .github/scripts/tests/deploy.test.mjs. Its whole world is the JSON file
// FAKE_DOCKER_STATE: local images, registry images, what each container runs, and every call.
import { readFileSync, writeFileSync } from 'node:fs';

const file = process.env.FAKE_DOCKER_STATE;
const state = JSON.parse(readFileSync(file, 'utf8'));
const args = process.argv.slice(2);
state.calls.push(args.join(' '));

function done(code = 0, out) {
  writeFileSync(file, JSON.stringify(state));
  if (out !== undefined) process.stdout.write(`${out}\n`);
  process.exit(code);
}

const service = (container) => container.replace(/^pkgdiff-/, '');
const format = () => args[args.indexOf('--format') + 1] ?? '';

switch (args[0]) {
  case 'compose': {
    if (args[1] === 'version') done(0, 'Docker Compose version v2.40.0');
    if (args[1] === 'exec' && args.includes('pg_dump')) {
      if (state.dump === 'fails') done(1);
      done(0, state.dump === 'cut off' ? '-- PostgreSQL database dump' : '-- PostgreSQL database dump\nCREATE TABLE t ();\n-- PostgreSQL database dump complete\n');
    }
    if (args[1] !== 'up') done(0);
    if (state.upFails) done(1);
    for (const name of ['backend', 'frontend']) {
      const id = state.images[`${state.prefix}-${name}:${process.env.PKGDIFF_IMAGE_TAG}`];
      if (!id) done(1);
      state.running[name] = id;
    }
    done(0);
    break;
  }
  case 'pull': {
    const ref = args.at(-1);
    if (!state.registry[ref]) done(1);
    state.images[ref] = state.registry[ref];
    done(0, ref);
    break;
  }
  case 'inspect': {
    const id = state.running[service(args[1])];
    done(id ? 0 : 1, id);
    break;
  }
  case 'image': {
    if (args[1] !== 'inspect') done(0);
    const id = state.images[args[2]];
    if (!id) done(1);
    if (format().includes('dev.pkgdiff.rendering')) done(0, (state.rendering ?? []).includes(id) ? 'server' : '');
    done(0, format().includes('Labels') ? `rev-${id}` : id);
    break;
  }
  case 'tag': {
    state.images[args[2]] = state.images[args[1]] ?? args[1];
    done(0);
    break;
  }
  case 'port': {
    const name = service(args[1]);
    done(0, state.ports?.[name] ?? (name === 'backend' ? '127.0.0.1:8080' : '127.0.0.1:8090'));
    break;
  }
  case 'login':
    done(state.loginFails ? 1 : 0);
    break;
  default:
    done(0);
}
