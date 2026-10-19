#!/usr/bin/env node
// Stand-in for the npm CLI in the gate tests. It answers `npm audit --json` from two fixture
// reports and applies npm's own rules for what an audit covers: devDependencies are left out when
// `omit` contains dev (NODE_ENV=production makes that the default), unless `include` names them.
// A command-line flag replaces the value from the environment, as in npm.
import { appendFileSync, readFileSync } from 'node:fs';

const args = process.argv.slice(2);

function fromFlag(name) {
  const values = args.filter((arg) => arg.startsWith(`--${name}=`)).map((arg) => arg.slice(name.length + 3));
  return values.length > 0 ? values : null;
}

function fromEnvironment(name) {
  const entry = Object.entries(process.env).find(([key]) => key.toLowerCase() === `npm_config_${name}`);
  return entry && entry[1] !== '' ? entry[1].split(/[\s,]+/).filter(Boolean) : null;
}

if (args[0] === '--version') {
  console.log('0.0.0-fake');
  process.exit(0);
}

const omit = fromFlag('omit') ?? fromEnvironment('omit') ?? (process.env.NODE_ENV === 'production' ? ['dev'] : []);
const include = fromFlag('include') ?? fromEnvironment('include') ?? [];
const devAudited = !omit.includes('dev') || include.includes('dev');

if (process.env.FAKE_NPM_LOG) {
  appendFileSync(process.env.FAKE_NPM_LOG, `${JSON.stringify({ args, devAudited })}\n`);
}

if (args[0] !== 'audit' || !args.includes('--json')) {
  console.error(`fake npm: only "audit --json" is supported, got: ${args.join(' ')}`);
  process.exit(64);
}

const text = readFileSync(devAudited ? process.env.FAKE_NPM_FULL_REPORT : process.env.FAKE_NPM_PROD_REPORT, 'utf8');
process.stdout.write(text);
process.exitCode = JSON.parse(text).metadata.vulnerabilities.total > 0 ? 1 : 0;
