# YADZY / Yahtzee — Unity Client

Клиент пошаговой мультиплеерной Yahtzee (2–4 игрока).  
Роль клиента: UI, анимации, офлайн-логика. Онлайн-матчи **авторитетны на Laravel API** (`yadzee_api`).

| | |
|---|---|
| **Репозиторий** | https://github.com/dartl/yahtzee_client |
| **Движок** | Unity **6000.4.11f1** (Unity 6) |
| **Язык** | C# (`async`/`await`), Newtonsoft.Json |
| **Realtime** | Laravel Reverb через NativeWebSocket |
| **Платформы** | Android, iOS, PC, WebGL |
| **Сцена** | `Assets/Scenes/SampleScene.unity` |
| **Версия клиента** | `ClientGate.ClientVersion` = `1.0.0` (шлётся как `X-Client-Version`) |

Связанный бэкенд: отдельный репозиторий API (Laravel + Sanctum + Reverb + PostgreSQL/Redis).

---

## Быстрый старт

1. Установите **Unity 6000.4.11f1** (или совместимый Unity 6 из того же major).
2. Клонируйте репозиторий и откройте папку как Unity-проект (Package Manager подтянет зависимости из `Packages/manifest.json`).
3. Убедитесь, что API и Reverb уже запущены (локально обычно `https://yahtzee.local` / порт Reverb `8080`).
4. Откройте сцену **`Assets/Scenes/SampleScene.unity`** и нажмите Play.
5. При первом входе клиент сам сделает guest-login (`device_id` + `device_secret` в PlayerPrefs).

### Важные ассеты

| Путь | Назначение |
|------|------------|
| `Assets/_Project/Resources/YahtzeeConfig.asset` | URL API, Reverb, правила, таймауты |
| `Assets/_Project/Resources/Themes/` | UI / board темы (Classic Clean) |
| `Assets/_Project/Scripts/` | Весь игровой код |
| `Assets/Resources/Localization/` | `en.json` / `ru.json` |
| `Docs/store-notes.md` | Заметки для стора |
| `.cursorrules` | Контракт клиента ↔ сервера для агентов/разработки |

---

## Настройка окружения (`YahtzeeConfig`)

Конфиг: **`Assets/_Project/Resources/YahtzeeConfig.asset`**  
(ScriptableObject, меню: *Yahtzee → Config*).

### Development (по умолчанию в Editor)

| Поле | Типичное значение |
|------|-------------------|
| API Base URL | `https://yahtzee.local` |
| Reverb Host | `yahtzee.local` |
| Reverb Port | `8080` |
| Reverb App Key | как в `.env` API (`REVERB_APP_KEY`) |
| Reverb TLS | `false` для локалки |
| Privacy / Terms | локальные HTML на том же домене |

### Production

В release-билде **без** Development Build клиент берёт production-поля:

- API: `https://api.yadzy.app`
- Reverb: `reverb.yadzy.app` + TLS
- Privacy / Terms: `https://yadzy.app/...`

В Editor можно принудительно включить production флагом **`Use Production In Editor`** на ассете конфига.

### Выбор окружения (логика)

- **Editor** → development, пока не включён `useProductionInEditor`
- **Development Build** → development endpoints
- **Release** → production endpoints

После смены URL/ключа Reverb перезапустите Play Mode.

---

## Режимы игры

### Онлайн (сервер считает кости и очки)

| Режим | Описание | При дисконнекте |
|-------|----------|-----------------|
| **Quick Match** | 1v1 casual очередь | Ждём reconnect; просрочка → forfeit |
| **Ranked** | 1v1 с MMR | То же; MMR меняется |
| **Tournament** | Live-очередь, недельные очки | То же; **без** соло «сдать лучший счёт» |
| **Custom lobby** 1v1 | Пароль/список лобби | Ждём возврата; соперник может выйти и отменить матч; idle ~1ч |
| **Custom lobby** 3–4 | До 4 игроков | Ушедший: лист заморожен, ход без бота |

В онлайн-матче клиент **не** генерирует authoritative-результаты: показывает то, что пришло по REST/WebSocket.

### Офлайн (локальная логика)

- **vs Bot** — локальный движок, уровни сложности  
- **Pass & Play** — несколько игроков на одном устройстве  
- **Bluetooth** — сейчас **только Android** (на iOS / PC / WebGL / Editor скрыто; см. `Docs/store-notes.md`)

---

## Авторизация и устройство

1. `POST /api/auth/guest` с `device_id`, `device_secret` (≥32 символа), `platform`.
2. Токен Sanctum кладётся в PlayerPrefs (`yahtzee.sanctum.token`).
3. Локально хранятся:
   - `yahtzee.device.id`
   - `yahtzee.device.secret`
