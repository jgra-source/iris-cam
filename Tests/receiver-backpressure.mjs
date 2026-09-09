// Checks the receiver's two-frame limit without connecting to the real camera.
// C# must acknowledge a published frame before the browser can exceed that limit.
// Run with node --test Tests/receiver-backpressure.mjs; no packages are needed.
import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';

const html = fs.readFileSync(new URL('../Windows-Client/wwwroot/receiver.html', import.meta.url), 'utf8');
const source = html.match(/<script[^>]*>([\s\S]*?)<\/script>/)[1];

// Give each test its own sockets and pixels so reconnect cases cannot leak state.
function receiver(videoCallbacks = true) {
  const sockets = [];
  const callbacks = [];
  const timers = [];
  const armedAtRead = [];
  let frameNumber = 1, pixelReads = 0, draws = 0, now = 100;
  const drawing = {
    fillRect() {},
    drawImage() { draws++; },
    getImageData() {
      pixelReads++;
      armedAtRead.push(callbacks.length);
      return { data: new Uint8ClampedArray([frameNumber]) };
    },
  };
  const elements = {
    canvas: { getContext: () => drawing },
    remote: { videoWidth: 1280, videoHeight: 720,
      requestVideoFrameCallback: callback => callbacks.push(callback) },
    pill: {}, stats: {},
  };
  if (!videoCallbacks) delete elements.remote.requestVideoFrameCallback;
  class Socket {
    static OPEN = 1;
    constructor(address) { this.address = address; this.readyState = 0; this.bufferedAmount = 0; this.sent = []; sockets.push(this); }
    send(data) { this.sent.push(data); }
    open() { this.readyState = 1; this.onopen?.(); }
    acknowledge() { this.onmessage?.({ data: 'frame-ready' }); }
    close() { this.readyState = 3; this.onclose?.(); }
  }
  const context = vm.createContext({
    console, document: { getElementById: name => elements[name] },
    location: { protocol: 'https:', host: 'localhost:9443' }, WebSocket: Socket,
    performance: { now: () => now }, setInterval() {},
    requestAnimationFrame: callback => callbacks.push(callback),
    setTimeout: callback => timers.push(callback),
  });
  vm.runInContext(source, context);
  const socket = sockets.find(candidate => candidate.address.endsWith('/ws/frames'));
  socket.open();
  return {
    context, socket, sockets, callbacks, timers,
    push: () => context.pushFrame(),
    setFrame: value => { frameNumber = value; },
    advance: (milliseconds = 34) => { now += milliseconds; },
    reads: () => pixelReads, draws: () => draws,
    armedAtRead,
  };
}

test('only two frames can be outstanding even when the browser socket has drained', () => {
  const page = receiver();
  page.push();
  page.push();
  page.push();
  assert.equal(page.socket.sent.length, 2);
  assert.equal(page.reads(), 2);
  assert.equal(page.draws(), 2);
});

test('acknowledgment sends the newest picture instead of replaying skipped frames', () => {
  const page = receiver();
  page.push();
  page.setFrame(2);
  page.push();
  page.setFrame(3);
  page.push();
  page.setFrame(4);
  page.socket.acknowledge();
  page.push();
  assert.deepEqual(page.socket.sent.map(buffer => new Uint8Array(buffer)[0]), [1, 2, 4]);
});

test('buffered bytes do not defeat the two-frame overlap, but a closed socket does no work', () => {
  const page = receiver();
  page.socket.bufferedAmount = 1;
  page.push();
  page.push();
  page.push();
  assert.equal(page.socket.sent.length, 2);
  page.socket.acknowledge();
  page.socket.close();
  page.push();
  assert.equal(page.reads(), 2);
  assert.equal(page.draws(), 2);
});

test('unrelated messages do not release an outstanding frame', () => {
  const page = receiver();
  page.push();
  page.push();
  page.socket.onmessage({ data: 'unrelated' });
  page.push();
  assert.equal(page.socket.sent.length, 2);
});

test('reconnect resets the wait and ignores late acknowledgments from the old socket', () => {
  const page = receiver();
  page.push();
  page.socket.close();
  page.timers.shift()();
  const replacement = page.sockets.at(-1);
  replacement.open();
  page.push();
  page.push();
  page.socket.acknowledge();
  page.push();
  assert.equal(replacement.sent.length, 2);
  replacement.acknowledge();
  page.push();
  assert.equal(replacement.sent.length, 3);
});

test('extra acknowledgments cannot create more than two credits', () => {
  const page = receiver();
  page.socket.acknowledge();
  page.socket.acknowledge();
  page.push();
  page.push();
  page.push();
  assert.equal(page.socket.sent.length, 2);
});

test('a new video track replaces the old pump instead of starting two senders', () => {
  const page = receiver();
  page.context.startPump();
  const oldCallback = page.callbacks.shift();
  page.socket.acknowledge();
  page.advance();
  page.context.startPump();
  const sent = page.socket.sent.length;
  page.socket.acknowledge();
  page.advance();
  oldCallback();
  assert.equal(page.socket.sent.length, sent);
  assert.equal(page.callbacks.length, 1);
});

test('the next video callback is armed before expensive pixel copying', () => {
  const page = receiver();
  page.context.startPump();
  assert.equal(page.armedAtRead[0], 1);
});

test('distinct decoded frames are not discarded because callbacks arrived close together', () => {
  const page = receiver();
  page.context.startPump();
  page.socket.acknowledge();
  page.advance(10);
  page.callbacks.shift()();
  assert.equal(page.socket.sent.length, 2);
});

test('animation-frame fallback still caps duplicate video copies', () => {
  const page = receiver(false);
  page.context.startPump();
  page.socket.acknowledge();
  page.advance(10);
  page.callbacks.shift()();
  assert.equal(page.socket.sent.length, 1);
  page.advance(24);
  page.callbacks.shift()();
  assert.equal(page.socket.sent.length, 2);
});