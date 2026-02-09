# AGENTS.md — правила работы, сборки и тестирования VibeShadowsocks

Этот документ задаёт обязательные правила для любых изменений (новые функции, рефакторинг, багфиксы) и порядок проверки качества/устойчивости. Цель: PRO-уровень архитектуры, UX/UI и надёжности.

---

## 1) Основные цели проекта

- **WinUI 3 Desktop (.NET 8)** приложение-обвязка над `shadowsocks-rust` (`sslocal.exe`), без реализации протокола/криптографии в коде.
- **Максимальная стрессоустойчивость**: отсутствие гонок и “сломанных” proxy-настроек Windows при любой ошибке/краше.
- **Профессиональный UX/UI**: быстрые сценарии (трей, хоткей, профили), понятные состояния, корректные уведомления, отсутствие фризов.

---

## 2) Инструменты и окружение (обязательно для сборки)

### Требования

- Windows 10/11 (x64)
- Visual Studio 2022 (или Build Tools 2022) с workload:
  - **.NET Desktop Development**
  - **Windows App SDK / WinUI 3 tooling**
- **.NET SDK 8.x**
- Git

### Локальные зависимости

- `sslocal.exe` должен быть доступен:
  - либо рядом с приложением (папка `tools/sslocal/` или `AppData`),
  - либо путь задаётся в Settings.

---

## 3) Структура решения и правила зависимостей

### Проекты

- `VibeShadowsocks.App` — WinUI 3 UI (только UI/Views/ViewModels/Navigation)
- `VibeShadowsocks.Core` — доменная логика (state machine, модели, правила маршрутизации, PAC генерация)
- `VibeShadowsocks.Infrastructure` — запуск процессов, прокси, PAC сервер, сеть, кеш, хранилища
- `VibeShadowsocks.Platform` — Win32 interop (tray, hotkey, single-instance, WinINet refresh)
- `VibeShadowsocks.Tests` — xUnit (Core + Infrastructure unit tests)

### Жёсткие правила ссылок

- `Core` **не зависит** от `App`, `Platform`, `Infrastructure`.
- `Infrastructure` может зависеть от `Core`.
- `Platform` может зависеть от `Core` (для DTO/контрактов), но не от `App`.
- `App` зависит от `Core`, `Infrastructure`, `Platform`.
- Запрещены циклические зависимости.

---

## 4) Архитектурные инварианты (нельзя нарушать)

### Единственный оркестратор состояния

- Любое действие (Connect/Disconnect/ApplyProxy/ApplyPAC/UpdateLists/ChangeServer/HotkeyToggle) выполняется **только** через `ConnectionOrchestrator`.
- Внутри: **очередь команд**, сериализация, debounce/coalesce, cancellation tokens.
- Состояния: `Disconnected / Starting / Connected / Stopping / Faulted`.

### Транзакционность proxy-настроек

- Применение системного прокси/AutoConfigURL делается **только** через `SystemProxyManager` в режиме транзакции:
  - `Snapshot -> Apply -> Commit` или `Snapshot -> Apply -> Rollback`
- При любом exception/timeout/kill/неожиданном exit `sslocal`:
  - **немедленный rollback** к snapshot.

### Порядок операций при подключении

1) Валидация профиля/портов/конфигов
2) Запуск `sslocal`
3) Health-check локального порта
4) Применение proxy/PAC (transaction apply + refresh)
5) Только после успеха: state=`Connected`

### Порядок при отключении

1) Transaction rollback proxy/PAC
2) Stop `sslocal` (graceful -> kill tree)
3) state=`Disconnected`

---

## 5) Стандарты качества кода

### Общие правила

- `Nullable` включён, предупреждения по возможности как ошибки.
- Никаких блокировок UI thread:
  - запрет `Task.Result`, `Wait()`, долгих операций в UI.
- Логи без секретов:
  - пароль/ключи/ss:// URI с секретом **никогда** не пишем в лог.
- Все I/O и сеть — таймауты, retry с backoff, ограничение размера входных данных.
- Все записи файлов — **атомарные** (temp -> replace).
- Любые публичные методы инфраструктуры должны быть cancellable где уместно.

### MVVM и UI

- ViewModels без Win32 interop напрямую.
- Навигация/диалоги через сервисы (интерфейсы в Core/App contracts, реализация в App).

---

## 6) UX/UI принципы (обязательные)

- Быстрый путь:
  - Трей: Connect/Disconnect, выбор сервера, режим Routing (Off/Global/PAC), открыть окно, Exit.
  - Хоткей: toggle connect/disconnect (по умолчанию Ctrl+Alt+Shift+P).
