# Лампа на C# / WPF

Черновик переписывания «Лампы» с Electron на Windows-приложение. Ветка `csharp`. Текущая Electron-версия остаётся в корне репозитория.

Остров не меняет размер HWND: окно сразу большого размера и прозрачное, капсула растёт внутри. Так hover не дерётся с ресайзом окна.

## Запуск

Нужен .NET 8 Desktop Runtime (или SDK):

```powershell
cd C:\Users\vladi\source\repos\seychas\csharp\Lamp
dotnet run
```

Собрать один exe:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Файл появится в `bin\Release\net8.0-windows\win-x64\publish\Lamp.exe`.

Готовый релиз: [Лампа C# 1.0.0](https://github.com/J0shGG/seychas/releases/tag/csharp-1.0.0) — скачай `Lamp-1.0.0.exe`. Это не latest для Electron: обновление `Seychas-Setup.exe` по-прежнему смотрит на v1.4.0.

## Что уже есть

- стол: задачи, круг, мысли, сегодня, неделя
- темы Лампа / Светлая / Тёмная / Чернила
- закрытие прячет окно в остров, выход только из трея
- остров: наведение, перетаскивание к краю, круг поверх полноэкранного приложения
- колокол, фон (дождь / огонь / кафе), громкость
- счётчик «N человек сейчас» через тот же ntfy-топик
- состояние в `%LocalAppData%\Lamp\state.json` (отдельное от Electron localStorage)

## Ещё не как в 1.4.0

- автообновление с GitHub (установщик Electron сюда не подходит)
- шрифты Cormorant Garamond и Manrope — пока Georgia и Segoe UI
- пиксель в пиксель CSS-стол

Electron-приложение на этой ветке не удаляли: можно сравнивать и переносить куски.
