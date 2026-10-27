// 서비스 워커와 팝업이 같이 쓰는 것들

export const HOST = 'com.bankmode.host';

let defaultSitesPromise;

export async function getSites() {
  const { sites } = await chrome.storage.local.get('sites');
  if (Array.isArray(sites) && sites.length) return sites;
  if (!defaultSitesPromise) {
    defaultSitesPromise = fetch(chrome.runtime.getURL('sites.json')).then((r) => r.json()).catch(() => []);
  }
  return defaultSitesPromise;
}

export function sitePermissionPattern(site) {
  try {
    const u = new URL(site.url);
    if ((u.protocol !== 'https:' && u.protocol !== 'http:') || u.username || u.password) return null;
    if (u.port && !((u.protocol === 'https:' && u.port === '443') || (u.protocol === 'http:' && u.port === '80'))) return null;
    // Host permissions must stay scoped to one valid DNS name and its subdomains.
    // Reject IPs, localhost, wildcards, and malformed labels rather than ever
    // turning unexpected site data into a broader Chrome match pattern.
    const host = u.hostname.toLowerCase().replace(/^www\./, '').replace(/\.$/, '');
    if (!host || host.length > 253 || host === 'localhost' || /^\d+(?:\.\d+){3}$/.test(host)) return null;
    const labels = host.split('.');
    if (labels.length < 2 || labels.some((label) =>
      label.length < 1 || label.length > 63 || !/^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/.test(label))) return null;
    return `${u.protocol}//*.${host}/*`;
  } catch {
    return null;
  }
}

// True only when a site permission is already supplied by the extension's
// required manifest permissions. Such permissions cannot be revoked per site.
export function sitePermissionIsStatic(site, patterns = chrome.runtime.getManifest().host_permissions || []) {
  const target = sitePermissionPattern(site);
  if (!target) return false;
  const targetUrl = new URL(site.url);
  const targetHost = targetUrl.hostname.toLowerCase().replace(/^www\./, '').replace(/\.$/, '');
  return patterns.some((pattern) => {
    const match = /^(\*|https?|file):\/\/([^/]+)\/(.*)$/.exec(pattern);
    if (!match || match[1] === 'file') return false;
    const [, scheme, hostPattern, path] = match;
    if (scheme !== '*' && `${scheme}:` !== targetUrl.protocol) return false;
    if (path !== '*') return false;
    if (hostPattern === '*') return true;
    if (hostPattern.startsWith('*.')) {
      const base = hostPattern.slice(2).toLowerCase();
      return targetHost === base || targetHost.endsWith(`.${base}`);
    }
    return false; // An exact host does not cover the site's registered subdomains.
  });
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
  if (/not found/i.test(m)) return '은행 모드 앱이 설치되지 않았거나 크롬과 연결되지 않았어요.';
  if (/forbidden/i.test(m)) return "설치된 앱이 이 확장을 아직 모르는 버전이에요. 아래 설치 파일로 최신 앱을 설치하거나, 앱에서 '크롬 확장 연결'을 다시 눌러 주세요.";
  if (/exited|communicating/i.test(m)) return "은행 모드 앱이 응답 없이 끝났어요. 앱 폴더를 옮겼다면 아래 설치 파일로 다시 설치하거나, 앱에서 '크롬 확장 연결'을 다시 눌러 주세요.";
  return m || '알 수 없는 오류가 났어요.';
}

// 켜고 끄는 중 표시가 이보다 오래 남아 있으면 끝난 것으로 본다 (브라우저가 중간에 멈춘 경우)
export const BUSY_TIMEOUT_MS = 5 * 60 * 1000;

export function isBusy(st) {
  return !!(st && st.busy && Date.now() - st.busy.startedAt < BUSY_TIMEOUT_MS);
}
