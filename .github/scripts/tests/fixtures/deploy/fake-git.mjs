// Stand-in for git in .github/scripts/tests/deploy.test.mjs: records the call in FAKE_DOCKER_STATE and
// fails when state.gitFails is set.
import { readFileSync, writeFileSync } from 'node:fs';

const file = process.env.FAKE_DOCKER_STATE;
const state = JSON.parse(readFileSync(file, 'utf8'));
state.calls.push(`git ${process.argv.slice(2).join(' ')}`);
writeFileSync(file, JSON.stringify(state));
process.exit(state.gitFails ? 1 : 0);
