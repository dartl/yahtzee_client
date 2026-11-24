# -*- coding: utf-8 -*-
"""Generate Yahtzee UI design brief as .docx (stdlib only)."""
from __future__ import annotations

import zipfile
from pathlib import Path
from xml.sax.saxutils import escape

OUT = Path(__file__).resolve().parent / "TZ_UI_Dizayn_Yadzee.docx"

W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"


def p(text: str, *, bold: False | bool = False, size: int = 22, center: bool = False, color: str | None = None) -> str:
    runs = []
    for line in text.split("\n"):
        rpr = f'<w:rPr><w:sz w:val="{size}"/><w:szCs w:val="{size}"/>'
        if bold:
            rpr += "<w:b/><w:bCs/>"
        if color:
            rpr += f'<w:color w:val="{color}"/>'
        rpr += "</w:rPr>"
        runs.append(f"<w:r>{rpr}<w:t xml:space=\"preserve\">{escape(line)}</w:t></w:r>")
    jc = '<w:jc w:val="center"/>' if center else ""
    return f'<w:p><w:pPr>{jc}<w:spacing w:after="120"/></w:pPr>{"".join(runs)}</w:p>'


def h(text: str, level: int = 1) -> str:
    sizes = {1: 36, 2: 28, 3: 24}
    size = sizes.get(level, 24)
    return (
        f'<w:p><w:pPr><w:pStyle w:val="Heading{level}"/>'
        f'<w:spacing w:before="280" w:after="160"/></w:pPr>'
        f'<w:r><w:rPr><w:b/><w:sz w:val="{size}"/><w:szCs w:val="{size}"/>'
        f'<w:color w:val="1F4E3D"/></w:rPr>'
        f'<w:t>{escape(text)}</w:t></w:r></w:p>'
    )


def bullet(text: str) -> str:
    return (
        '<w:p><w:pPr><w:pStyle w:val="ListParagraph"/>'
        '<w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr>'
        '<w:spacing w:after="60"/></w:pPr>'
        f'<w:r><w:rPr><w:sz w:val="21"/><w:szCs w:val="21"/></w:rPr>'
        f'<w:t xml:space="preserve">{escape(text)}</w:t></w:r></w:p>'
    )


def comment_box(title: str) -> str:
    # Light yellow table cell for handwritten / Drive comments
    return f"""
<w:tbl>
  <w:tblPr>
    <w:tblW w:w="9000" w:type="dxa"/>
    <w:tblBorders>
      <w:top w:val="single" w:sz="8" w:color="C9A227"/>
      <w:left w:val="single" w:sz="8" w:color="C9A227"/>
      <w:bottom w:val="single" w:sz="8" w:color="C9A227"/>
      <w:right w:val="single" w:sz="8" w:color="C9A227"/>
    </w:tblBorders>
    <w:shd w:val="clear" w:fill="FFF8E7"/>
  </w:tblPr>
  <w:tr>
    <w:tc>
      <w:tcPr><w:tcW w:w="9000" w:type="dxa"/><w:shd w:val="clear" w:fill="FFF8E7"/></w:tcPr>
      {p(title, bold=True, size=20, color="8A6A00")}
      {p(" ", size=20)}
      {p(" ", size=20)}
      {p(" ", size=20)}
    </w:tc>
  </w:tr>
</w:tbl>
{p(" ", size=12)}
"""


def screen_block(
    code: str,
    title: str,
    purpose: str,
    elements: list[str],
    states: list[str],
    notes: list[str] | None = None,
) -> str:
    parts = [
        h(f"{code}. {title}", 2),
        p(f"Назначение: {purpose}", size=21),
        p("Элементы макета", bold=True, size=22),
    ]
    parts.extend(bullet(x) for x in elements)
    parts.append(p("Состояния / варианты", bold=True, size=22))
    parts.extend(bullet(x) for x in states)
    if notes:
        parts.append(p("Особые требования", bold=True, size=22))
        parts.extend(bullet(x) for x in notes)
    parts.append(comment_box("Комментарии заказчика (заполнить):"))
    parts.append(comment_box("Комментарии / вопросы дизайнера:"))
    return "".join(parts)


