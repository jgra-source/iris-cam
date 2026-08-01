// Checks the phone page's reconnect logic.
//
//   node Tests/phone-reconnect.mjs
//
// iOS takes the camera away whenever the phone locks or Safari leaves the
// front, so the page has to notice and put itself back together. Testing that
// by hand means locking and unlocking a real iPhone over and over, and the
// interesting cases - a PC that is switched off, a camera Safari refuses to
// hand back - are tedious to stage at all.
//
// So the page's script is loaded here with the browser stubbed out: fake
// sockets that can be dropped on demand, a fake camera whose track can be
// ended, and timers that are inspected rather than waited for. That makes the
// whole state machine checkable in milliseconds.
//
// Nothing is installed for this. It uses only what ships with Node, and the C#
// tests do not depend on it, so a machine without Node simply skips it.
import fs from 'node:fs';
import path from 'node:path';
import url from 'node:url';
import vm from 'node:vm';
import assert from 'node:assert';

const here = path.dirname(url.fileURLToPath(import.meta.url));
const HTML = process.argv[2]
  ?? path.join(here, '..', 'Windows-Client', 'wwwroot', 'index.html');
const src = fs.readFileSync(HTML, 'utf8')
  .match(/<script[^>]*>([\s\S]*?)<\/script>/)[1];

// ---- stubs ---------------------------------------------------------------

class FakeClassList {
  constructor() { this.set = new Set(); }
  toggle(n, force) { force ? this.set.add(n) : this.set.delete(n); }
  add(n) { this.set.add(n); }
  remove(n) { this.set.delete(n); }
  contains(n) { return this.set.has(n); }
}

function el() {
  return { style: {}, classList: new FakeClassList(), textContent: '',
           onclick: null, srcObject: null, readyState: 4, videoWidth: 1280,
           videoHeight: 720, play: async () => {}, getContext: () => fakeCtx,
           captureStream: () => new FakeStream('canvas') };
}

const fakeCtx = { save(){}, restore(){}, translate(){}, rotate(){}, drawImage(){} };

class FakeTrack {
  constructor(kind) { this.kind = kind; this.readyState = 'live'; this.onended = null; }
  stop() { this.readyState = 'ended'; }
  end() { this.readyState = 'ended'; if (this.onended) this.onended(); }
}

class FakeStream {
  constructor(tag) { this.tag = tag; this.tracks = [new FakeTrack('video')]; }
  getTracks() { return this.tracks; }
  getVideoTracks() { return this.tracks.filter(t => t.kind === 'video'); }
}

const sockets = [];
class FakeWebSocket {
  static CONNECTING = 0; static OPEN = 1; static CLOSING = 2; static CLOSED = 3;
  constructor(url) {
    this.url = url; this.readyState = 0; this.sent = [];
    sockets.push(this);
  }
  open()  { this.readyState = 1; this.onopen && this.onopen(); }
  recv(o) { this.onmessage && this.onmessage({ data: JSON.stringify(o) }); }
  drop()  { this.readyState = 3; this.onclose && this.onclose(); }
  send(d) { this.sent.push(JSON.parse(d)); }
  close() { if (this.readyState !== 3) this.drop(); }
}

const peers = [];
class FakeRTCPeerConnection {
  constructor() {
    this.connectionState = 'new'; this.senders = []; this.closed = false;
    peers.push(this);
  }
  addTrack(t) { this.senders.push({ track: t, replaceTrack: async () => {} }); }
  getSenders() { return this.senders; }
  async createOffer() { return { type: 'offer', sdp: 'SDP' }; }
  async setLocalDescription(d) { this.localDescription = d; }
  async setRemoteDescription() {}
  async addIceCandidate() {}
  close() { this.closed = true; }
  go(state) { this.connectionState = state; this.onconnectionstatechange && this.onconnectionstatechange(); }
}

// Timers are collected rather than run, so backoff can be inspected instantly.
const timers = [];
let gumFails = false, gumCalls = 0;

const ctx = {
  console,
  document: {
    getElementById: () => el(),
    addEventListener: (n, f) => { ctx.document['on' + n] = f; },
    hidden: false,
  },
  window: { addEventListener: (n, f) => { ctx.window['on' + n] = f; } },
  location: { protocol: 'https:', host: 'pc:9443' },
  navigator: { mediaDevices: { getUserMedia: async () => {
    gumCalls++;
    if (gumFails) throw new Error('NotAllowedError');
    return new FakeStream('camera');
  } } },
  WebSocket: FakeWebSocket,
  RTCPeerConnection: FakeRTCPeerConnection,
  RTCSessionDescription: class { constructor(o) { Object.assign(this, o); } },
  RTCIceCandidate: class { constructor(o) { Object.assign(this, o); } },
  requestAnimationFrame: () => 1,
  cancelAnimationFrame: () => {},
  setTimeout: (fn, ms) => { timers.push({ fn, ms }); return timers.length; },
  clearTimeout: (id) => { if (timers[id - 1]) timers[id - 1].cancelled = true; },
  performance: { now: () => Date.now() },
};
ctx.globalThis = ctx;
vm.createContext(ctx);
vm.runInContext(src, ctx);

