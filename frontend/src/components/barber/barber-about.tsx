import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { BarberSectionHeading } from '@/components/barber/barber-section'
import { LANGUAGE_LABELS } from '@/lib/barber-profile-labels'
import type { BarberProfile } from '@/lib/catalog'

/**
 * «О мастере»: текст, языки и списки, которые барбер заполнил сам.
 *
 * Пустые поля не превращаются в заголовки с прочерком: если рассказывать нечего,
 * секции нет вовсе.
 */
export function BarberAbout({ profile }: { profile: BarberProfile }) {
  const lists = [
    { key: 'certificates', title: 'Сертификаты и обучение', items: profile.certificatesAndTraining },
    { key: 'achievements', title: 'Достижения', items: profile.achievements },
    { key: 'goodies', title: 'Особенности', items: profile.goodies },
    { key: 'communication', title: 'Формат общения', items: profile.communicationFormat },
  ].filter((list) => list.items.length > 0)

  const hasContent =
    Boolean(profile.bio) ||
    profile.languages.length > 0 ||
    lists.length > 0

  if (!hasContent) {
    return null
  }

  return (
    <section aria-labelledby="barber-about-heading">
      <Card>
        <BarberSectionHeading id="barber-about-heading">
          О мастере
        </BarberSectionHeading>

        <CardContent className="space-y-4">
          {profile.bio && (
            <p className="max-w-prose text-sm leading-relaxed break-words whitespace-pre-line">
              {profile.bio}
            </p>
          )}

          {profile.languages.length > 0 && (
            <div>
              <h3 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
                Языки
              </h3>
              <ul className="mt-2 flex flex-wrap gap-2">
                {profile.languages.map((language) => (
                  <li key={language}>
                    <Badge variant="secondary">
                      {LANGUAGE_LABELS[language]}
                    </Badge>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {lists.map((list) => (
            <div key={list.key}>
              <h3 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
                {list.title}
              </h3>
              <ul className="mt-2 list-disc space-y-1 pl-5 text-sm marker:text-muted-foreground">
                {list.items.map((item) => (
                  <li key={item} className="break-words">
                    {item}
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </CardContent>
      </Card>
    </section>
  )
}
