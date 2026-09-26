// Oracle generator for @parse/node-apn.
//
// Sends every scenario through a real node-apn Provider to a local HTTP/2 TLS server and records the request
// exactly as it arrived on the wire, so the fixture reflects node-apn's own header and payload building rather
// than a transcription of it.

import { createRequire } from 'node:module';
import { readFileSync, writeFileSync } from 'node:fs';
import http2 from 'node:http2';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const apn = require('@parse/node-apn');
const { version } = require('@parse/node-apn/package.json');

const here = path.dirname(fileURLToPath(import.meta.url));
const oraclesDir = path.resolve(here, '..');
const repoRoot = path.resolve(oraclesDir, '..', '..');
const scenariosPath = path.join(oraclesDir, 'scenarios.json');
const outPath = path.join(
  repoRoot,
  'tests/Headless.PushNotifications.Apns.Tests.Unit/CrossLibrary/Fixtures/node-apn.json'
);
const broadcastOutPath = path.join(
  repoRoot,
  'tests/Headless.PushNotifications.Apns.Tests.Unit/CrossLibrary/Fixtures/node-apn-broadcast.json'
);

const HEADER_NAMES = [
  'apns-push-type',
  'apns-topic',
  'apns-priority',
  'apns-expiration',
  'apns-collapse-id',
  'apns-id',
];

const spec = JSON.parse(readFileSync(scenariosPath, 'utf8'));

// node-apn exposes one setter per Apple key; a key without a setter is something this library cannot express.
const APS_SETTERS = {
  badge: (n, v) => (n.badge = v),
  sound: (n, v) => (n.sound = v),
  'content-available': (n, v) => (n.contentAvailable = v),
  'mutable-content': (n, v) => (n.mutableContent = v),
  'content-changed': (n, v) => (n.contentChanged = v),
  'thread-id': (n, v) => (n.threadId = v),
  category: (n, v) => (n.category = v),
  'target-content-id': (n, v) => (n.targetContentIdentifier = v),
  'interruption-level': (n, v) => (n.interruptionLevel = v),
  'relevance-score': (n, v) => (n.relevanceScore = v),
  timestamp: (n, v) => (n.timestamp = v),
  event: (n, v) => (n.event = v),
  'stale-date': (n, v) => (n.staleDate = v),
  'dismissal-date': (n, v) => (n.dismissalDate = v),
  'content-state': (n, v) => (n.contentState = v),
  'attributes-type': (n, v) => (n.attributesType = v),
  attributes: (n, v) => (n.attributes = v),
};

const ALERT_SETTERS = {
  title: (n, v) => (n.title = v),
  subtitle: (n, v) => (n.subtitle = v),
  body: (n, v) => (n.body = v),
  'title-loc-key': (n, v) => (n.titleLocKey = v),
  'title-loc-args': (n, v) => (n.titleLocArgs = v),
  'loc-key': (n, v) => (n.locKey = v),
  'loc-args': (n, v) => (n.locArgs = v),
  'subtitle-loc-key': (n, v) => (n.subtitleLocKey = v),
  'subtitle-loc-args': (n, v) => (n.subtitleLocArgs = v),
  'launch-image': (n, v) => (n.launchImage = v),
};

class Unsupported extends Error {}

function buildNotification(scenario) {
  const note = new apn.Notification();
  note.topic = scenario.topic;
  note.pushType = scenario.pushType;
  if (scenario.priority !== undefined) note.priority = scenario.priority;
  if (scenario.expiration !== undefined) note.expiry = scenario.expiration;
  if (scenario.collapseId !== undefined) note.collapseId = scenario.collapseId;
  if (scenario.apnsId !== undefined) note.id = scenario.apnsId;

  if (scenario.raw !== undefined) {
    note.rawPayload = scenario.raw;
    return note;
  }

  for (const [key, value] of Object.entries(scenario.aps ?? {})) {
    if (key === 'alert') {
      if (typeof value === 'string') {
        note.alert = value;
        continue;
      }
      for (const [alertKey, alertValue] of Object.entries(value)) {
        const set = ALERT_SETTERS[alertKey];
        if (!set) throw new Unsupported(`no setter for aps.alert.${alertKey}`);
        set(note, alertValue);
      }
      continue;
    }
    const set = APS_SETTERS[key];
    if (!set) throw new Unsupported(`no setter for aps.${key}`);
    set(note, value);
  }

  if (scenario.custom !== undefined) note.payload = structuredClone(scenario.custom);
  return note;
}

function sortKeys(value) {
  if (Array.isArray(value)) return value.map(sortKeys);
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(
      Object.keys(value)
        .sort()
        .map(k => [k, sortKeys(value[k])])
    );
  }
  return value;
}

function decodeJwt(authorization) {
  const token = authorization.replace(/^bearer /i, '');
  const [header, claims] = token
    .split('.')
    .slice(0, 2)
    .map(part => JSON.parse(Buffer.from(part, 'base64url').toString('utf8')));
  return { header, claims };
}

