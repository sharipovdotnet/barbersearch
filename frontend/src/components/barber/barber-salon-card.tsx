import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import type { BarberSalonSummary } from '@/lib/barber-salons'
import {
  SALON_MODERATION_LABELS,
  type SalonModerationStatus,
} from '@/lib/salon-moderation'

/**
 * Карточка привязанного салона в «Моих салонах».
 *
 * Статус модерации — словом, а не только цветом: барбер должен понимать, почему
 * салона нет в поиске. Правка и состав доступны только владельцу (`ownerUserId`),
 * в том числе для салона на проверке; у салона, созданного администратором,
 * владельца нет, и кнопок управления нет. Активации здесь нет — видимостью
 * управляет администратор.
 */
export function BarberSalonCard({
  salon,
  canEdit,
  onEdit,
  onManageBarbers,
}: {
  salon: BarberSalonSummary
  /** `ownerUserId === user.id`: чужой салон правит только администратор. */
  canEdit: boolean
  onEdit: (focus: 'location' | 'contacts') => void
  /** Состав салона: владелец прикрепляет и открепляет других барберов. */
  onManageBarbers: () => void
}) {
  return (
    <Card>
      <CardContent className="space-y-2 pt-6">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="font-medium break-words">{salon.name}</h2>
          {salon.isPrimary && <Badge variant="secondary">Основной</Badge>}
          <Badge variant={moderationVariant(salon.moderationStatus)}>
            {SALON_MODERATION_LABELS[salon.moderationStatus]}
          </Badge>
        </div>

        <p className="text-sm break-words text-muted-foreground">
          {salon.city}
          {salon.address ? `, ${salon.address}` : ''}
        </p>

        <p className="text-sm text-muted-foreground">
          Часовой пояс:{' '}
          <span className="text-foreground">{salon.timeZoneId}</span>
        </p>

        {salon.moderationStatus === 'Pending' && (
          <p className="text-sm text-muted-foreground">
            Салон появится в поиске после проверки администратором.
          </p>
        )}

        {salon.moderationStatus === 'Rejected' && (
          <p className="text-sm text-destructive">
            Причина отказа:{' '}
            <span className="text-foreground">
              {salon.rejectionReason ?? 'не указана'}
            </span>
          </p>
        )}

        {canEdit && (
          <div className="flex flex-wrap gap-2 pt-1">
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              onClick={onManageBarbers}
            >
              Барберы салона
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              onClick={() => onEdit('location')}
            >
              Изменить адрес
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              onClick={() => onEdit('contacts')}
            >
              Изменить контакты
            </Button>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function moderationVariant(
  status: SalonModerationStatus
): 'secondary' | 'outline' | 'destructive' {
  switch (status) {
    case 'Active':
      return 'secondary'
    case 'Pending':
      return 'outline'
    case 'Rejected':
      return 'destructive'
  }
}
