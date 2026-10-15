// 은행 모드 확장 — 서비스 워커
// 앱(BankMode.exe --native-host)에 status / on / off 를 보내고, 결과를 storage.session에 둬서 팝업이 그린다.
// 켜기·끄기는 팝업이 아니라 여기서 실행한다: 관리자 확인 창이 뜨면 팝업이 닫히기 때문.
// 평소에 앱을 주기적으로 부르지 않는다 — 팝업을 열 때, 은행 사이트에 들어갈 때만 상태를 확인한다.
import { HOST, getSites, siteFor, friendlyError, isBusy } from './common.js';

const STALE_MS = 15 * 1000;   // 은행 사이트에 들어갈 때 이보다 오래된 상태면 다시 확인

const COLOR = { on: '#16A34A', warn: '#D97706', busy: '#2563EB', gray: '#6B7280' };

// ---------- 상태 저장 ----------

async function getState() {
  const { state } = await chrome.storage.session.get('state');
  return state || {};
}

// 읽고-고치고-쓰기가 겹치지 않도록 한 줄로 세운다
let chain = Promise.resolve();
function updateState(change) {
  const run = chain.then(async () => {
    const cur = await getState();
    const next = { ...cur, ...(typeof change === 'function' ? change(cur) : change) };
    await chrome.storage.session.set({ state: next });
    return next;
  });
  chain = run.catch(() => {});
  return run;
}

async function saveResult(res) {
  if (Array.isArray(res.sites)) await chrome.storage.local.set({ sites: res.sites });
  await updateState({ status: res.status || null, warning: res.warning || null, error: null, checkedAt: Date.now() });
}

// ---------- 앱과 통신 ----------

// connectNative로 보낸다: 연결이 열려 있는 동안 서비스 워커가 잠들지 않아 오래 걸리는 켜기·끄기에도 안전
function callHost(cmd, onProgress) {
  return new Promise((resolve, reject) => {
    let port;
    let settled = false;
    try {
      port = chrome.runtime.connectNative(HOST);
    } catch (e) {
      reject(e);
      return;
    }
    port.onMessage.addListener((msg) => {
      if (msg && msg.type === 'progress') {
        if (onProgress) onProgress(msg.line);
        return;
      }
      settled = true;
      port.disconnect();
      resolve(msg || {});
    });
    port.onDisconnect.addListener(() => {
      if (settled) return;
      settled = true;
      const err = chrome.runtime.lastError;
      reject(new Error((err && err.message) || 'Native host has exited.'));
    });
    port.postMessage({ cmd, id: Date.now() });
  });
}

let inflight = null;

async function refreshStatus(force) {
  const st = await getState();
  if (isBusy(st)) return st;   // 켜고 끄는 중이면 곧 결과가 온다
  if (!force && st.checkedAt && Date.now() - st.checkedAt < STALE_MS) return st;
  if (!inflight) {
    inflight = (async () => {
      try {
        const res = await callHost('status');
        if (res.ok === false) throw new Error(res.error || '상태를 확인하지 못했어요.');
        await saveResult(res);
      } catch (e) {
        await updateState({ status: null, error: friendlyError(e), checkedAt: Date.now() });
      }
      await refreshBadges();
    })().finally(() => {
      inflight = null;
    });
  }
  await inflight;
  return getState();
}

async function runOp(cmd) {
  let started = false;
  await updateState((s) => {
    if (isBusy(s)) return {};   // 두 번 눌러도 한 번만
    started = true;
    return { busy: { cmd, startedAt: Date.now(), line: '' }, last: null };
  });
  if (!started) return;
  await refreshBadges();
  let last;
  try {
    const res = await callHost(cmd, (line) => {
      updateState((s) => (s.busy ? { busy: { ...s.busy, line: String(line || '').trim() } } : {}));
    });
    if (res.status) await saveResult(res);
    last = { cmd, ok: !!res.ok, message: res.message || res.error || '', failures: res.failures || [], at: Date.now() };
  } catch (e) {
    last = { cmd, ok: false, message: friendlyError(e), failures: [], at: Date.now() };
  }
  await updateState({ busy: null, last });
  await refreshBadges();
}

// ---------- 배지 ----------

function badgeFor(st, site) {
  const s = st.status;
  if (isBusy(st)) {
    return { text: '…', color: COLOR.busy, title: st.busy.cmd === 'on' ? '은행 모드 켜는 중…' : '은행 모드 끄는 중…' };
  }
  if (s && s.on) {
    return { text: 'ON', color: COLOR.on, title: `은행 모드 켜짐 — ${s.running}개 실행 중 · 메모리 ${s.memoryMB} MB` };
  }
  if (site && s) {
    return { text: '!', color: COLOR.warn, title: `${site.name}: 은행 모드가 꺼져 있어요 — 눌러서 켜기` };
  }
  if (site && st.error) {
    return { text: '?', color: COLOR.gray, title: `${site.name}: ${st.error}` };
  }
  return { text: '', color: COLOR.gray, title: s ? '은행 모드 꺼짐' : '은행 모드' };
}

async function setBadge(tabId, b) {
  const opt = tabId === undefined ? {} : { tabId };
  try {
    await Promise.all([
      chrome.action.setBadgeText({ ...opt, text: b.text }),
      chrome.action.setBadgeBackgroundColor({ ...opt, color: b.color }),
      chrome.action.setTitle({ ...opt, title: b.title }),
    ]);
  } catch {
    // 그 사이 탭이 닫힘
  }
}

async function badgeTab(tab, st, sites) {
  if (!tab || tab.id === undefined || tab.id < 0) return;
  await setBadge(tab.id, badgeFor(st, siteFor(tab.url, sites)));
}

async function refreshBadges() {
  const [st, sites, tabs] = await Promise.all([getState(), getSites(), chrome.tabs.query({})]);
  await setBadge(undefined, badgeFor(st, null));   // 새로 여는 탭의 기본값
  await Promise.all(tabs.map((t) => badgeTab(t, st, sites)));
}

async function onTabChanged(tab) {
  if (!tab) return;
  const sites = await getSites();
  await badgeTab(tab, await getState(), sites);   // 아는 상태로 먼저 표시
  if (siteFor(tab.url, sites)) await refreshStatus(false);   // 은행 사이트면 상태를 새로 확인 (배지도 다시 그림)
}

// ---------- 이벤트 ----------

chrome.tabs.onUpdated.addListener((tabId, info, tab) => {
  if (info.url || info.status === 'complete') onTabChanged(tab);
});

chrome.tabs.onActivated.addListener(({ tabId }) => {
  chrome.tabs.get(tabId).then(onTabChanged, () => {});
});

chrome.runtime.onInstalled.addListener(() => refreshBadges());
chrome.runtime.onStartup.addListener(() => refreshBadges());

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (sender.id !== chrome.runtime.id || !msg) return;
  if (msg.type === 'refresh') {
    refreshStatus(!!msg.force).then(sendResponse, () => sendResponse(null));
    return true;   // 비동기로 답함
  }
  if (msg.type === 'run' && (msg.cmd === 'on' || msg.cmd === 'off')) {
    runOp(msg.cmd);
    sendResponse({ started: true });
  }
});
