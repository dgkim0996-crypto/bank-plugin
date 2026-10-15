// 은행 모드 확장 — 팝업. 상태는 서비스 워커가 storage.session에 둔 것을 그리기만 한다
import { getSites, siteFor, isBusy } from './common.js';

const $ = (id) => document.getElementById(id);
const RECENT_MS = 10 * 60 * 1000;   // 이보다 오래된 켜기·끄기 결과는 보여 주지 않음

let tab = null;
let site = null;
let state = {};

function render() {
  const st = state;
  const s = st.status;
  const busy = isBusy(st);

  // 상태 줄
  const dot = $('dot');
  dot.className = 'dot' + (busy ? ' busy' : s && s.on ? ' on' : '');
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
      title = '켜짐';
      sub = `${s.running}개 실행 중 · 메모리 ${s.memoryMB} MB 사용`;
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
}

async function init() {
  [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  site = tab ? siteFor(tab.url, await getSites()) : null;
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
      render();
    }
  });

  const send = (msg) => chrome.runtime.sendMessage(msg).catch(() => {});   // 팝업이 먼저 닫혀도 괜찮음
  $('on').addEventListener('click', () => send({ type: 'run', cmd: 'on' }));
  $('off').addEventListener('click', () => send({ type: 'run', cmd: 'off' }));
  $('reload').addEventListener('click', async () => {
    if (tab) await chrome.tabs.reload(tab.id);
    window.close();
  });

  // 열 때마다 앱에 지금 상태를 물어본다 (결과는 storage 변경으로 들어옴)
  send({ type: 'refresh', force: true });
  setInterval(render, 30 * 1000);   // '켜는 중' 시간 초과 처리 등
}

init();
