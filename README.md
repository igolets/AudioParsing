# AudioParsing

AudioParsing — приложение для автоматической расшифровки аудио и видеофайлов.
Программа отправляет аудиодорожку в RouterAI для транскрибации Whisper, создает краткое содержание с помощью языковой модели и сохраняет результат в Markdown-файл рядом с исходным медиафайлом.

Проект включает графическое приложение для Windows и консольный интерфейс.

## Возможности

- Перетаскивание аудио- и видеофайлов в окно приложения.
- Рекурсивный поиск медиафайлов во вложенных папках.
- Поддержка аудиоформатов `.mp3`, `.m4a`, `.wav`, `.flac`, `.ogg`, `.opus`, `.wma`, `.aac` и других.
- Поддержка видеоформатов `.mp4`, `.m4v`, `.mkv`, `.mov`, `.avi`, `.webm`, `.wmv`, `.flv`, `.mpg`, `.3gp`, `.mts`, `.m2ts`.
- Извлечение аудиодорожки из видео и сжатие больших аудиофайлов через FFmpeg.
- Транскрибация через `openai/whisper-large-v3-turbo`.
- Суммаризация транскрипта через `openai/gpt-6-luna`.
- Сохранение краткого содержания, ключевых слов и полного транскрипта в соседний `.md`-файл.
- Пропуск уже обработанных файлов с возможностью принудительного повторного запуска.
- Отображение этапов обработки, ошибок и результатов в графическом интерфейсе.
- Настройка пути к FFmpeg и используемых моделей через окно настроек или `appsettings.json`.

## Скриншот

![Главное окно AudioParsing](docs/screenshot.png)

## Требования

- Windows для графического приложения.
- .NET SDK 10.0 или новее.
- FFmpeg.
- API-ключ RouterAI.

Консольный проект и библиотека ядра рассчитаны на `net10.0`. WPF-приложение рассчитано на `net10.0-windows`.

## Настройка

### 1. Установка FFmpeg

Установите FFmpeg и убедитесь, что исполняемый файл доступен по пути, указанному в настройках. Значение по умолчанию:

```text
C:\Program Files (x86)\ffmpeg\ffmpeg.exe
```

Путь можно изменить в окне «Настройки» или в файле `appsettings.json`:

```json
{
  "FfmpegPath": "C:\\Program Files (x86)\\ffmpeg\\ffmpeg.exe",
  "TranscriptionModel": "openai/whisper-large-v3-turbo",
  "SummaryModel": "openai/gpt-6-luna"
}
```

### 2. Настройка API-ключа

Приложение читает ключ RouterAI из переменной окружения `ANTHROPIC_AUTH_TOKEN`.

В PowerShell:

```powershell
$env:ANTHROPIC_AUTH_TOKEN = "ваш-api-ключ"
```

Не добавляйте API-ключ в репозиторий, `appsettings.json` или README.

### 3. Проверка подключения

Консольный режим поддерживает отдельную проверку подключения к RouterAI:

```powershell
dotnet run --project src/AudioParsing.Console -- --routerai-check
```

## Запуск графического приложения

Соберите и запустите WPF-приложение:

```powershell
dotnet run --project src/AudioParsing.Win
```

В открывшееся окно перетащите один или несколько поддерживаемых медиафайлов. Для каждого исходного файла будет создан файл с тем же именем и расширением `.md`.

Например:

```text
lecture.m4a  ->  lecture.md
interview.mp4 -> interview.md
```

Если Markdown-файл уже существует, файл пропускается. Для повторной обработки используйте консольный параметр `--force` или удалите существующий Markdown-файл.

## Консольный режим

Запуск обработки текущей папки:

```powershell
dotnet run --project src/AudioParsing.Console
```

Обработка указанной папки:

```powershell
dotnet run --project src/AudioParsing.Console -- "C:\Media\Lectures"
```

То же через именованный параметр:

```powershell
dotnet run --project src/AudioParsing.Console -- --folder "C:\Media\Lectures"
```

Выбор языка речи:

```powershell
dotnet run --project src/AudioParsing.Console -- --folder "C:\Media" --language ru
```

Автоматическое определение языка:

```powershell
dotnet run --project src/AudioParsing.Console -- --folder "C:\Media" --language auto
```

Принудительная повторная обработка:

```powershell
dotnet run --project src/AudioParsing.Console -- --folder "C:\Media" --force
```

Справка по параметрам:

```powershell
dotnet run --project src/AudioParsing.Console -- --help
```

## Формат результата

Для каждого медиафайла создается соседний Markdown-файл, содержащий:

- название исходного файла;
- дату обработки;
- краткое содержание;
- ключевые слова;
- полный текст транскрипции.

Файлы, которые не удалось обработать, не останавливают обработку остальных файлов. Их ошибки выводятся в консоль или журнал приложения.

## Сборка и тестирование

Сборка решения в конфигурации Release:

```powershell
dotnet build AudioParsing.sln --configuration Release
```

Запуск тестов:

```powershell
dotnet test AudioParsing.sln --configuration Release
```

Проверка форматирования:

```powershell
dotnet format AudioParsing.sln --verify-no-changes
```

## Структура проекта

```text
src/
  AudioParsing.Core/       Ядро: поиск файлов, FFmpeg, RouterAI и Markdown
  AudioParsing.Console/    Консольный интерфейс
  AudioParsing.Win/        WPF-интерфейс для Windows
tests/
  AudioParsing.Tests/      Тесты ядра и консольной конфигурации
  AudioParsing.Win.Tests/  Тесты WPF-моделей и сервисов
docs/                      Техническая документация проекта
```

## Лицензия

Проект распространяется по лицензии MIT. Текст лицензии находится в файле [LICENSE](LICENSE).

FFmpeg запускается как внешняя программа и не входит в состав проекта.
