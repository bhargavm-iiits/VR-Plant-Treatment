import { useState, useRef, useCallback, useEffect } from 'react'
import { Link } from 'react-router-dom'

import Breadcrumb from '../components/Breadcrumb'
import ScrollReveal from '../components/ScrollReveal'
import { Upload } from '../components/Icons'
import VrLaunchCard from '../components/VrLaunchCard'
import { api } from '../lib/api'
import { useLang } from '../context/LanguageContext'
import { CROPS } from '../crops'
import { usePageTitle } from '../lib/usePageTitle'

// Matches the API's max_upload_bytes. Checked here so a farmer on a slow
// connection is told before the upload, not after it.
const MAX_MB = 10
const NOT_A_LEAF = 'Not_a_leaf'

/** A failed scan, said in words a farmer can act on. */
function scanError(err, t) {
  if (err.status === 503) return t('detect.err.noModel')
  if (err.status === 502) return t('detect.err.storage')
  if (err.status === 413) return t('detect.err.tooBig', { mb: MAX_MB })
  if (err.status === 400) return t('detect.err.badImage')
  if (err.status === undefined) return t('detect.err.network')
  return t('detect.err.server')
}

/** "Tomato___Early_blight" -> "Early blight", for a class the API did not name. */
const prettyClass = (id) => id.split('___').pop().replace(/_/g, ' ')

/**
 * What the scan found, in one line. Mirrors the Result page's wording, and
 * like it never dresses an unsure answer up as a diagnosis: no disease name,
 * no score, an ochre "Not sure".
 */
function summarise(result, t) {
  const { class_name: className, top3, disease_info: info } = result
  if (result.status === 'uncertain') {
    const wrongCrop = result.looksLikeCrop ? t(`crop.${result.looksLikeCrop}`) : null
    const title = top3?.[0]?.class_name === NOT_A_LEAF ? t('result.notLeafTitle')
      : wrongCrop ? t('result.wrongCropTitle', { crop: wrongCrop })
      : t('result.uncertainTitle')
    return { tone: 'caution', badge: t('result.uncertainBadge'), title, detail: t('detect.best') }
  }
  const healthy = info?.is_healthy ?? className.includes('healthy')
  const plant = info?.plant ?? className.split('___')[0].replace(/_/g, ' ')
  return {
    tone: healthy ? 'ok' : 'found',
    badge: healthy ? t('result.healthyBadge') : t('result.diseaseBadge'),
    title: info?.name || prettyClass(className),
    detail: `${t(`crop.${plant}`)} · ${t('result.confidence')} ${result.confidence.toFixed(1)}%`,
  }
}

/**
 * One job: choose a leaf photo. Choosing it is what starts the analysis, so
 * there is no button to press for that. Once the scan is back the page offers
 * exactly two: Open in CropScan XR and Pair VR. Everything else (the full
 * result, another photo) is a link.
 */