- Состояния UI:
  - `Starting/Stopping` — кнопки блокируются, показывается прогресс, при этом UI не фризится.
- Ошибки:
  - понятное сообщение пользователю + ссылка на Logs + кнопка Copy Diagnostics.
- Настройки:
  - автозапуск, автоконнект, хоткей (с детектом конфликтов), порты, уровни логов, поведение закрытия (minimize to tray).
- PAC:
  - предпросмотр итогового PAC,
  - тест “url/host -> результат FindProxyForURL”,
  - безопасный fallback: не применять невалидный PAC/списки.

---

## 7) Политики безопасности

- Никакой “скрытой” активности. Всё, что меняет системные настройки — явно отображается в UI.
- Подгружаемые правила/PAC из сети:
  - только HTTPS,
  - лимит размера,
  - валидация формата,
  - если обновление невалидно — не применять.
- Хранение секретов:
  - Credential Manager или DPAPI.
  - В settings JSON секретов быть не должно.

---

## 8) Команды сборки и тестирования (обязательный минимум)

> Все команды выполняются из корня репозитория.

### Быстрая проверка

- Сборка:
  - `dotnet build -c Release`
- Тесты:
  - `dotnet test -c Release`

### Полная проверка перед PR

1) `dotnet clean`
2) `dotnet build -c Release`
3) `dotnet test -c Release`
4) Ручная проверка (см. чеклист ниже)

### Что делать, если WinUI сборка падает в dotnet build

- Использовать MSBuild (VS Build Tools):
  - `msbuild VibeShadowsocks.sln /p:Configuration=Release /m`

---

## 9) Ручной QA чеклист (обязательно при изменениях в connection/proxy/pac/tray/hotkey)

### Подключение/отключение и восстановление proxy

- Connect -> убедиться, что:
  - `sslocal` запущен,
  - порт слушает,
  - proxy/PAC применился после health-check.
- Disconnect -> убедиться, что:
  - proxy/PAC вернулся к исходным значениям,
  - `sslocal` остановлен.

### Краш-устойчивость (критично)

- Во время `Connected` принудительно завершить `sslocal` (kill process):
  - приложение должно:
    - перейти в `Faulted`,
    - **сразу** восстановить proxy snapshot,
    - показать уведомление/статус.
- Принудительно завершить приложение (End Task) в Connected:
  - при следующем старте должно сработать crash recovery и восстановить proxy.

### Трей и хоткей

- Close window -> уходит в tray (если настройка включена).
- Tray Connect/Disconnect работает корректно и синхронно с UI состоянием.
- Хоткей:
  - toggle работает,
  - при конфликте хоткея показывается UI, приложение не падает.

### PAC

- ManagedPAC: обновление правил из URL/GitHub:
  - корректное кеширование,
  - невалидный контент не применяется,
  - тест FindProxyForURL возвращает ожидаемое.
- RemotePAC: установка AutoConfigURL на удалённый PAC URL.

### Списки и обновления

- UpdateLists не блокирует UI.
- При отсутствии сети/таймауте:
  - отображается ошибка,
  - система остаётся в рабочем состоянии.

---

## 10) Правила внесения изменений (Definition of Done)

Изменение считается готовым, только если:

- Проходит `dotnet build -c Release` и `dotnet test -c Release`.
- Не нарушены зависимости проектов (см. раздел 3).
- Добавлены/обновлены unit tests на ключевую логику (state machine, PAC генерация/парсинг правил, atomic write, обработка ошибок).
- Никаких секретов в логах/трассировке/исключениях.
- Обновлена документация (README/Settings UI подсказки) при изменении поведения.

---

## 11) Контракты стабильности (нельзя менять без миграций)

- Формат settings JSON: любые изменения только через версионирование и миграции.
- Поведение proxy transaction:
  - rollback обязан срабатывать при любой ошибке.
- Горячее обновление PAC/списков:
  - “сломанный” апдейт не должен ломать текущую конфигурацию.

---

## 12) Рекомендованный CI (если добавляется GitHub Actions)

- Workflow должен выполнять:
  - build Release
  - test Release
- Если UI сборка в hosted runner проблемна, допускается:
  - отделить Core/Infrastructure tests в CI,
  - UI сборку — через self-hosted Windows runner.

---

## 13) Примечания для агентов (Codex/автокод)

- Не добавлять новые зависимости без необходимости; если добавлены — фиксировать версии.
- Любые изменения в `SystemProxyManager`, `ConnectionOrchestrator`, `PacManager`, `SsLocalRunner`, `TrayManager`, `HotkeyManager` требуют прохождения полного ручного QA чеклиста.
- При сомнении — выбирать решение, которое сохраняет инварианты (rollback, single orchestrator, не блокировать UI).

---
