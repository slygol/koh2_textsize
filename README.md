# KoH2 Larger Text Patcher

Русский | [English](#english)

Предзагрузочный патч для **Knights of Honor II: Sovereign**, увеличивающий размер обычных надписей интерфейса на 25%.

Обычные моды KoH2 могут менять размеры только тех шрифтов, которые вынесены разработчиками в `.def`-файлы. Размер многих кнопок, панелей и других элементов записан непосредственно в Unity-префабах и недоступен системе модов игры. Этот patcher изменяет такие надписи во время запуска игры.

## Как это работает

Проект использует BepInEx 5. До загрузки игрового кода patcher изменяет находящуюся в памяти копию `Assembly-CSharp.dll` и добавляет масштабирование в `UIText.OnEnable`.

Когда Unity активирует надпись интерфейса, patcher:

- получает связанный компонент TextMeshPro;
- умножает `fontSize` на `1.25`;
- для текста с автоматическим подбором размера также увеличивает `fontSizeMin` и `fontSizeMax`;
- отмечает экземпляр как обработанный, чтобы не увеличивать его повторно при следующем открытии окна.

Оригинальный `Assembly-CSharp.dll` на диске **не изменяется**. Все изменения существуют только в памяти процесса и исчезают после закрытия игры.

Для элементов с особенно тесной компоновкой предусмотрены исключения: диалоговые окна `MessageWnd` и `AudienceWindow`, компактное значение благочестия и значение дохода еды в верхней панели остаются штатного размера.

## Установка

1. Скачайте архив из раздела **Releases**.
2. Распакуйте содержимое архива в корневую папку игры, где находится `Sovereign.exe`.
3. Подтвердите объединение папок, если Windows его запросит.
4. Отключите обычный мод **Larger Text (125%)** в меню `Settings → Mods`, чтобы одни и те же надписи не масштабировались дважды.
5. Запустите игру обычным способом.

После установки основные файлы должны находиться здесь:

```text
Knights of Honor II Sovereign/
├── Sovereign.exe
├── winhttp.dll
├── doorstop_config.ini
└── BepInEx/
    ├── core/
    └── patchers/
        └── KoH2.LargerText.Patcher.dll
```

Если BepInEx 5 уже установлен, достаточно поместить `KoH2.LargerText.Patcher.dll` в `BepInEx/patchers`. Не устанавливайте старый экспериментальный `KoH2.LargerText.Plugin.dll` из исходников.

## Проверка работы

После запуска откройте файл `BepInEx/LogOutput.log`. В нём должны присутствовать строки, подобные следующим:

```text
Loaded 1 patcher method from [KoH2.LargerText.Patcher ...]
Patching [Assembly-CSharp] with [KoH2.LargerText.Preloader.Patcher]
```

## Удаление

Удалите файл:

```text
BepInEx/patchers/KoH2.LargerText.Patcher.dll
```

Если BepInEx был установлен только для этого патча, его остальные файлы также можно удалить отдельно. Файлы самой игры восстанавливать не требуется: patcher их не перезаписывает.

## Сборка из исходников

Для сборки требуется .NET SDK и локальная копия BepInEx 5 в `vendor/BepInEx`.

Откройте PowerShell в папке проекта и выполните:

```powershell
./build_plugin.ps1
```

Готовый установочный пакет появится в папке `package`. Коэффициент увеличения задан константой `TextScale` в `PreloaderPatcher.cs`; после его изменения проект необходимо собрать заново.

В репозитории также сохранён `LargerTextPlugin.cs` — это прежний экспериментальный runtime-сканер. Текущий релиз использует только проект `KoH2.LargerText.Patcher.csproj`.

## Ограничения и совместимость

- Увеличение текста не увеличивает размеры кнопок и панелей. В тесных элементах возможны переносы строк или обрезание текста.
- Patcher рассчитан на конкретную структуру классов игры. После обновления KoH2, изменяющего `Assembly-CSharp.dll`, может потребоваться новая версия патча.
- Обрабатываются надписи, использующие игровой компонент `UIText`. Редкие TextMeshPro-компоненты, созданные в обход него, могут остаться без изменений.
- BepInEx является сторонним загрузчиком. Для сетевой игры рекомендуется использовать одинаковый набор файлов у всех участников либо временно удалить patcher.

---

## English

A preloader patch for **Knights of Honor II: Sovereign** that increases regular UI text by 25%.

Standard KoH2 mods can change only the font sizes exposed by the developers through `.def` files. Many buttons, panels, and other UI elements store their font sizes directly in Unity prefabs, outside the game's data-modding system. This patcher adjusts those labels while the game starts.

## How it works

The project uses BepInEx 5. Before the game code is loaded, the patcher modifies the in-memory copy of `Assembly-CSharp.dll` and adds text scaling to `UIText.OnEnable`.

Whenever Unity activates a UI label, the patcher:

- obtains its TextMeshPro component;
- multiplies `fontSize` by `1.25`;
- also scales `fontSizeMin` and `fontSizeMax` when auto-sizing is enabled;
- marks the instance as processed so reopening a window does not scale the same label again.

The original `Assembly-CSharp.dll` on disk is **never modified**. All changes exist only in the running process and disappear when the game is closed.

Some tightly constrained UI elements are excluded: labels inside `MessageWnd` and `AudienceWindow`, the compact Piety value, and the Kingdom Food income value in the top bar retain their original size.

## Installation

1. Download the archive from **Releases**.
2. Extract its contents into the game directory containing `Sovereign.exe`.
3. Allow Windows to merge the folders if prompted.
4. Disable the regular **Larger Text (125%)** mod under `Settings → Mods` to prevent some text from being scaled twice.
5. Start the game normally.

The main files should be arranged as follows:

```text
Knights of Honor II Sovereign/
├── Sovereign.exe
├── winhttp.dll
├── doorstop_config.ini
└── BepInEx/
    ├── core/
    └── patchers/
        └── KoH2.LargerText.Patcher.dll
```

If BepInEx 5 is already installed, place only `KoH2.LargerText.Patcher.dll` in `BepInEx/patchers`. Do not install the old experimental `KoH2.LargerText.Plugin.dll` found in the source tree.

## Verifying the installation

Start the game and open `BepInEx/LogOutput.log`. It should contain lines similar to:

```text
Loaded 1 patcher method from [KoH2.LargerText.Patcher ...]
Patching [Assembly-CSharp] with [KoH2.LargerText.Preloader.Patcher]
```

## Uninstallation

Delete:

```text
BepInEx/patchers/KoH2.LargerText.Patcher.dll
```

If BepInEx was installed only for this patch, its remaining files may be removed separately. No game files need to be restored because the patcher does not overwrite them.

## Building from source

Building requires the .NET SDK and a local BepInEx 5 copy under `vendor/BepInEx`.

Open PowerShell in the project directory and run:

```powershell
./build_plugin.ps1
```

The installation package will be created under `package`. The scale factor is the `TextScale` constant in `PreloaderPatcher.cs`; rebuild the project after changing it.

The repository also retains `LargerTextPlugin.cs`, an earlier experimental runtime scanner. Current releases build and use only `KoH2.LargerText.Patcher.csproj`.

## Limitations and compatibility

- Increasing the text size does not enlarge its buttons or panels. Text may wrap or be clipped in tightly constrained UI elements.
- The patcher targets the game's current class structure. A KoH2 update that changes `Assembly-CSharp.dll` may require an updated patch.
- It processes labels that use the game's `UIText` component. Rare TextMeshPro components created without that wrapper may remain unchanged.
- BepInEx is a third-party loader. For multiplayer, use the same files for all participants or temporarily remove the patcher.
