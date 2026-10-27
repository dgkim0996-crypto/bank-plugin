// 은행 모드 확장 — 팝업. 상태는 서비스 워커가 storage.session에 둔 것을 그리기만 한다
import { getSites, siteFor, sitePermissionPattern, sitePermissionIsStatic, isBusy } from './common.js';

const $ = (id) => document.getElementById(id);
const RECENT_MS = 10 * 60 * 1000;   // 이보다 오래된 켜기·끄기 결과는 보여 주지 않음

let tab = null;
let site = null;
let state = {};
let sitePermissions = [];
let permissionMessage = '';

function render() {
  const st = state;
  const s = st.status;
  const busy = isBusy(st);

  // 상태 줄
  const dot = $('dot');
  dot.className = 'dot' + (busy ? ' busy' : s && s.partial ? ' partial' : s && s.on ? ' on' : '');
  let title;
  let sub = '';
  if (busy) {
    title = st.busy.cmd === 'on' ? '켜는 중…' : '끄는 중…';
    sub = st.busy.line || '잠시만 기다려 주세요';
  } else if (s) {
    if (s.managed === 0) {
      title = '관리할 프로그램 없음';
      sub = s.found === 0 ? '이 PC에서 은행 보안 프로그램을 찾지 못했어요.' : '앱 목록에서 관리할 프로그램을 체크하세요.';
    } else if (s.on) {
      title = s.partial ? '일부 켜짐' : '켜짐';
      sub = `${s.running}/${s.managed}개 모듈 실행 중 · 메모리 ${s.memoryMB} MB 사용`;
    } else {
      title = '꺼짐';
      sub = '은행 보안 프로그램이 모두 쉬고 있어요';
    }
    if (s.bootAuto > 0) sub += `\n부팅할 때 저절로 켜지는 프로그램 ${s.bootAuto}개 — '끄기'를 누르면 막아 둬요`;
  } else if (st.error) {
    title = '앱과 연결되지 않음';
  } else {
    title = '확인 중…';
  }
  $('state').textContent = title;
  $('sub').textContent = sub;
  $('sub').hidden = !sub;

  // 은행 사이트인데 꺼져 있으면 켤지 묻는다
  const ask = !!(site && s && !s.on && s.managed > 0 && !busy);
  $('prompt').hidden = !ask;
  $('promptSite').textContent = ask ? `${site.name} 사이트예요` : '';

  $('on').disabled = busy || !s;
  $('off').disabled = busy || !s;
  $('hint').hidden = !busy;

  // 지난 켜기·끄기 결과
  const last = st.last;
  const showLast = !busy && last && Date.now() - last.at < RECENT_MS && last.message;
  $('result').hidden = !showLast;
  if (showLast) {
    $('result').className = 'result ' + (last.ok ? 'ok' : 'fail');
    $('resultMsg').textContent = last.message;
    const ul = $('failures');
    ul.replaceChildren(
      ...(last.failures || []).slice(0, 6).map((f) => {
        const li = document.createElement('li');
        li.textContent = f;
        return li;
      }),
    );
    // 켠 다음 은행 사이트를 새로고침해야 보안 프로그램을 다시 찾는다
    $('reload').hidden = !(last.cmd === 'on' && site && s && s.on);
  }

  // 앱과 연결 안 됨
  $('setup').hidden = !(st.error && !s && !busy);
  $('setupMsg').textContent = st.error || '';

  $('warning').hidden = !st.warning;
  $('warning').textContent = st.warning || '';
  $('siteAccess').hidden = sitePermissions.length === 0;
  $('siteAccessSummary').textContent = `사이트별 자동 배지 권한 (${sitePermissions.length})`;
  $('siteAccessMsg').textContent = permissionMessage || '사이트별로 자동 배지 접근 권한을 관리할 수 있어요.';
}

async function refreshSitePermissions() {
  const sites = await getSites();
  const unique = new Map();
  for (const site of sites) {
    const pattern = sitePermissionPattern(site);
    if (!pattern || unique.has(pattern)) continue;
    unique.set(pattern, { site, orphan: false });
  }
  // Keep optional grants visible after a site is removed from the app's catalog,
  // and fixed manifest grants visible when the app catalog no longer uses them.
  try {
    const required = new Set(chrome.runtime.getManifest().host_permissions || []);
    for (const pattern of required) {
      const match = /^\*:\/\/\*\.([a-z0-9.-]+)\/\*$/.exec(pattern);
      if (!match) continue;
      const candidate = { name: '앱 목록에서 삭제됨', url: `https://${match[1]}/` };
      const stillRegistered = [...unique.values()].some((entry) => sitePermissionIsStatic(entry.site, [pattern]));
      if (!stillRegistered) unique.set(pattern, { site: candidate, orphan: true, fixed: true });
    }
    const granted = await chrome.permissions.getAll();
    for (const pattern of granted.origins || []) {
      const siteUrl = pattern.replace(/^https?:\/\/\*\./, (scheme) => scheme.replace('://*.', '://')).replace(/\/\*$/, '/');
      const candidateSite = { url: siteUrl };
      if (required.has(pattern) || sitePermissionIsStatic(candidateSite, [...required]) || unique.has(pattern) ||
        !/^https?:\/\/\*\.[a-z0-9.-]+\/\*$/.test(pattern)) continue;
      unique.set(pattern, { site: { name: '등록 목록에서 삭제됨', url: '' }, orphan: true, fixed: false });
    }
  } catch {
    permissionMessage = '현재 사이트 권한을 모두 확인하지 못했어요.';
  }
  sitePermissions = await Promise.all([...unique].map(async ([pattern, entry]) => {
    let allowed = false;
    let error = '';
    if (entry.orphan) {
      allowed = true;
    } else {
      try {
        allowed = await chrome.permissions.contains({ origins: [pattern] });
      } catch {
        error = '권한 상태를 확인하지 못했어요.';
      }
    }
    return { site: entry.site, pattern, allowed, fixed: !!entry.fixed || (!entry.orphan && sitePermissionIsStatic(entry.site)), orphan: entry.orphan, error };
  }));
  renderSitePermissions();
  render();
}

