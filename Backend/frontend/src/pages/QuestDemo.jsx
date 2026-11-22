import Breadcrumb from '../components/Breadcrumb'
import ScrollReveal from '../components/ScrollReveal'
import { useLang } from '../context/LanguageContext'
import { isQuestBrowser, questApkDownload, QUEST_DEMO_APP_LINK, sendDemoToQuest } from '../lib/questDemo'
import { usePageTitle } from '../lib/usePageTitle'

import questArt from '../assets/art/quest-lab-threshold.webp'

export default function QuestDemo() {
  const { t } = useLang()
  usePageTitle(t('quest.title'))

  // What is offered is decided by the Quest helpers, not by the layout: the
  // send link only exists over HTTPS and outside the headset, the download
  // only when an APK URL is configured. The installed-app link is always there.
  const origin = window.location.origin
  const inQuest = isQuestBrowser(navigator.userAgent)
  const sendUrl = inQuest ? null : sendDemoToQuest(origin)
  const download = questApkDownload(import.meta.env.VITE_QUEST_APK_URL, origin)

  return (
    <div className="quest">
      <div className="quest__copy">
        <Breadcrumb items={[{ label: t('nav.field'), to: '/field' }, { label: t('quest.crumb') }]} />
        <h1 className="h1 h1--quest" tabIndex={-1}>{t('quest.title')}</h1>
        <p className="lead">{t('quest.description')}</p>

        {/* Each of these is followed only from the reader's own click. A custom
            protocol must never fire from a visit or a scroll. */}
        <div className="quest__actions">
          {sendUrl && <a href={sendUrl} className="btn-primary btn-block">{t('quest.send')}</a>}
          {download && <a href={download} className="btn-secondary btn-block">{t('quest.download')}</a>}
          <a href={QUEST_DEMO_APP_LINK} className={sendUrl ? 'link' : 'btn-primary btn-block'}>
            {t('quest.open')}
          </a>
        </div>

        <ScrollReveal aria-labelledby="quest-steps">
          <h2 className="quest__steps-title" id="quest-steps">{t('quest.steps')}</h2>
          <ol className="numbered">
            <li>{t('quest.install')}</li>
            <li>{t('quest.browser')}</li>
            <li>{t('quest.manual')}</li>
          </ol>
        </ScrollReveal>
      </div>

      <ScrollReveal as="div" className="quest__art" aria-hidden="true">
        <div className="quest__stage">
          <img src={questArt} alt="" width="1059" height="910" decoding="async" />
        </div>
      </ScrollReveal>
    </div>
  )
}
