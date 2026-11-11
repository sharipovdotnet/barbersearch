import { describe, expect, it } from 'vitest'

import {
  AVATAR_WIDTHS,
  PORTFOLIO_WIDTHS,
  avatarImageSource,
  portfolioPhotoSource,
} from '@/lib/media'

const PORTFOLIO_URL =
  '/api/media/portfolio/9e100000-0000-4000-8000-000000000015/9e200000000040008000000000000001.jpg'

const AVATAR_URL =
  '/api/media/avatars/9e000000-0000-4000-8000-000000000001/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

const GALLERY_SIZES =
  '(min-width: 1024px) 25vw, (min-width: 640px) 33vw, 50vw'

describe('media: ширины — контракт с backend', () => {
  it('портфолио и аватар используют согласованные ширины', () => {
    expect(PORTFOLIO_WIDTHS).toEqual({ sm: 256, md: 512, lg: 1024 })
    expect(AVATAR_WIDTHS).toEqual({ sm: 96, md: 192, lg: 384 })
  })
})

describe('portfolioPhotoSource', () => {
  it('собственный URL: исходник в src, три ширины и sizes сетки', () => {
    expect(portfolioPhotoSource(PORTFOLIO_URL)).toEqual({
      src: PORTFOLIO_URL,
      srcSet: `${PORTFOLIO_URL}?size=sm 256w, ${PORTFOLIO_URL}?size=md 512w, ${PORTFOLIO_URL}?size=lg 1024w`,
      sizes: GALLERY_SIZES,
    })
  })

  it('sizes переопределяется вызывающим местом', () => {
    expect(portfolioPhotoSource(PORTFOLIO_URL, '80px')).toEqual({
      src: PORTFOLIO_URL,
      srcSet: `${PORTFOLIO_URL}?size=sm 256w, ${PORTFOLIO_URL}?size=md 512w, ${PORTFOLIO_URL}?size=lg 1024w`,
      sizes: '80px',
    })
  })

  it('src всегда остаётся исходником', () => {
    expect(portfolioPhotoSource(PORTFOLIO_URL).src).toBe(PORTFOLIO_URL)
  })

  it('внешний https-URL — только src', () => {
    const external = 'https://cdn.example.com/works/photo.jpg'

    expect(portfolioPhotoSource(external)).toEqual({ src: external })
  })

  it('data: и blob: — только src', () => {
    expect(portfolioPhotoSource('data:image/png;base64,AAAA')).toEqual({
      src: 'data:image/png;base64,AAAA',
    })
    expect(portfolioPhotoSource('blob:http://localhost/photo')).toEqual({
      src: 'blob:http://localhost/photo',
    })
  })

  it('URL с готовым query или fragment не портится', () => {
    const withQuery = `${PORTFOLIO_URL}?size=md`
    const withFragment = `${PORTFOLIO_URL}#photo`

    expect(portfolioPhotoSource(withQuery)).toEqual({ src: withQuery })
    expect(portfolioPhotoSource(withFragment)).toEqual({ src: withFragment })
  })

  it('URL аватара и чужой префикс не получают srcSet портфолио', () => {
    expect(portfolioPhotoSource(AVATAR_URL)).toEqual({ src: AVATAR_URL })
    expect(portfolioPhotoSource('/api/media/portfolio')).toEqual({
      src: '/api/media/portfolio',
    })
  })
})

describe('avatarImageSource', () => {
  it('собственный URL: исходник в src и плотности 1x/2x/3x без sizes', () => {
    const source = avatarImageSource(AVATAR_URL)

    expect(source).toEqual({
      src: AVATAR_URL,
      srcSet: `${AVATAR_URL}?size=sm 1x, ${AVATAR_URL}?size=md 2x, ${AVATAR_URL}?size=lg 3x`,
    })
    expect(source.sizes).toBeUndefined()
  })

  it('src всегда остаётся исходником', () => {
    expect(avatarImageSource(AVATAR_URL).src).toBe(AVATAR_URL)
  })

  it('внешний https-URL — только src', () => {
    const external = 'https://cdn.example.com/avatars/photo.jpg'

    expect(avatarImageSource(external)).toEqual({ src: external })
  })

  it('URL с готовым query или fragment не портится', () => {
    const withQuery = `${AVATAR_URL}?size=sm`
    const withFragment = `${AVATAR_URL}#avatar`

    expect(avatarImageSource(withQuery)).toEqual({ src: withQuery })
    expect(avatarImageSource(withFragment)).toEqual({ src: withFragment })
  })

  it('URL портфолио не получает srcSet аватара', () => {
    expect(avatarImageSource(PORTFOLIO_URL)).toEqual({ src: PORTFOLIO_URL })
  })
})