async function main() {
  const key = readFileSync(path.join(oraclesDir, 'test-keys/localhost.TEST-ONLY.key'));
  const cert = readFileSync(path.join(oraclesDir, 'test-keys/localhost.TEST-ONLY.crt'));

  const captures = [];
  const server = http2.createSecureServer({ key, cert, allowHTTP1: false });
  server.on('stream', (stream, headers) => {
    const chunks = [];
    stream.on('data', c => chunks.push(c));
    stream.on('end', () => {
      captures.push({ headers, body: Buffer.concat(chunks).toString('utf8') });
      stream.respond({ ':status': 200, 'apns-id': '00000000-0000-0000-0000-000000000000' });
      stream.end();
    });
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address();

  const provider = new apn.Provider({
    token: {
      key: path.join(oraclesDir, spec.jwt.keyFile),
      keyId: spec.jwt.keyId,
      teamId: spec.jwt.teamId,
    },
    address: 'localhost',
    port,
    ca: cert,
    production: false,
    manageChannelsAddress: 'localhost',
    manageChannelsPort: port,
  });

  const scenarios = [];
  let jwt = null;

  for (const scenario of spec.scenarios) {
    let note;
    try {
      note = buildNotification(scenario);
    } catch (err) {
      if (!(err instanceof Unsupported)) throw err;
      scenarios.push({
        id: scenario.id,
        supported: false,
        unsupportedReason: err.message,
        headers: null,
        payload: null,
      });
      continue;
    }

    const before = captures.length;
    const result = await provider.send(note, spec.deviceToken);
    if (result.failed.length > 0 || captures.length !== before + 1) {
      throw new Error(`scenario ${scenario.id}: send failed ${JSON.stringify(result.failed)}`);
    }
    const captured = captures[before];
    if (captured.headers[':path'] !== `/3/device/${spec.deviceToken}`) {
      throw new Error(`scenario ${scenario.id}: unexpected path ${captured.headers[':path']}`);
    }

    const headers = Object.fromEntries(HEADER_NAMES.map(h => [h, captured.headers[h] ?? null]));
    scenarios.push({
      id: scenario.id,
      supported: true,
      unsupportedReason: null,
      headers,
      // node-apn skips the request body when the compiled payload is "{}"; an empty body is recorded as null.
      payload: captured.body === '' ? null : sortKeys(JSON.parse(captured.body)),
    });

    jwt ??= decodeJwt(captured.headers.authorization);
  }

  const broadcast = await captureBroadcast(provider, captures);

  provider.shutdown();
  server.close();

  // node-apn signs with jsonwebtoken and passes no clock, so iat is always the wall clock; record its type only.
  if (!Number.isInteger(jwt.claims.iat)) throw new Error('node-apn JWT iat is not an integer');
  jwt.claims.iat = '<number>';

  const output = {
    library: '@parse/node-apn',
    version,
    generatedAt: spec.generatedAt,
    scenarios,
    jwt: sortKeys(jwt),
  };
  writeFileSync(outPath, JSON.stringify(output, null, 2) + '\n');
  console.log(`node-apn ${version}: wrote ${scenarios.length} scenarios to ${path.relative(repoRoot, outPath)}`);

  const broadcastOutput = { library: '@parse/node-apn', version, generatedAt: spec.generatedAt, ...broadcast };
  writeFileSync(broadcastOutPath, JSON.stringify(broadcastOutput, null, 2) + '\n');
  console.log(`node-apn ${version}: wrote broadcast and channel requests to ${path.relative(repoRoot, broadcastOutPath)}`);
}

// iOS 18 broadcast and channel management: node-apn is the only surveyed library that implements them. Each call
// goes through the real Provider to the local server, which records the request as it arrived.
const BROADCAST_HEADER_NAMES = [
  'apns-push-type',
  'apns-priority',
  'apns-expiration',
  'apns-channel-id',
  'apns-topic',
  'apns-collapse-id',
];

async function captureBroadcast(provider, captures) {
  const { bundleId, channelId, update } = spec.broadcast;

  async function capture(label, call) {
    const before = captures.length;
    const result = await call();
    if ((result.failed?.length ?? 0) > 0 || captures.length !== before + 1) {
      throw new Error(`${label}: request failed ${JSON.stringify(result.failed)}`);
    }
    const c = captures[before];
    return {
      method: c.headers[':method'],
      path: c.headers[':path'],
      headers: Object.fromEntries(BROADCAST_HEADER_NAMES.map(h => [h, c.headers[h] ?? null])),
      hasRequestId: typeof c.headers['apns-request-id'] === 'string',
      body: c.body === '' ? null : sortKeys(JSON.parse(c.body)),
    };
  }

  // The same update the .NET side broadcasts: priority 5 and expiration 0, which our provider sends by default.
  const note = new apn.Notification();
  note.channelId = channelId;
  note.pushType = 'liveactivity';
  note.priority = 5;
  note.expiry = 0;
  note.requestId = spec.broadcast.requestId;
  for (const [k, v] of Object.entries(update.aps)) APS_SETTERS[k](note, v);
  const send = await capture('broadcast', () => provider.broadcast(note, bundleId));

  const createNote = new apn.Notification();
  createNote.requestId = spec.broadcast.requestId;
  createNote.payload = { 'message-storage-policy': 1 };
  const create = await capture('create', () => provider.manageChannels(createNote, bundleId, 'create'));

  const channelNote = () => {
    const n = new apn.Notification();
    n.requestId = spec.broadcast.requestId;
    n.channelId = channelId;
    return n;
  };
  const read = await capture('read', () => provider.manageChannels(channelNote(), bundleId, 'read'));
  const readAll = await capture('readAll', () => {
    const n = new apn.Notification();
    n.requestId = spec.broadcast.requestId;
    return provider.manageChannels(n, bundleId, 'readAll');
  });
  const del = await capture('delete', () => provider.manageChannels(channelNote(), bundleId, 'delete'));

  return { broadcast: send, channels: { create, read, list: readAll, delete: del } };
}

await main();
