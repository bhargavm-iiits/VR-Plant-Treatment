export const QUEST_DEMO_PATH = '/quest-demo'
export const QUEST_DEMO_APP_LINK = 'plantdiseasevr://demo/'

export function isQuestBrowser(userAgent) {
  return /\b(?:OculusBrowser|MetaQuestBrowser)\//i.test(userAgent)
}

export function questDemoIntent(origin) {
  const fallback = encodeURIComponent(new URL(QUEST_DEMO_PATH, origin).href)
  return `intent://demo/#Intent;scheme=plantdiseasevr;package=com.btp.plantdiseasevr;S.browser_fallback_url=${fallback};end`
}

export function sendDemoToQuest(origin) {
  const demo = new URL(QUEST_DEMO_PATH, origin)
  if (demo.protocol !== 'https:') return null

  const url = new URL('https://www.oculus.com/open_url/')
  url.searchParams.set('url', demo.href)
  return url.href
}

export function questApkDownload(value, origin) {
  if (!value?.trim()) return null

  try {
    const url = new URL(value, origin)
    return url.protocol === 'https:' || url.protocol === 'http:' ? url.href : null
  } catch {
    return null
  }
}
