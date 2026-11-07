import { useEffect, useState, type MouseEvent } from 'react'
import { Upload } from 'lucide-react'

import { SignInGate } from '@/components/account/account-gates'
import { BarberCabinetNav } from '@/components/barber/barber-cabinet-nav'
import {
  BarberCabinetHeading,
  BarberCabinetSkeleton,
  BarberProfileMissingGate,
  BarberRoleGate,
} from '@/components/barber/barber-cabinet-gates'
import { ImportForm } from '@/components/barber/import-form'
import { ImportPreview } from '@/components/barber/import-preview'
import { ImportResult } from '@/components/barber/import-result'
import { SessionUnavailable } from '@/components/session-unavailable'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { useBarberImport, type ImportPause } from '@/hooks/use-barber-import'
import { useSession } from '@/hooks/use-session'
import {
  BARBER_CABINET_PATH,
  BARBER_IMPORT_PATH,
  BARBER_SALONS_PATH,
} from '@/lib/barber-cabinet-path'
import { isPlainClick, navigate } from '@/lib/router'

/**
 * Перенос старых записей из вставленного списка.
 *
 * Три шага на одной странице, без URL-состояния: вставленный текст — личные данные
 * клиентов, ему нечего делать в адресе и в истории браузера. Гейты те же, что
 * у кабинета: пока сессия, роль и профиль не проверены, запросов нет.
 */
export function BarberImportPage() {
  const { status, user } = useSession()

  useEffect(() => {
    document.title = 'Перенести записи — BarberSearch'
  }, [])

  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-8 sm:py-10">
      {status === 'loading' && (
        <div className="space-y-5">
          <p role="status" className="sr-only">
            Загружаем перенос записей…
          </p>
          <BarberCabinetSkeleton />
        </div>
      )}

      {status === 'unavailable' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <SessionUnavailable />
        </div>
      )}

      {status === 'anonymous' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <SignInGate
            title="Войдите, чтобы перенести записи"
            description="Разбор списка и создание записей доступны барберу после входа. Вход по email и паролю."
            redirectTo={BARBER_IMPORT_PATH}
          />
        </div>
      )}

      {status === 'authenticated' && user && user.role !== 'Barber' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <BarberRoleGate role={user.role} />
        </div>
      )}

      {status === 'authenticated' && user && user.role === 'Barber' && (
        user.barberProfileId === null ? (
          <div className="flex min-h-[55vh] items-center justify-center py-6">
            <BarberProfileMissingGate />
          </div>
        ) : (
          <BarberImportScreen
            key={user.barberProfileId}
            barberProfileId={user.barberProfileId}
          />
        )
      )}
    </div>
  )
}