function renderSitePermissions() {
  const list = $('sitePermissionList');
  list.replaceChildren();
  for (const [index, item] of sitePermissions.entries()) {
    const row = document.createElement('li');
    row.className = 'site-row';
    const detail = document.createElement('div');
    detail.className = 'site-detail';
    const name = document.createElement('strong');
    name.textContent = item.site.name || new URL(item.site.url).hostname;
    const host = document.createElement('span');
    host.textContent = item.pattern.replace(/^(\*|https?):\/\//, '').replace(/\/\*$/, '').replace(/^\*\./, '');
    const status = document.createElement('span');
    status.className = 'site-permission-status';
    status.textContent = item.error || (item.orphan && item.fixed
      ? '앱 목록에서 삭제됨 · 기본 권한은 확장 업데이트 전까지 유지'
      : item.orphan ? '앱 목록에서 삭제됨 · 선택 권한 유지 중' : item.fixed ? '기본 허용' : item.allowed ? '허용됨' : '허용 안 됨');
    detail.append(name, host, status);

    const button = document.createElement('button');
    button.className = 'permission-button';
    button.dataset.index = String(index);
    if (item.fixed) {
      button.textContent = '기본 권한';
      button.disabled = true;
      button.title = item.orphan ? '이 사이트는 앱 목록에서 삭제됐지만 확장 프로그램의 기본 권한에 포함돼 있어, 확장 업데이트 전에는 권한을 해제할 수 없어요.' : '확장 프로그램의 기본 권한으로 사용 중인 사이트예요.';
    } else if (item.allowed) {
      button.textContent = '해제';
      button.dataset.action = 'remove';
      button.setAttribute('aria-label', `${item.orphan ? host.textContent : item.site.name || item.pattern} 사이트 접근 권한 해제`);
    } else {
      button.textContent = '허용';
      button.dataset.action = 'request';
      button.setAttribute('aria-label', `${item.site.name || item.pattern} 사이트 접근 권한 허용`);
    }
    row.append(detail, button);
    list.append(row);
  }
}

async function changeSitePermission(button) {
  const item = sitePermissions[Number(button.dataset.index)];
  if (!item || item.fixed) return;
  button.disabled = true;
  permissionMessage = '';
  let succeeded = false;
  try {
    if (button.dataset.action === 'request') {
      // Call request directly from the click handler to preserve Chrome's user gesture.
      succeeded = await chrome.permissions.request({ origins: [item.pattern] });
      permissionMessage = succeeded
        ? `${item.site.name || '사이트'} 접근을 허용했어요.`
        : '권한을 허용하지 않았어요. 자동 배지는 표시되지 않지만 확장 버튼으로 기능을 사용할 수 있어요.';
    } else {
      succeeded = await chrome.permissions.remove({ origins: [item.pattern] });
      permissionMessage = succeeded
        ? `${item.orphan ? item.pattern : item.site.name || '사이트'} 접근 권한을 해제했어요.`
        : '권한을 해제하지 못했어요. 이미 해제했거나 기본 권한으로 제공되는 사이트인지 확인해 주세요.';
    }
  } catch {
    permissionMessage = button.dataset.action === 'request'
      ? '권한 요청에 실패했어요. 사이트 주소 형식과 확장 설정을 확인해 주세요.'
      : '권한 해제에 실패했어요. 잠시 후 다시 시도해 주세요.';
  }
  await refreshSitePermissions();
  if (succeeded) chrome.runtime.sendMessage({ type: 'refresh', force: true }).catch(() => {});
}

async function init() {
  [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  site = tab ? siteFor(tab.url, await getSites()) : null;
  await refreshSitePermissions();
  const got = await chrome.storage.session.get('state');
  state = got.state || {};
  render();

  chrome.storage.onChanged.addListener(async (changes, area) => {
    if (area === 'session' && changes.state) {
      state = changes.state.newValue || {};
      render();
    }
    if (area === 'local' && changes.sites && tab) {
      site = siteFor(tab.url, await getSites());
      await refreshSitePermissions();
      render();
    }
  });
  chrome.permissions.onAdded.addListener(() => refreshSitePermissions());
  chrome.permissions.onRemoved.addListener(() => refreshSitePermissions());

  const send = (msg) => chrome.runtime.sendMessage(msg).catch(() => {});   // 팝업이 먼저 닫혀도 괜찮음
  $('on').addEventListener('click', () => send({ type: 'run', cmd: 'on' }));
  $('off').addEventListener('click', () => send({ type: 'run', cmd: 'off' }));
  $('reload').addEventListener('click', async () => {
    if (tab) await chrome.tabs.reload(tab.id);
    window.close();
  });
  $('sitePermissionList').addEventListener('click', (event) => {
    const button = event.target.closest('button[data-action]');
    if (button) changeSitePermission(button);
  });

  // 열 때마다 앱에 지금 상태를 물어본다 (결과는 storage 변경으로 들어옴)
  send({ type: 'refresh', force: true });
  setInterval(render, 30 * 1000);   // '켜는 중' 시간 초과 처리 등
}

init();