export default function Detect() {
  const [image, setImage]     = useState(null)
  const [preview, setPreview] = useState(null)
  const [dragOver, setDragOver] = useState(false)
  const [loading, setLoading] = useState(false)
  // Two messages, two places: a problem with the file sits by the picker, a
  // problem with the upload sits under the photo it was about.
  const [fileError, setFileError] = useState('')
  const [scanFailed, setScanFailed] = useState('')
  const [crop, setCrop]     = useState('')
  const [result, setResult] = useState(null)
  // The language the result's words were fetched in, to refetch on a switch.
  const [resultLang, setResultLang] = useState(null)
  const fileInputRef = useRef(null)
  // Each analysis gets a number. Only the newest one may touch the page, so a
  // slow answer for an earlier photo or crop can never overwrite a later one.
  const run = useRef(0)
  const { lang, t } = useLang()
  usePageTitle(t('detect.title'))

  // Leaving the page abandons whatever is in flight.
  useEffect(() => () => { run.current += 1 }, [])

  const analyse = useCallback(async (file, cropName) => {
    const mine = ++run.current
    setLoading(true)
    setScanFailed('')
    setResult(null)
    try {
      const scan = await api.createScan(file, lang, cropName)
      if (mine !== run.current) return
      setResult(scan)
      setResultLang(lang)
    } catch (err) {
      console.error('[CropScan] scan failed', err)
      // The photo and the crop stay exactly as they were, ready for a retry.
      if (mine === run.current) setScanFailed(scanError(err, t))
    } finally {
      if (mine === run.current) setLoading(false)
    }
  }, [lang, t])

  // The API writes the diagnosis in the reader's language, so a language
  // switch fetches the same scan again. It is a read: nothing is re-uploaded.
  const scanId = result?.id
  useEffect(() => {
    if (!scanId || resultLang === lang) return
    let alive = true
    api.getScan(scanId, lang)
      .then((scan) => {
        if (!alive) return
        setResult(scan)
        setResultLang(lang)
      })
      .catch(() => {})   // the result already on screen stays, in its earlier language
    return () => { alive = false }
  }, [scanId, resultLang, lang])

  const handleFile = useCallback((file) => {
    if (!file) return
    if (!file.type.startsWith('image/')) {
      setFileError(t('detect.err.notImage'))
      return
    }
    if (file.size > MAX_MB * 1024 * 1024) {
      setFileError(t('detect.err.tooBig', { mb: MAX_MB }))
      return
    }
    setFileError('')
    setImage(file)
    // ponytail: the last preview is not revoked on unmount -- Result still
    // shows it. One blob per scan; revoked whenever it is replaced here.
    setPreview((old) => {
      if (old) URL.revokeObjectURL(old)
      return URL.createObjectURL(file)
    })
    analyse(file, crop)
  }, [t, analyse, crop])

  const onDrop = (e) => {
    e.preventDefault()
    setDragOver(false)
    handleFile(e.dataTransfer.files?.[0])
  }

  const changeCrop = (next) => {
    setCrop(next)
    // The crop is part of the question, so a photo already chosen is asked again.
    if (image) analyse(image, next)
  }

  const summary = result ? summarise(result, t) : null

  return (
    <div className="page page--contours">
      <Breadcrumb items={[{ label: t('nav.home'), to: '/' }, { label: t('nav.detect') }]} />
      <h1 className="h1" tabIndex={-1}>{t('detect.title')}</h1>
      <p className="lead">{t('detect.intro')} {t('detect.auto')}</p>

      <div className="detect" aria-busy={loading}>
        <div className="field field--inline">
          <label htmlFor="leaf-crop">{t('detect.cropQ')}</label>
          {/* Naming the crop stops look-alike crops competing (corn vs rice,
              pepper vs chilli): about 59% -> 81% right on real field photos.
              Optional, so it is never marked required. */}
          <select
            id="leaf-crop"
            className="select"
            value={crop}
            onChange={(e) => changeCrop(e.target.value)}
            aria-describedby="leaf-crop-hint"
          >
            <option value="">{t('detect.cropAny')}</option>
            {CROPS.map(([, plant]) => (
              <option key={plant} value={plant}>{t(`crop.${plant}`)}</option>
            ))}
          </select>
          <p id="leaf-crop-hint" className="hint">{t('detect.cropHint')}</p>
        </div>

        <ScrollReveal
          className="detect__photo"
          aria-labelledby="leaf-photo-title"
          data-drag={dragOver}
          onDrop={onDrop}
          onDragOver={(e) => { e.preventDefault(); setDragOver(true) }}
          onDragLeave={() => setDragOver(false)}
        >
          <h2 id="leaf-photo-title" className="sr-only">{t('detect.photo')}</h2>

          {/* One real file input for every state. It is visually hidden but
              stays in the tab order; the frame after it shows its focus ring.
              On a phone this is what offers the camera or the photo library. */}
          <input
            ref={fileInputRef}
            id="leaf-file"
            className="sr-only file-input"
            type="file"
            accept="image/*"
            aria-labelledby="leaf-photo-title"
            aria-describedby={fileError ? 'leaf-help leaf-error' : 'leaf-help'}
            aria-invalid={Boolean(fileError)}
            onChange={(e) => handleFile(e.target.files?.[0])}
          />

          {preview ? (
            <div className="shot" data-busy={loading}>
              <img src={preview} alt={t('detect.previewAlt')} />
              <span className="frame-marks" aria-hidden="true" />
              {loading && <span className="shot__sweep" aria-hidden="true" />}
            </div>
          ) : (
            <label htmlFor="leaf-file" className="dropzone">
              <span className="frame-marks" aria-hidden="true" />
              <Upload size={40} />
              <span className="dropzone__title">
                {dragOver ? t('detect.dropActive') : t('detect.choose')}
              </span>
              <span className="dropzone__hint">{t('detect.drop')}</span>
            </label>
          )}

          <p id="leaf-help" className="detect__formats">{t('detect.formats', { mb: MAX_MB })}</p>
          {fileError && <p id="leaf-error" role="alert" className="note note--fail">{fileError}</p>}
        </ScrollReveal>

        {/* Always in the page, so what arrives in it is announced: first that
            the photo is being analysed, then what was found. */}
        <div className="detect__result" role="status">
          {loading && <p className="muted">{t('detect.analysingStatus')}</p>}
          {summary && (
            <ScrollReveal as="div" className="detect__summary">
              <p className={`status status--${summary.tone}`}>
                <span className="status__dot" aria-hidden="true" />
                {summary.badge}
              </p>
              <h2 className="detect__name">{summary.title}</h2>
              <p className="muted">{summary.detail}</p>
            </ScrollReveal>
          )}
        </div>

        {scanFailed && (
          <div role="alert" className="note note--fail note--row">
            <span>{scanFailed}</span>
            <button type="button" className="btn-secondary btn-sm" onClick={() => analyse(image, crop)}>
              {t('field.retry')}
            </button>
          </div>
        )}

        {result && (
          <>
            {/* The two actions this page ends in. Keyed by scan, so a new photo
                starts them clean. Each says why when it cannot be used. */}
            <VrLaunchCard key={result.id} result={result} t={t} compact />

            <ScrollReveal as="p" className="detect__links">
              <Link
                to={`/result?scan=${encodeURIComponent(result.id)}`}
                state={{ result, imageUrl: preview }}
                className="link"
              >
                {t('detect.fullResult')}
              </Link>
              {/* A label for the same file input: pressing it opens the picker.
                  From a keyboard the input itself is the way in. */}
              <label htmlFor="leaf-file" className="link link--label">{t('detect.another')}</label>
            </ScrollReveal>
          </>
        )}
      </div>
    </div>
  )
}