function BarberImportScreen({ barberProfileId }: { barberProfileId: string }) {
  const state = useBarberImport(barberProfileId)

  const isSignInPause = state.pause?.kind === 'signIn'
  const hasSalons =
    state.options !== null && state.options.salons.length > 0
  const isReady = state.optionsStatus === 'ready' && hasSalons

  return (
    <div className="space-y-5">
      <BarberCabinetHeading />
      <BarberCabinetNav active="bookings" />

      <a
        href={BARBER_CABINET_PATH}
        className="inline-flex h-11 items-center text-sm text-muted-foreground underline-offset-2 hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:h-8"
        onClick={(event: MouseEvent<HTMLAnchorElement>) => {
          if (isPlainClick(event)) {
            event.preventDefault()
            navigate(BARBER_CABINET_PATH)
          }
        }}
      >
        ← Записи клиентов
      </a>

      <header className="flex items-center gap-3">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Upload className="size-5 text-muted-foreground" aria-hidden />
        </div>
        <div>
          <h2 className="font-heading text-2xl font-semibold tracking-tight">
            Перенести записи
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Вставьте список из Google Docs, таблицы или заметок — мы разберём его
            построчно.
          </p>
        </div>
      </header>

      {isSignInPause ? (
        <div className="flex min-h-[45vh] items-center justify-center py-6">
          <SignInGate
            title="Сессия истекла"
            description="Войдите снова, чтобы продолжить с того места, где остановились."
            redirectTo={BARBER_IMPORT_PATH}
            onSignedIn={() => {
              void state.resume()
            }}
          />
        </div>
      ) : (
        <>
          {state.optionsStatus === 'loading' && (
            <p role="status" className="text-sm text-muted-foreground">
              Загружаем услуги и салоны…
            </p>
          )}

          {state.optionsStatus === 'error' && (
            <Card>
              <CardHeader>
                <CardTitle>Не удалось загрузить услуги и салоны</CardTitle>
                <CardDescription>
                  Без прайса и салонов не из чего выбирать при разборе строк.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-3">
                <p role="alert" className="text-sm text-destructive">
                  {state.optionsError}
                </p>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="h-11 sm:h-8"
                  onClick={state.retryOptions}
                >
                  Повторить
                </Button>
              </CardContent>
            </Card>
          )}

          {state.optionsStatus === 'ready' && !hasSalons && (
            <Card>
              <CardHeader>
                <CardTitle>Нет привязанных салонов</CardTitle>
                <CardDescription>
                  Перенос создаёт записи в салоне профиля. Добавьте место приёма
                  — и возвращайтесь сюда.
                </CardDescription>
              </CardHeader>
              <CardContent>
                <Button
                  type="button"
                  size="sm"
                  className="h-11 sm:h-8"
                  onClick={() => {
                    navigate(BARBER_SALONS_PATH)
                  }}
                >
                  Открыть салоны
                </Button>
              </CardContent>
            </Card>
          )}

          {isReady && state.stage === 'form' && (
            <ImportForm
              text={state.text}
              failure={state.failure}
              onChange={state.setText}
              onParse={state.runParse}
              onClear={state.reset}
            />
          )}

          {state.optionsStatus === 'ready'
            && state.stage === 'preview'
            && (state.options === null ? (
              <p role="status" className="text-sm text-muted-foreground">
                Загружаем услуги и салоны…
              </p>
            ) : (
              <>
                <ImportPreview
                  rows={state.rows}
                  summary={state.summary}
                  options={state.options}
                  isRunning={state.isRunning}
                  progress={state.progress}
                  onUpdate={state.updateRow}
                  onCreate={() => {
                    void state.createRows()
                  }}
                  onBack={state.backToForm}
                />

                {state.pause !== null && state.pause.kind !== 'signIn' && (
                  <ImportPauseNotice
                    pause={state.pause}
                    onResume={() => {
                      void state.resume()
                    }}
                  />
                )}
              </>
            ))}

          {state.optionsStatus === 'ready'
            && state.stage === 'result'
            && (state.options === null ? (
              <p role="status" className="text-sm text-muted-foreground">
                Загружаем услуги и салоны…
              </p>
            ) : (
              <>
                <ImportResult
                  rows={state.rows}
                  summary={state.summary}
                  options={state.options}
                  isRunning={state.isRunning}
                  onRetryFailed={() => {
                    void state.retryFailed()
                  }}
                  onApplySlot={(line, slot) => {
                    void state.applySlot(line, slot)
                  }}
                  onLoadSlots={(line) => {
                    void state.loadSlots(line)
                  }}
                  onOpenBookings={() => {
                    navigate(BARBER_CABINET_PATH)
                  }}
                />

                {state.pause !== null && state.pause.kind !== 'signIn' && (
                  <ImportPauseNotice
                    pause={state.pause}
                    onResume={() => {
                      void state.resume()
                    }}
                  />
                )}
              </>
            ))}
        </>
      )}
    </div>
  )
}

/**
 * Остановка пачки: `429` и сеть лечатся повтором, `403`/`404` — нет.
 *
 * Для `429` кнопка ждёт минуту: сервер просит именно столько, а мгновенный повтор
 * только продлит паузу.
 */
function ImportPauseNotice({
  pause,
  onResume,
}: {
  pause: NonNullable<ImportPause>
  onResume: () => void
}) {
  const [seconds, setSeconds] = useState(pause.kind === 'rateLimit' ? 60 : 0)

  useEffect(() => {
    if (seconds <= 0) {
      return
    }

    const timer = window.setTimeout(() => {
      setSeconds((value) => value - 1)
    }, 1000)

    return () => {
      window.clearTimeout(timer)
    }
  }, [seconds])

  return (
    <div
      role="alert"
      className="flex flex-wrap items-center gap-3 rounded-lg border border-dashed px-3 py-2 text-sm"
    >
      <p className="min-w-0 flex-1">{pause.message}</p>
      {pause.kind !== 'fatal' && (
        <Button
          type="button"
          size="sm"
          className="h-11 sm:h-8"
          disabled={seconds > 0}
          onClick={onResume}
        >
          {seconds > 0 ? `Продолжить через ${seconds} с` : 'Продолжить'}
        </Button>
      )}
    </div>
  )
}