def body() -> str:
    chunks: list[str] = []

    chunks.append(p("ТЗ НА UI/UX ДИЗАЙН", bold=True, size=40, center=True, color="1F4E3D"))
    chunks.append(p("Мобильная / кроссплатформенная игра «Яцзи» (Yahtzee)", bold=True, size=26, center=True))
    chunks.append(p("Документ для подготовки макетов всех экранов клиента", size=20, center=True))
    chunks.append(p("Версия: 1.0  ·  Формат: редактируемый Word / Google Docs", size=18, center=True, color="666666"))
    chunks.append(p(" ", size=12))

    chunks.append(h("1. О проекте", 1))
    chunks.append(p(
        "Клиентская игра в кости Яцзи (Yahtzee) на Unity 6. Платформы: Android, iOS, PC, WebGL. "
        "Режимы: онлайн (2–4 игрока), офлайн против бота, Pass & Play на одном устройстве, "
        "локальная сеть Wi‑Fi, Bluetooth (не WebGL). Есть магазин скинов, кастомизация стола/костей, "
        "рейтинг, лобби, турниры, обучение."
    ))
    chunks.append(p(
        "Сейчас UI собран процедурно (временный «скелет»). Задача дизайнера — предложить "
        "цельный визуальный язык клуба/стола и отрисовать все экраны под реализацию в Unity."
    ))
    chunks.append(comment_box("Комментарии заказчика к общему видению продукта:"))

    chunks.append(h("2. Цели дизайна", 1))
    for item in [
        "Единый стиль «клубный стол» / премиальный casual — без шаблонного «фиолетового AI-глянца».",
        "Читаемость таблицы очков и костей на телефоне (портрет — приоритет) и на планшете/ПК (ландшафт).",
        "Крупные зоны тапа: бросок, удержание костей, запись категории.",
        "Понятная иерархия: бренд → действие → вторичные пункты.",
        "Отдельные макеты для ключевых состояний (загрузка, ошибка, пустой список, бан, дисконнект).",
        "Адаптив: safe area (вырез, home indicator), отдельные компоновки Portrait / Landscape для Gameplay.",
    ]:
        chunks.append(bullet(item))
    chunks.append(comment_box("Комментарии заказчика к целям / референсам (ссылки на Moodboard):"))

    chunks.append(h("3. Платформы и форматы выдачи", 1))
    for item in [
        "Основные артборды: Mobile Portrait 390×844 (или 360×800) — обязательный набор.",
        "Дополнительно: Tablet / Desktop Landscape для Gameplay и ключевых меню (по согласованию).",
        "Выдача: Figma (предпочтительно) + экспорт ключевых экранов PNG/PDF для согласования.",
        "Иконки UI: отдельный набор (онлайн, офлайн, настройки, магазин, кастомизация) — vector SVG/PDF.",
        "Слайсы для Unity: 2x/3x или @1/@2/@3 по договорённости; именование файлов латиницей.",
        "Этот Word-документ можно загрузить на Google Диск и править комментарии прямо в Docs.",
    ]:
        chunks.append(bullet(item))
    chunks.append(comment_box("Комментарии заказчика к формату сдачи:"))

    chunks.append(h("4. Визуальный язык (заполнить / согласовать)", 1))
    for item in [
        "Палитра: основные / акцентные / фон стола / текст / disabled (таблица цветов).",
        "Типографика: display для заголовков, UI для кнопок, mono/tabular для очков.",
        "Компоненты: primary button, secondary/glass, toggle, input, карточка меню, row таблицы очков.",
        "Фон меню vs фон игрового стола (разделить атмосферу лобби и партии).",
        "Моушн-заметки: появление экрана, запись очков, победа/поражение (описание, не анимация).",
    ]:
        chunks.append(bullet(item))
    chunks.append(comment_box("Референсы / запреты / бренд (заказчик):"))
    chunks.append(comment_box("Предложение дизайнера по стилю:"))

    chunks.append(h("5. Карта экранов (сводка)", 1))
    for row in [
        "A — Главное меню",
        "B — Онлайн-меню",
        "C — Офлайн-меню (+ Pass & Play выбор числа игроков)",
        "D — Поиск матча / матчмейкинг (+ турнир)",
        "E — Список лобби / Своё лобби",
        "F — Локальная сеть / Bluetooth",
        "G — Игровой стол (Gameplay) + оверлеи",
        "H — Обучение (tutorial поверх Gameplay)",
        "I — Итоги партии",
        "J — Магазин",
        "K — Кастомизация",
        "L — Настройки",
        "M — Таблица лидеров",
        "N — Бан / оверлей блокировки",
        "O — Потеря связи / переподключение",
        "P — Дисконнект соперника (1v1 выбор)",
        "Q — Реклама / IAP (плейсхолдеры UI)",
    ]:
        chunks.append(bullet(row))
    chunks.append(comment_box("Комментарии заказчика: какие экраны в приоритете v1:"))

    # --- Screens ---
    chunks.append(h("6. Детальные требования по экранам", 1))

    chunks.append(screen_block(
        "A", "Главное меню",
        "Точка входа: онлайн, офлайн, настройки, магазин, кастомизация, выход (PC/Android).",
        [
            "Логотип / название игры (hero-level, не мелкая подпись в шапке).",
            "Кнопки: «Онлайн игра», «Офлайн игра», «Настройки», «Магазин», «Кастомизация».",
            "Профиль: ник, монеты, MMR/рейтинг (карточка или полоска).",
            "Состояние offline / нет сети: заметка + панель «Переподключить».",
            "Кнопка «Выйти из игры» (desktop / Android).",
        ],
        [
            "Онлайн доступен / онлайн недоступен (кнопка онлайн disabled).",
            "Бан пользователя — переход на экран N, меню залочено.",
            "Первый запуск: может автозапускать обучение (логика клиента); визуально — обычное меню после skip.",
        ],
        ["Обучение в главном меню НЕ показывать — только через Офлайн."],
    ))

    chunks.append(screen_block(
        "B", "Онлайн-меню",
        "Выбор онлайн-режима.",
        [
            "Заголовок «Онлайн» + короткий подзаголовок.",
            "Пункты: Быстрая игра, Рейтинговый матч, Турнир, Своё лобби, Лидеры, Назад.",
        ],
        ["Обычный / акцент на «Быстрая игра» как primary CTA."],
    ))

    chunks.append(screen_block(
        "C", "Офлайн-меню",
        "Локальные режимы без сервера партии.",
        [
            "«Обучение (как играть?)» — отдельный пункт (важно).",
            "«Игра с ботом».",
            "«На одном телефоне» → вложенная панель: 2 / 3 / 4 игрока.",
            "«По локальной сети (Wi‑Fi)».",
            "«Bluetooth» (только где поддерживается; иначе скрыт).",
            "«Назад».",
        ],
        ["Панель Pass & Play поверх меню.", "Bluetooth hidden на Web."],
    ))

    chunks.append(screen_block(
        "D", "Матчмейкинг / поиск",
        "Ожидание соперника в очереди (quick / ranked / tournament).",
        [
            "Индикатор поиска (анимация/прогресс).",
            "Текст статуса (ищем… / найдено…).",
            "Тип очереди (быстрая / рейтинг / турнир).",
            "Кнопка отмены → назад в онлайн-меню.",
        ],
        ["Поиск", "Успешный матч (короткий переход)", "Ошибка / таймаут"],
    ))

    chunks.append(screen_block(
        "E", "Лобби: список и своё лобби",
        "Кастомные комнаты онлайн.",
        [
            "Список лобби: название, число игроков, кнопка войти / обновить.",
            "Пустой список / загрузка / ошибка.",
            "Создание лобби: поля настроек (по текущей логике клиента), старт, выход.",
            "Экран внутри комнаты: список игроков, готовность, хост-контроль.",
        ],
        ["Список", "Комната ожидания", "Старт партии"],
    ))

    chunks.append(screen_block(
        "F", "Локальная сеть / Bluetooth",
        "Хост / клиент для LAN или Bluetooth.",
        [
            "Выбор роли: создать стол / присоединиться.",
            "Список найденных столов / код подключения (как принято в текущем UX).",
            "Статус подключения, отмена, назад в офлайн-меню.",
        ],
        ["Поиск", "Подключено / ошибка", "Режим Bluetooth vs Wi‑Fi (бейдж)"],
    ))

    chunks.append(screen_block(
        "G", "Игровой стол (Gameplay) — ключевой экран",
        "Партия: кости, таблица очков, ход, запись категории.",
        [
            "3D/вид стола + зона костей (tray).",
            "Таблица очков: верх (1–6), низ (тройка, каре, фулл-хаус, стриты, шанс), Яцзи, итоги/бонус.",
            "Полоска игроков: имена, очки, чей ход (2–4 места; бот; Pass & Play).",
            "Кнопка «Бросок» / «Перебросить оставшиеся (n/3)».",
            "Кнопка «Записать: {категория}» (появляется после выбора строки).",
            "Тап по кости = удержание.",
            "Кнопка «В меню» + подтверждение сдачи/выхода.",
            "Подсветка выбранной и доступной строки; committed rows.",
        ],
        [
            "Portrait layout (таблица сверху/сбоку — по макету; кости в зоне стола).",
            "Landscape layout (таблица слева, стол справа — ориентир текущей логики).",
            "Ход игрока / ход соперника / ход бота.",
            "Онлайн таймер хода (если есть).",
            "Скин костей и стола (слоты под кастомизацию).",
        ],
        [
            "Приоритет читаемости чисел очков.",
            "Не перекрывать «Единицы» и кнопку «Записать» декоративными панелями.",
        ],
    ))

    chunks.append(screen_block(
        "H", "Обучение",
        "Сценарий поверх Gameplay: скриптовые кости, подсказки, цель — категории.",
        [
            "Панель подсказки на таблице (не над полем костей): заголовок урока, текст, «Закончить обучение».",
            "Позиция панели: низ таблицы (у Яцзи) для верхних уроков; верх таблицы для нижних категорий (стрит/шанс/яцзи).",
            "Подсветка целевой строки категории.",
            "Уроки: Единицы, Тройка, Фулл-хаус, Шанс, Большой стрит, Яцзи.",
        ],
        ["Старт урока", "Подсказка hold/roll/score (текст меняется)", "Завершение обучения → итоги"],
    ))

    chunks.append(screen_block(
        "I", "Итоги партии",
        "Результаты после матча / обучения.",
        [
            "Заголовок: Победа / Партия окончена / Обучение пройдено и т.п.",
            "Список игроков: место, ник, очки, MMR delta (если есть).",
            "Кнопки: «В меню», «Ещё раз» / «С ботом» (rematch).",
        ],
        ["Победа", "Поражение", "Ничья", "Обучение", "Forfeit"],
    ))

    chunks.append(screen_block(
        "J", "Магазин",
        "Покупка скинов за мягкую валюту / IAP-пакеты (если в объёме).",
        [
            "Баланс монет.",
            "Список товаров: превью, цена, купить / куплено.",
            "Статусы загрузки / ошибки.",
            "Назад.",
        ],
        ["Каталог", "Недостаточно монет", "Покупка OK"],
        ["Отдельно заложить место под IAP packs и Rewarded Video «получить монеты»."],
    ))

    chunks.append(screen_block(
        "K", "Кастомизация",
        "Экипировка купленных скинов костей/стола.",
        [
            "Список owned скинов, кнопка «Надеть» / «Надето».",
            "Превью на столе (желательно).",
            "Назад.",
        ],
        ["Пустой инвентарь", "Выбран активный скин"],
    ))

    chunks.append(screen_block(
        "L", "Настройки",
        "Звук, ник, документы.",
        [
            "Toggle Звук / Музыка.",
            "Поле никнейма + «Сохранить ник».",
            "Ссылки: Конфиденциальность, Условия.",
            "Назад.",
        ],
        ["Сохранение OK / ошибка сети"],
    ))

    chunks.append(screen_block(
        "M", "Таблица лидеров",
        "Рейтинг игроков онлайн.",
        [
            "Список: место, ник, MMR/очки.",
            "Подсветка локального игрока.",
            "Обновить / назад в онлайн-меню.",
        ],
        ["Загрузка", "Пусто", "Ошибка"],
    ))

    chunks.append(screen_block(
        "N", "Бан / блокировка",
        "Полноэкранный оверлей при USER_BANNED.",
        [
            "Заголовок, причина, срок (если есть).",
            "Без доступа к меню игры.",
        ],
        ["Временный бан", "Перманентный"],
    ))

    chunks.append(screen_block(
        "O", "Потеря связи",
        "Оверлей reconnect & catch-up.",
        [
            "Сообщение о потере соединения.",
            "Индикатор переподключения.",
            "Кнопка ручного retry (если нужна).",
        ],
        ["Поиск сети", "Восстановлено", "Не удалось — выход в меню"],
    ))

    chunks.append(screen_block(
        "P", "Дисконнект соперника (онлайн 1v1)",
        "Попап выбора при уходе оппонента.",
        [
            "Текст ситуации.",
            "«Закончить игру, забрать победу».",
            "«Играть против ИИ».",
            "Опционально «Вернуть ход» (если есть в логике).",
        ],
        ["Только 1v1; для 3–4 игроков — другой UX (заморозка места без бота)."],
    ))

    chunks.append(screen_block(
        "Q", "Монетизация (UI-плейсхолдеры)",
        "Не обязательно финальный креатив рекламы, но нужны рамки в UX.",
        [
            "Interstitial после матча (заглушка «реклама»).",
            "Rewarded: кнопка «Посмотреть и получить монеты».",
            "Состояние ad-free (подписка) — рекламные точки скрыты.",
            "IAP: паки монет, подписка без рекламы.",
        ],
        ["С рекламой", "Ad-free"],
    ))

    chunks.append(h("7. Общие оверлеи и системные состояния", 1))
    for item in [
        "Confirm: выход в меню / сдача.",
        "Handoff Pass & Play: «Передай телефон игроку N».",
        "Сложность бота: Новичок / Профессионал / Назад.",
        "Тосты/статус-строки ошибок сети (единый стиль).",
    ]:
        chunks.append(bullet(item))
    chunks.append(comment_box("Комментарии заказчика к оверлеям:"))

    chunks.append(h("8. Приоритеты сдачи", 1))
    for item in [
        "P0: Главное меню, Офлайн, Gameplay (portrait), Итоги, Обучение, Настройки.",
        "P1: Онлайн-меню, Матчмейкинг, Магазин, Кастомизация, дисконнект/reconnect.",
        "P2: Лобби, лидеры, LAN/Bluetooth, landscape Gameplay, IAP/ads frames.",
    ]:
        chunks.append(bullet(item))
    chunks.append(comment_box("Комментарии заказчика к приоритетам / дедлайнам:"))

    chunks.append(h("9. Чек-лист приёмки макетов", 1))
    for item in [
        "Все экраны из раздела 5 покрыты.",
        "Указаны состояния (loading/empty/error) для списков.",
        "Gameplay: portrait + (желательно) landscape.",
        "Компоненты кнопок/типографики описаны или в UI kit.",
        "Иконки меню в едином стиле.",
        "Safe area учтена.",
        "Тексты на русском (как в клиенте); длина строк проверена на узком экране.",
    ]:
        chunks.append(bullet(item))
    chunks.append(comment_box("Итог согласования (дата / кто утвердил):"))

    chunks.append(h("10. Контакты и процесс", 1))
    chunks.append(p("Канал связи: _______________________"))
    chunks.append(p("Ответственный со стороны продукта: _______________________"))
    chunks.append(p("Дизайнер: _______________________"))
    chunks.append(p("Ссылка на Figma: _______________________"))
    chunks.append(comment_box("Прочие договорённости:"))

    return "".join(chunks)


