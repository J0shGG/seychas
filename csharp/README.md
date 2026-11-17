# Лампа на C# / WPF

Переписывание «Лампы» с Electron на Windows-приложение. Ветка `csharp`. Текущая Electron-версия остаётся в корне репозитория для сравнения.

Остров не меняет размер HWND: окно сразу большого размера и прозрачное, капсула растёт внутри. Так hover не дерётся с ресайзом окна.

## Запуск

Нужен .NET 8 Desktop Runtime (или SDK):

```powershell
cd C:\Users\vladi\source\repos\seychas\csharp\Lamp
dotnet run
```

Собрать один exe:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Файл появится в `bin\Release\net8.0-windows\win-x64\publish\Lamp.exe`.

Для обновления Electron 1.4.0 этот же файл публикуется как `Seychas-Setup.exe` в latest-релизе GitHub.

## Релизы

- [Лампа 2.1.0](https://github.com/J0shGG/seychas/releases/tag/v2.1.0) — latest, `Seychas-Setup.exe`. Ставит C#-Лампу вместо Electron и переносит задачи из `seychas.v1`.
- [Лампа 2.0.0](https://github.com/J0shGG/seychas/releases/tag/v2.0.0) — предыдущий C#-установщик.
- [Лампа C# 1.0.0](https://github.com/J0shGG/seychas/releases/tag/csharp-1.0.0) — портативный `Lamp-1.0.0.exe`, не latest.

Установщик кладёт приложение в `%LocalAppData%\Programs\Seychas\Seychas.exe` и обновляет ярлыки «Лампа». Состояние — `%LocalAppData%\Lamp\state.json`. При первом запуске (и при установке) читается Chromium localStorage Electron (`%AppData%\Seychas` / `seychas`) и задачи, мысли, журнал дней сливаются в этот файл.

## Что есть

- стол: задачи, круг, мысли, сегодня, неделя
- темы Лампа / Светлая / Тёмная / Чернила
- закрытие прячет окно в остров, выход только из трея
- остров: наведение, перетаскивание к краю, круг поверх полноэкранного приложения
- колокол, фон (дождь / огонь / кафе), громкость
- счётчик «N человек сейчас» через тот же ntfy-топик
- переход с Electron 1.4.0: `Seychas-Setup.exe` как latest, перенос `seychas.v1`

## Ещё не как в 1.4.0

- шрифты Cormorant Garamond и Manrope — пока Georgia и Segoe UI
- пиксель в пиксель CSS-стол

Electron-приложение на этой ветке не удаляли: можно сравнивать и переносить куски.
