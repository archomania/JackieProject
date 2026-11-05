import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('../wwwroot/js/app.js', import.meta.url), 'utf8');
const stateSource = await readFile(new URL('../wwwroot/js/url-state.js', import.meta.url), 'utf8');
const state = await import('data:text/javascript;base64,' + Buffer.from(stateSource).toString('base64'));
const apiSource = await readFile(new URL('../wwwroot/js/api.js', import.meta.url), 'utf8');
const tick = () => new Promise(resolve => setImmediate(resolve));
const deferred = () => { let resolve, reject; const promise = new Promise((a,b) => {resolve=a; reject=b;}); return {promise,resolve,reject}; };

class Element {
  handlers = new Map();
  disabled = false;
  value = '';
  returnValue = '';
  files = [];
  dataset = {};
  addEventListener(type, callback, options = {}) {
    const list = this.handlers.get(type) ?? [];
    list.push({ callback, once: options.once });
    this.handlers.set(type, list);
  }
  async emit(type, event = {}) {
    const list = [...(this.handlers.get(type) ?? [])];
    this.handlers.set(type, list.filter(item => !item.once));
    await Promise.all(list.map(item => item.callback(event)));
  }
  reset() {}
  focus() {}
  showModal() { this.open = true; }
  async close(value = 'cancel') { this.open = false; this.returnValue = value; await this.emit('close'); }
  click() { return this.emit('click'); }
}

async function app(overrides = {}) {
  const elements = new Map();
  const element = id => {
    if (!elements.has(id)) elements.set(id, new Element());
    return elements.get(id);
  };
  const location = { pathname: '/', search: '?path=folder&q=notes' };
  const renders = [], statuses = [], requests = [];
  const context = vm.createContext({
    ...state, location, AbortController, console,
    document: { getElementById: element, addEventListener() {} },
    window: { addEventListener() {} },
    history: { pushState(_a, _b, url) { const parsed = new URL(url, 'http://localhost'); location.pathname = parsed.pathname; location.search = parsed.search; } },
    getListing: async (s, signal) => { requests.push({ ...s, signal }); return { entries: [], summary: {} }; },
    renderNavigation() {}, clearListing() {}, setBusy() {},
    renderListing(value) { renders.push(value); },
    showStatus(message, error) { statuses.push({ message, error }); },
    createFolder: async () => {}, deleteEntry: async () => {}, uploadFile: async () => {},
    ...overrides,
  });
  vm.runInContext(source.replace(/^import .*;\r?$/gm, ''), context);
  await tick();
  return { context, element, location, renders, statuses, requests };
}

test('URL state round trips Unicode and reserved URL characters', () => {
  const value = { path: 'Reports & notes/日本語 #1 + 100%', q: 'a&b+#' };
  assert.deepEqual(state.readState(state.stateUrl(value).slice(1)), value);
  assert.equal(state.parentPath(value.path), 'Reports & notes');
});

test('upload preserves the active search and refreshes its listing', async () => {
  const ui = await app();
  ui.element('picker').files = [{ name: 'notes.txt', size: 5 }];
  await ui.element('picker').emit('change');
  assert.equal(ui.location.search, '?path=folder&q=notes');
  assert.equal(ui.requests.length, 2);
  assert.equal(ui.requests[1].q, 'notes');
  assert.equal(ui.element('upload').disabled, false);
});

test('oversized uploads fail before any request and reset the picker', async () => {
  let uploads = 0;
  const ui = await app({ uploadFile: async () => { uploads++; } });
  ui.element('picker').files = [{ name: 'large.bin', size: 10 * 1024 * 1024 + 1 }];
  await ui.element('picker').emit('change');
  assert.equal(uploads, 0);
  assert.match(ui.statuses.at(-1).message, /10 MiB/);
  assert.equal(ui.element('picker').value, '');
  assert.equal(ui.element('upload').disabled, false);
});

test('late upload failures do not replace another views status', async () => {
  const operation = deferred();
  const ui = await app({ uploadFile: () => operation.promise });
  ui.element('picker').files = [{ name: 'notes.txt', size: 5 }];
  const uploading = ui.element('picker').emit('change');
  ui.location.search = '?path=other';
  await vm.runInContext('load()', ui.context);
  const status = ui.statuses.at(-1);
  operation.reject(new Error('old view failure'));
  await uploading;
  assert.equal(ui.statuses.at(-1), status);
});

test('an aborted older listing cannot overwrite the latest navigation', async () => {
  const oldListing = deferred();
  let calls = 0;
  const ui = await app({ getListing: () => ++calls === 1 ? oldListing.promise : Promise.resolve({ id: 'new' }) });
  ui.location.search = '?path=new';
  await vm.runInContext('load()', ui.context);
  oldListing.resolve({ id: 'old' });
  await tick();
  assert.deepEqual(ui.renders.map(value => value.id), ['new']);
});

test('failed navigation keeps upload and new folder disabled', async () => {
  const ui = await app({ getListing: async () => { throw new Error('Item not found.'); } });
  assert.equal(ui.element('upload').disabled, true);
  assert.equal(ui.element('create-folder').disabled, true);
  assert.equal(ui.statuses.at(-1).message, 'Item not found.');
});

test('cancelled and repeated delete dialogs do not retain old handlers', async () => {
  const ui = await app();
  const first = vm.runInContext('confirmDelete("first")', ui.context);
  await ui.element('delete-dialog').close('cancel');
  assert.equal(await first, false);
  const second = vm.runInContext('confirmDelete("second")', ui.context);
  await ui.element('delete-dialog').close('delete');
  assert.equal(await second, true);
  assert.equal(ui.element('delete-dialog').handlers.get('close').length, 0);
});

test('API mutations give readable network errors and support empty DELETE responses', async () => {
  const context = vm.createContext({ URLSearchParams, FormData, fetch: async () => { throw new TypeError('Failed to fetch'); } });
  vm.runInContext(apiSource.replace(/export /g, ''), context);
  await assert.rejects(vm.runInContext('deleteEntry("file")', context), /Cannot connect to the server/);
  context.fetch = async () => ({ ok: true, status: 204, json() { throw new Error('Must not parse empty response'); } });
  assert.equal(await vm.runInContext('deleteEntry("file")', context), undefined);
});

test('late delete success does not reload a different view', async () => {
  const operation = deferred();
  const ui = await app({ deleteEntry: () => operation.promise });
  const button = new Element();
  button.dataset = { deletePath: 'folder/file', deleteName: 'file' };
  ui.context.testButton = button;
  const removing = vm.runInContext('removeEntry(testButton)', ui.context);
  await ui.element('delete-dialog').close('delete');
  await tick();
  ui.location.search = '?path=other';
  await vm.runInContext('load()', ui.context);
  const requests = ui.requests.length;
  operation.resolve();
  await removing;
  assert.equal(ui.requests.length, requests);
});

test('new folder cancel does not create anything and re-enables the control', async () => {
  let created = 0;
  const ui = await app({ createFolder: async () => { created++; } });
  const creating = ui.element('create-folder').emit('click');
  await ui.element('folder-dialog').close('cancel');
  await creating;
  assert.equal(created, 0);
  assert.equal(ui.element('create-folder').disabled, false);
});