4. Все защищённые запросы: `Authorization: Bearer …` + **`X-Client-Version: 1.0.0`**.
5. Краш-репорты (`/api/crash-reports`) тоже требуют токен.

Без валидного `X-Client-Version` API отвечает `426 VERSION_REQUIRED` / `VERSION_UNSUPPORTED`.

---

## Сеть и контракт с API

- REST: camelCase JSON  
- Матч: HTTP actions + канал Reverb `presence-match.{id}`  
- Auth для broadcasting: `/broadcasting/auth`  
- Статус сервиса / feature flags / min version: `GET /api/status` → `ClientGate`  
- Ошибки: `{ "error": "CODE", "message": "..." }` — тексты игроку через локализацию по коду  

Типичные пути (префикс = `ApiBaseUrl`):

```
/api/status
/api/auth/guest
/api/profile
/api/matchmaking/queue | /ranked
/api/tournament/join
/api/lobbies
/api/matches/{id}/roll|hold|score|reconnect|...
/api/shop , /api/economy/...
/api/crash-reports
```

DTO: `Assets/_Project/Scripts/Network/NetworkDtos.cs` — поля должны совпадать с Laravel.

---

## Экономика

Soft currency **только с сервера**:

- награды live-турнира  
- IAP packs  
- rewarded ads (S2S / проверенный redeem)  

Победа в обычном матче **не** даёт монеты.  
Локальный «офлайн-баланс» не синкается на сервер.

Магазин скинов: `/api/shop`, экипировка `/api/skins/equip`.  
Темы доски/UI: ScriptableObjects в `Resources/Themes/`.

---

## Структура кода

```
Assets/_Project/Scripts/
  Core/          YahtzeeConfig, CrashReporter, правила/кости
  Network/       NetworkManager, ClientGate, DTOs, Bluetooth*
  UI/            экраны меню, геймплея, лобби, магазина
  Models/        локальные модели счёта
  Localization/  GameText
  Audio/ Feedback/ Editor/
```

Архитектурные правила:

- UI подписан на события сети/геймплея, а не наоборот  
- В `Update()` — без лишних аллокаций  
- Online = визуализация серверного состояния  

---

## Зависимости (Package Manager)

Из `Packages/manifest.json` (ключевые):

- URP `com.unity.render-pipelines.universal`
- Input System
- Newtonsoft Json (`com.unity.nuget.newtonsoft-json`)
- NativeWebSocket (`com.endel.nativewebsocket`)
- uGUI, Timeline, Test Framework

Git-пакеты требуют сеть при первом открытии проекта.

---

## Сборка и стор

1. Выставьте production endpoints / Reverb key в `YahtzeeConfig`.
2. `ClientGate.ClientVersion` ≥ `minClientVersion` на сервере.
3. Пройдите чеклист: Unity меню **YADZY → Smoke Checklist** (и `yadzee_api/docs/staging-runbook.md` на стороне API).
4. Bluetooth: не обещайте на iOS/Web в стор-текстах — см. `Docs/store-notes.md`.
5. Production crash: дополнительно Firebase Crashlytics / Sentry (сейчас есть sink в API).

Платформенные заметки:

- **WebGL** — без Bluetooth; нужны CORS и `wss` на Reverb  
- **Android** — Bluetooth multiplayer доступен  
- **iOS / PC** — онлайн + Pass&Play + боты  

---

## Локальная отладка с API

1. Поднять `yadzee_api` (migrate, Sanctum, Reverb).  
2. Домен/сертификат `yahtzee.local` должен резолвиться с машины разработки (и с устройства — hosts / proxy).  
3. Согласовать `REVERB_APP_KEY` с полем Reverb App Key в конфиге.  
4. В Editor: Play → guest login → Quick Match на двух инстансах (Editor + Build, разные device id: суффиксы `_Editor` / `_Build`).  
5. Админка API: `/admin` (не часть этого репо).

Если онлайн-кнопки серые: смотрите `GET /api/status` (maintenance / feature flags / version).

---

## Git

В репозиторий **не** коммитятся: `Library/`, `Temp/`, `Logs/`, `obj/`, `Build/`, `UserSettings/`, `.csproj`/`.sln` (см. `.gitignore`).

```bash
git clone https://github.com/dartl/yahtzee_client.git
# открыть папку в Unity Hub → Add → Open
```

---

## Полезные ссылки в репо

- `Docs/store-notes.md` — ограничения стора / монеты / краши  
- `.cursorrules` — disconnect rules, экономика, контракт с сервером  
- `Assets/_Project/Scripts/Network/ClientGate.cs` — версия и feature flags  
- `Assets/_Project/Resources/YahtzeeConfig.asset` — все endpoint’ы  

Вопросы по серверной валидации, MMR и admin — в репозитории API (`docs/admin-panel.md`, `docs/security-backlog.md`, `docs/staging-runbook.md`).
