import { describe, expect, it } from 'vitest'

import { setCurrentLocale } from '@/i18n'
import {
  canBlockPhoto,
  canRenderAvatarCardPreview,
  canRenderAvatarPreview,
  canReviewPhoto,
  isPhotoModerationStatus,
  PHOTO_MODERATION_LABELS,
  PHOTO_MODERATION_STATUSES,
  PHOTO_REJECTION_MAX_LENGTH,
  photoModerationLabel,
  validatePhotoRejectionReason,
} from '@/lib/photo-moderation'

/**
 * Премодерация фото: значения, подписи и правила показа.
 *
 * Статус приходит из API именем, поэтому проверяем только три известных значения
 * и то, что новый серверный статус стал бы ошибкой экрана, а не сломанной строкой.
 */

describe('isPhotoModerationStatus', () => {
  it('принимает три значения контракта', () => {
    expect(PHOTO_MODERATION_STATUSES).toStrictEqual([
      'Pending',
      'Approved',
      'Rejected',
    ])

    for (const status of PHOTO_MODERATION_STATUSES) {
      expect(isPhotoModerationStatus(status)).toBe(true)
    }
  })

  it('отвергает числа, регистр и неизвестные имена', () => {
    expect(isPhotoModerationStatus(1)).toBe(false)
    expect(isPhotoModerationStatus('pending')).toBe(false)
    expect(isPhotoModerationStatus('Queued')).toBe(false)
    expect(isPhotoModerationStatus(null)).toBe(false)
    expect(isPhotoModerationStatus(undefined)).toBe(false)
  })
})

describe('подписи статусов', () => {
  it('держит русские исходные строки в словаре', () => {
    expect(PHOTO_MODERATION_LABELS).toStrictEqual({
      Pending: 'На проверке',
      Approved: 'Одобрено',
      Rejected: 'Отклонено',
    })
  })

  it('переводит подпись через t() на месте вывода', () => {
    expect(photoModerationLabel('Pending')).toBe('На проверке')

    setCurrentLocale('en')

    expect(photoModerationLabel('Pending')).toBe('Pending review')
    expect(photoModerationLabel('Approved')).toBe('Approved')
    expect(photoModerationLabel('Rejected')).toBe('Rejected')
  })
})

describe('правила действий и превью', () => {
  it('одобрять и отклонять можно только кадр «На проверке»', () => {
    expect(canReviewPhoto('Pending')).toBe(true)
    expect(canReviewPhoto('Approved')).toBe(false)
    expect(canReviewPhoto('Rejected')).toBe(false)
  })

  it('блокировка имеет смысл только для одобренного кадра', () => {
    expect(canBlockPhoto('Approved')).toBe(true)
    expect(canBlockPhoto('Pending')).toBe(false)
    expect(canBlockPhoto('Rejected')).toBe(false)
  })

  it('владелец видит превью аватара при Pending и Approved', () => {
    const base = { avatarUrl: '/api/media/avatars/u/x.jpg', isAvatarBlocked: false }

    expect(
      canRenderAvatarPreview({ ...base, avatarModerationStatus: 'Pending' })
    ).toBe(true)
    expect(
      canRenderAvatarPreview({ ...base, avatarModerationStatus: 'Approved' })
    ).toBe(true)
    expect(
      canRenderAvatarPreview({ ...base, avatarModerationStatus: 'Rejected' })
    ).toBe(false)
  })

  it('отклонённый, скрытый и отсутствующий кадр не рендерится', () => {
    expect(
      canRenderAvatarPreview({
        avatarUrl: '/api/media/avatars/u/x.jpg',
        isAvatarBlocked: true,
        avatarModerationStatus: 'Approved',
      })
    ).toBe(false)
    expect(
      canRenderAvatarPreview({
        avatarUrl: null,
        isAvatarBlocked: false,
        avatarModerationStatus: 'Pending',
      })
    ).toBe(false)
  })

  it('в карточке кабинета виден и отклонённый кадр — рядом причина', () => {
    const avatarUrl = '/api/media/avatars/u/x.jpg'

    expect(
      canRenderAvatarCardPreview({ avatarUrl, isAvatarBlocked: false })
    ).toBe(true)
    expect(
      canRenderAvatarCardPreview({ avatarUrl, isAvatarBlocked: true })
    ).toBe(false)
    expect(
      canRenderAvatarCardPreview({ avatarUrl: null, isAvatarBlocked: false })
    ).toBe(false)
  })
})

describe('validatePhotoRejectionReason', () => {
  it('пустая причина и одни пробелы — ошибка', () => {
    expect(validatePhotoRejectionReason('')).toBe('Укажите причину отказа.')
    expect(validatePhotoRejectionReason('   ')).toBe('Укажите причину отказа.')
  })

  it('причина в пределах 1..500 принимается', () => {
    expect(validatePhotoRejectionReason('Кадр размыт')).toBeNull()
    expect(
      validatePhotoRejectionReason('а'.repeat(PHOTO_REJECTION_MAX_LENGTH))
    ).toBeNull()
    expect(PHOTO_REJECTION_MAX_LENGTH).toBe(500)
  })

  it('слишком длинная причина — ошибка с пределом', () => {
    expect(
      validatePhotoRejectionReason('а'.repeat(PHOTO_REJECTION_MAX_LENGTH + 1))
    ).toBe('Слишком длинная причина — максимум 500 символов.')
  })
})
