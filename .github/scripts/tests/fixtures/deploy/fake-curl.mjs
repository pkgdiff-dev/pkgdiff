// Stand-in for curl in .github/scripts/tests/deploy.test.mjs: a service answers unless the image its
// container runs is listed in state.unhealthy. With -w it prints the status code instead of failing;
// an image listed in state.legacy has no /health/ready and answers 404 there. Asked for a page
// (no -o), the frontend prints one: rendered on the server when its image is listed in
// state.rendering and the render server is not listed as down in state.renderDown.
import { readFileSync } from 'node:fs';

const state = JSON.parse(readFileSync(process.env.FAKE_DOCKER_STATE, 'utf8'));
const args = process.argv.slice(2);
const url = args.at(-1);
const image = state.running[url.includes(':8080') ? 'backend' : 'frontend'];
const healthy = !state.unhealthy.includes(image);

if (args.includes('-w')) {
  const isLegacy = (state.legacy ?? []).includes(image) && url.includes('/health/ready');
  process.stdout.write(isLegacy ? '404' : healthy ? '200' : '503');
  process.exit(0);
}

if (healthy && !args.includes('-o')) {
  const rendered = (state.rendering ?? []).includes(image) && !(state.renderDown ?? []).includes(image);
  process.stdout.write(rendered ? '<html><body><app-root ng-server-context="ssr"></app-root></body></html>\n' : '<html><body><app-root></app-root></body></html>\n');
}

process.exit(healthy ? 0 : 22);