def document_xml() -> str:
    return f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="{W}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <w:body>
    {body()}
    <w:sectPr>
      <w:pgSz w:w="11906" w:h="16838"/>
      <w:pgMar w:top="1134" w:right="1134" w:bottom="1134" w:left="1134"/>
    </w:sectPr>
  </w:body>
</w:document>'''


CONTENT_TYPES = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
  <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
</Types>
"""

RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>
"""

DOC_RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
</Relationships>
"""

STYLES = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="{W}">
  <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
    <w:name w:val="Normal"/>
    <w:qFormat/>
    <w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:sz w:val="22"/></w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading1">
    <w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:qFormat/>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading2">
    <w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:qFormat/>
  </w:style>
  <w:style w:type="paragraph" w:styleId="ListParagraph">
    <w:name w:val="List Paragraph"/><w:basedOn w:val="Normal"/><w:qFormat/>
  </w:style>
</w:styles>
"""

NUMBERING = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:numbering xmlns:w="{W}">
  <w:abstractNum w:abstractNumId="0">
    <w:lvl w:ilvl="0">
      <w:start w:val="1"/>
      <w:numFmt w:val="bullet"/>
      <w:lvlText w:val="•"/>
      <w:lvlJc w:val="left"/>
      <w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr>
    </w:lvl>
  </w:abstractNum>
  <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
</w:numbering>
"""


def main() -> None:
    with zipfile.ZipFile(OUT, "w", compression=zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", CONTENT_TYPES)
        z.writestr("_rels/.rels", RELS)
        z.writestr("word/document.xml", document_xml())
        z.writestr("word/_rels/document.xml.rels", DOC_RELS)
        z.writestr("word/styles.xml", STYLES)
        z.writestr("word/numbering.xml", NUMBERING)
    print(OUT)


if __name__ == "__main__":
    main()