// ---- helpers -------------------------------------------------------------

const tick = () => new Promise(r => setImmediate(r));
const lastSocket = () => sockets[sockets.length - 1];
// `let`/`const` at the top of the script land in the context's lexical scope,
// not on the context object, so they have to be read by evaluating them there.
const get = (expr) => vm.runInContext(expr, ctx);
const pending = () => timers.filter(t => !t.cancelled && !t.fired);
function fire(t) { t.fired = true; t.fn(); }

let failures = 0;
async function check(name, fn) {
  try { await fn(); console.log('  ok   ' + name); }
  catch (e) { failures++; console.log('  FAIL ' + name + '\n       ' + e.message); }
}

// ---- the tests -----------------------------------------------------------

console.log('\nphone reconnect logic\n');

await ctx.start();
await tick();
const s1 = lastSocket();
s1.open();
await tick();

await check('start opens a signalling socket and a peer connection', () => {
  assert.equal(sockets.length, 1);
  assert.equal(peers.length, 1);
  assert.equal(get('running'), true);
});

await check('no offer is sent until the PC says it is ready', () => {
  assert.equal(s1.sent.length, 0);
});

await check('viewer-ready produces an offer', async () => {
  s1.recv({ type: 'viewer-ready' });
  await tick();
  assert.equal(s1.sent.filter(m => m.type === 'offer').length, 1);
});

// The PC is switched off: every attempt fails to open, so the waits must grow
// rather than hammering it once a second all day.
await check('repeated failures back off 1s, 2s, 4s, then cap at 5s', () => {
  const seen = [];
  lastSocket().drop();
  for (let i = 0; i < 5; i++) {
    const t = pending().pop();
    seen.push(t.ms);
    fire(t);                       // connect() opens a socket...
    lastSocket().drop();           // ...which never comes up
  }
  assert.deepEqual(seen, [1000, 2000, 4000, 5000, 5000]);
});

await check('a connection that succeeds resets the backoff to 1s', () => {
  fire(pending().pop());
  lastSocket().open();             // the PC is back
  lastSocket().drop();
  assert.equal(pending().pop().ms, 1000);
});

await check('each reconnect gets a brand new peer connection', () => {
  const before = peers.length;
  fire(pending().pop());
  lastSocket().open();
  assert.equal(peers.length, before + 1);
  assert.equal(peers[before - 1].closed, true, 'the old peer should be closed');
});

await check('resume() leaves a healthy stream completely alone', async () => {
  lastSocket().readyState = 1;
  peers[peers.length - 1].go('connected');
  const socketsBefore = sockets.length, peersBefore = peers.length;
  ctx.resume();
  await tick();
  assert.equal(sockets.length, socketsBefore, 'opened a socket it did not need');
  assert.equal(peers.length, peersBefore, 'rebuilt a working peer connection');
});

// Two senders on one slot means the PC relays to whichever registered last,
// and the other one streams into nothing.
await check('connect() refuses to open a second socket over a live one', () => {
  lastSocket().readyState = 1;
  const before = sockets.length;
  ctx.connect();
  ctx.connect();
  assert.equal(sockets.length, before);
});

await check('connect() also refuses while one is still coming up', () => {
  lastSocket().readyState = 0;         // CONNECTING
  const before = sockets.length;
  ctx.connect();
  assert.equal(sockets.length, before);
});

await check('resume() rebuilds when the socket is dead', async () => {
  lastSocket().readyState = 3;
  const before = sockets.length;
  ctx.resume();
  await tick();
  assert.equal(sockets.length, before + 1);
});

await check('a failed peer connection triggers a rebuild', async () => {
  lastSocket().open();
  await tick();
  const before = peers.length;
  peers[peers.length - 1].go('failed');
  await tick();
  assert.equal(peers.length, before + 1);
});

await check('the camera is re-acquired when iOS ends the track', async () => {
  const calls = gumCalls;
  get('rawStream').getVideoTracks()[0].end();
  await tick(); await tick();
  assert.equal(gumCalls, calls + 1, 'getUserMedia should have been called again');
});

await check('a camera refused while backgrounded offers a tap to retry', async () => {
  gumFails = true;
  get('rawStream').getVideoTracks()[0].end();
  await tick(); await tick();
  const status = get('statusEl');
  assert.ok(status.classList.contains('retry'),
            'status should be tappable, was: ' + status.textContent);
  gumFails = false;
});

// Asserts the outcome rather than the bookkeeping: let every outstanding timer
// run and check nothing crawls back to life.
await check('Stop really stops - nothing reconnects afterwards', () => {
  ctx.stop();
  lastSocket().drop();
  const before = sockets.length;
  pending().forEach(fire);
  assert.equal(get('running'), false);
  assert.equal(sockets.length, before, 'something reopened a socket after Stop');
});

await check('a socket that closes after Stop does not revive anything', async () => {
  const before = sockets.length;
  ctx.resume();
  await tick();
  assert.equal(sockets.length, before);
});

console.log(failures === 0 ? '\nall passed\n' : `\n${failures} FAILED\n`);
process.exit(failures === 0 ? 0 : 1);
