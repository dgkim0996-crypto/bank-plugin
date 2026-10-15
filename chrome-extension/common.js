// 서비스 워커와 팝업이 같이 쓰는 것들

export const HOST = 'com.bankmode.host';

// 앱에서 사이트 목록을 아직 못 받았을 때 쓰는 기본값 (앱 settings.json 기본값과 같음)
export const DEFAULT_SITES = [
  { name: 'KB국민은행', url: 'https://www.kbstar.com' },
  { name: '신한은행', url: 'https://www.shinhan.com' },
  { name: '우리은행', url: 'https://www.wooribank.com' },
  { name: '하나은행', url: 'https://www.kebhana.com' },
  { name: 'IBK기업은행', url: 'https://www.ibk.co.kr' },
  { name: 'NH농협은행', url: 'https://banking.nonghyup.com' },
  { name: '홈택스', url: 'https://www.hometax.go.kr' },
  { name: '정부24', url: 'https://www.gov.kr' },
];

export async function getSites() {
  const { sites } = await chrome.storage.local.get('sites');
  return Array.isArray(sites) && sites.length ? sites : DEFAULT_SITES;
}

// 주소가 등록된 은행 사이트(또는 그 하위 도메인)면 그 사이트를 돌려준다
// www.kbstar.com 으로 등록돼 있으면 obank.kbstar.com 도 같은 은행으로 본다
export function siteFor(url, sites) {
  let host;
  try {
    const u = new URL(url);
    if (u.protocol !== 'https:' && u.protocol !== 'http:') return null;
    host = u.hostname.toLowerCase();
  } catch {
    return null;
  }
  for (const s of sites || []) {
    let base;
    try {
      base = new URL(s.url).hostname.toLowerCase().replace(/^www\./, '');
    } catch {
      continue;
    }
    if (base && (host === base || host.endsWith('.' + base))) return s;
  }
  return null;
}

// 크롬의 Native Messaging 오류(영문)를 사용자에게 보여 줄 말로
export function friendlyError(e) {
  const m = String((e && e.message) || e || '');
  if (/not found/i.test(m)) return "은행 모드 앱과 연결되지 않았어요. 앱 아래쪽의 '크롬 확장 연결'을 눌러 주세요.";
  if (/forbidden/i.test(m)) return "이 확장 프로그램이 앱에 등록되지 않았어요. 앱에서 '크롬 확장 연결'을 다시 눌러 주세요.";
  if (/exited|communicating/i.test(m)) return "은행 모드 앱이 응답 없이 끝났어요. 앱 폴더를 옮겼다면 앱에서 '크롬 확장 연결'을 다시 눌러 주세요.";
  return m || '알 수 없는 오류가 났어요.';
}

// 켜고 끄는 중 표시가 이보다 오래 남아 있으면 끝난 것으로 본다 (브라우저가 중간에 멈춘 경우)
export const BUSY_TIMEOUT_MS = 5 * 60 * 1000;

export function isBusy(st) {
  return !!(st && st.busy && Date.now() - st.busy.startedAt < BUSY_TIMEOUT_MS);
}
