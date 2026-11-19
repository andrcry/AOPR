# SimpleArchiver

Архіватор файлів для Проєкту 2 «Розумний захищений архіватор». Програма:

- стискає файли (LZ77 з жадібним, лінивим або ДП-розбором і коди Гаффмана);
- шифрує архів паролем (PBKDF2 → AES-256-CTR) і захищає його від змін (HMAC-SHA256, Encrypt-then-MAC);
- ділить архів на томи заданого розміру (задача про рюкзак 0/1: ДП і жадібний алгоритм);
- визначає тип файла перцептроном і не стискає вже стиснуті файли;
- прогнозує наступний байт (залишки замість байтів) для растру і плавних даних;
- підбирає параметри генетичним алгоритмом, будує фронт Парето;
- відновлює файли побайтово точно або відмовляє з кодом помилки.

Формат архіву — `SARC` за специфікацією викладача `FORMAT_SPEC_P2.md`. Параметри команди (α, β, t_ref, розмір тому і блока, пріоритети типів, F1_min) програма сама читає з `data/team_config.json`.

## 1. Що потрібно встановити

.NET SDK версії 10. Перевірити:

```bash
dotnet --version
```

Має з'явитися число, що починається з `10`. Якщо ні — встановіть .NET 10 SDK з сайту Microsoft (на Windows після встановлення закрийте і знову відкрийте термінал).

**Усі команди нижче виконуються з кореневої папки проєкту** (там, де лежить `SimpleArchiver.slnx`): програма шукає `data/team_config.json`, `data/corpus` і `data/reference` відносно поточної папки.

## 2. Збірка і перевірка (з нуля)

```bash
dotnet build SimpleArchiver.slnx
dotnet test SimpleArchiver.slnx
dotnet test SimpleArchiver.slnx --collect:"XPlat Code Coverage"
dotnet run --project src/SimpleArchiver -- check
dotnet run --project src/SimpleArchiver -- ref-check data/reference
```

- `dotnet test` — усі автотести (мають пройти всі).
- `check` — контрольні точки алгоритмів: CRC32, Гаффман, контрольний блок специфікації, ДП-розбір, ДП-розбиття, рюкзак, градієнт перцептрона, прогноз байта, ГА, фронт Парето. Кожен рядок — `OK` або `ПОМИЛКА`.
- `ref-check` — еталонні архіви викладача (`ref_*.arc`) розпаковуються, а зіпсовані (`bad_*.arc`) відхиляються з очікуваним кодом із `expected_failures.json`. Очікується `Пройдено: 12 з 12`.

Щоб не писати щоразу `dotnet run --project src/SimpleArchiver --`, можна один раз зібрати і запускати готову програму:

```bash
dotnet build src/SimpleArchiver -c Release
src/SimpleArchiver/bin/Release/net10.0/SimpleArchiver check
```

## 3. Пароль

Програма бере пароль зі змінної середовища `SARC_PASSWORD`. Якщо її немає, пароль питається з клавіатури. Для розпакування потрібен той самий пароль; з іншим — відмова `E_HMAC`. Пароль еталонних архівів викладача — у `data/team_config.json` (`пароль_сеансу`).

macOS і Linux — пароль задається на початку команди:

```bash
SARC_PASSWORD=mypass dotnet run --project src/SimpleArchiver -- pack archive.arc notes.txt
```

Windows (PowerShell) — спочатку окремою командою, далі запускати без `SARC_PASSWORD=...`:

```powershell
$env:SARC_PASSWORD = "mypass"
dotnet run --project src/SimpleArchiver -- pack archive.arc notes.txt
```

У cmd: `set SARC_PASSWORD=mypass`.

## 4. Пакування

```
pack [опції] архів.arc файли_або_папки
```

```bash
SARC_PASSWORD=mypass dotnet run --project src/SimpleArchiver -- pack archive.arc data/corpus
```

Якщо все вміщується в один том, з'явиться `archive.arc`, інакше — `archive.part0.arc`, `archive.part1.arc`, … Папки розгортаються у файли; `labels.csv` і приховані файли пропускаються. В архів записуються лише імена файлів, без шляхів.

| Опція | Що робить |
|---|---|
| `--parser greedy` | жадібний розбір LZ77: у кожній позиції найдовший збіг |
| `--parser lazy` | лінивий розбір (за замовчуванням) |
| `--parser dp` | оптимальний ДП-розбір за фіксованими вартостями (9 біт літерал, 17 + … біт посилання) |
| `--split dp` | ДП-розбиття на блоки з власними таблицями Гаффмана |
| `--predict` | прогноз наступного байта; допомагає растру |
| `--model файл` | визначати тип файла навченим класифікатором (розділ 7) |
| `--strategy` | параметри стиснення за типом файла (лише разом з `--model`) |
| `--pad байти` | вирівняти розмір тому до кратного числа байтів (захист від аналізу метаданих) |
| `--pad-pow2` | вирівняти розмір тому до степеня двійки |
| `--dh мій.private чужий.public` | ключ із Діффі — Геллмана замість пароля (розділ 9) |

Приклад з усіма опціями стиснення:

```bash
SARC_PASSWORD=mypass dotnet run --project src/SimpleArchiver -- pack --parser dp --split dp --predict --model experiments/classifier.model --strategy archive.arc data/corpus
```

## 5. Розпакування

```
unpack папка том1.arc том2.arc ...
```

```bash
SARC_PASSWORD=mypass dotnet run --project src/SimpleArchiver -- unpack out archive.part0.arc archive.part1.arc
```

- томи можна передавати в будь-якому порядку;
- папка створюється сама; наявні файли не перезаписуються (помилка);
- спочатку перевіряється весь архів (HMAC кожного тому, структура, CRC32), і лише потім файли записуються на диск. Якщо архів зіпсований, у папці не з'явиться жодного файла.

Перевірити еталонний архів викладача вручну:

```bash
SARC_PASSWORD=algo2026 dotnet run --project src/SimpleArchiver -- unpack out_ref data/reference/ref_04_seven_groups.arc
```

## 6. Метрика Q і звіт

```
Q = alpha·S + beta·T,   S = B_orig / (B_orig + B_arc),   T = t_ref / (t_ref + t)
```

`t` — пакування + розпакування; якщо хоч один файл не відновлено побайтово, `Q = 0`. α, β, t_ref — з `data/team_config.json`.

| Команда | Що робить |
|---|---|
| `q [опції як у pack] [--log файл] [файли_або_папки]` | базове Q однієї конфігурації (без файлів — `data/corpus`), лог прогону в `experiments/corpus_run.log` |
| `report` (або `report-all`) `[--quick] [--ga-pop N] [--ga-gen N]` | **усі таблиці звіту** в `experiments/FULL_REPORT_TABLES.md` + лог `experiments/corpus_run.log` (≈ 3 хв; `--quick` — без запуску ГА) |
| `measure файли_або_папки` | таблиця 1: жадібний, лінивий і ДП-розбір LZ77, розбиття на блоки, підсумок за групами |
| `q-table [--model файл] [файли_або_папки]` | таблиця 2: вплив кожного блока на Q (кожен блок вимикається окремо від повної системи), файл `experiments/q_table.tsv` |
| `knapsack-table [--volume байти] [--model файл] файли_або_папки` | таблиця 3: ДП-рюкзак проти жадібного на першому томі |
| `crypto-order [файли_або_папки]` | таблиця 4: стиснути → зашифрувати проти зашифрувати → стиснути, ентропія до і після AES |
| `ga [--advanced] [--pop N] [--gen N] [--seed N] [--out файл]` | таблиця 5: ГА проти випадкового пошуку (з `--advanced` — повна хромосома, рівномірний кросовер, адаптивна мутація, локальний пошук); журнал поколінь `experiments/ga_generations.tsv` |
| `pareto [--pop N] [--gen N]` | NSGA-II за (S, T), фронт Парето в `experiments/pareto_front.md` |
| `attack [--cipher xor\|vigenere] [--key текст] [--pad байти] [файли_або_папки]` | модуль атаки: злам XOR/Віженера ГА, атака на метадані та перевірка `E_HMAC` |
| `entropy-table файли_або_папки` | ентропія байтів і залишків прогнозу за групами |
| `leak-table [--pad байти] файли_або_папки` | витік метаданих через розміри томів і вартість вирівнювання |

Групи файлів корпусу беруться з `labels.csv` у папці файлів. Таблиці друкуються через табуляцію — їх можна вставити в Excel або Google Таблиці.

Найшвидший спосіб оновити всі таблиці звіту:

```bash
dotnet run -c Release --project src/SimpleArchiver -- report
```

## 7. Класифікатор типу файла

Перцептрон за 52 ознаками файла (ентропія, гістограма байтів, частка ASCII, відстань між кінцями рядків, автокореляція на лагах 1–16, ентропія біграм) визначає тип:

| № | Тип | № | Тип |
|---|---|---|---|
| 0 | природний текст | 4 | виконуваний файл |
| 1 | вихідний код | 5 | вже стиснуте (записується без стиснення) |
| 2 | структуровані дані | 6 | штучні дані |
| 3 | растр без стиснення | | |

Навчання за протоколом з `team_config.json` (train/ для навчання, вибір моделі за macro-F1 на valid/ з кількох ініціалізацій):

```bash
dotnet run --project src/SimpleArchiver -- train data/train data/valid model.txt
dotnet run --project src/SimpleArchiver -- classify model.txt data/corpus
```

`train` друкує macro-F1, матрицю помилок і порівняння з F1_min. Готова модель після `report` лежить у `experiments/classifier.model`. Формат `labels.csv` від викладача: `file,type,type_name,size`.

## 8. Файли від викладача

| Файл або папка | Що це |
|---|---|
| `data/team_config.json` | параметри команди: α, β, t_ref, max_volume_bytes, block_size, пріоритети типів, F1_min |
| `data/corpus/` + `labels.csv` | робочий корпус: 21 файл, по 3 з кожної з 7 груп |
| `data/train/`, `data/valid/` + `labels.csv` | 350 і 105 файлів з мітками типів |
| `data/reference/ref_*.arc` + `.json` | еталонні архіви з переліком файлів, розмірами і CRC32 |
| `data/reference/bad_*.arc` | зіпсовані архіви; очікувані коди — у `expected_failures.json` |

Якщо файлів викладача немає, команда `demo-data [--seed N] папка` створює замінник для демонстрації. `team-config` показує, які параметри програма прочитала.

## 9. Передати архів колезі без пароля (Діффі — Геллман)

```bash
dotnet run --project src/SimpleArchiver -- dh-keys alice
dotnet run --project src/SimpleArchiver -- dh-keys bob
dotnet run --project src/SimpleArchiver -- pack --dh alice.private bob.public archive.arc data/corpus
dotnet run --project src/SimpleArchiver -- unpack --dh bob.private alice.public out archive*.arc
```

Кожен передає іншому лише свій `.public`; `.private` нікому не передається. Обидві сторони отримують той самий спільний секрет, з якого робиться пароль архіву.

## 10. Коди помилок

Якщо архів неправильний, програма друкує `Архів відхилено.` і код:

| Код | Що означає |
|---|---|
| `E_SHORT` | файл тому коротший за 73 байти (41 заголовок + 32 HMAC) |
| `E_MAGIC` | це не архів SARC або інша версія формату |
| `E_LENGTH` | довжина тому не збігається з записаною в заголовку (архів обрізаний) |
| `E_HMAC` | невірний пароль або архів змінено |
| `E_PAYLOAD` | неправильний вміст: поля, reserved ≠ 0, небезпечні імена (`../`, `/`, `\`, `:`) |
| `E_CODE` | неправильні коди Гаффмана або зіпсований бітовий потік |
| `E_CRC` | контрольна сума відновленого файла не збіглася |
| `E_VOLUMES` | бракує тому, том передано двічі або томи з різних архівів |

Код завершення: 0 — успіх, 2 — архів відхилено, 1 — інша помилка (немає файла, немає доступу, неправильні опції).

## 11. Структура проєкту

```
SimpleArchiver.slnx          рішення: програма і тести
src/SimpleArchiver/
  Common/                    помилки, налаштування, team_config.json, CRC32, читання файлів
  Huffman/                   біти, дерево Гаффмана, канонічні коди, таблиці кодів
  Compression/               LZ77 (жадібний, лінивий, ДП), кодування блока, ДП-розбиття на блоки
  Analysis/                  ознаки, перцептрон, класифікатор, прогноз байта, метрика Q, таблиці звіту, report
  Crypto/                    ключі (PBKDF2), AES-CTR, HMAC, Діффі — Геллман, витік метаданих, порядок операцій, модуль атаки (`WeakCipherAttack`)
  Format/                    заголовок тому, таблиці файлів і блоків, вирівнювання
  Packing/                   рюкзак, розкладання блоків по томах
  Optimization/              генетичний алгоритм, випадковий і локальний пошук, NSGA-II
  Archive/                   створення і читання архіву, запис на диск, перевірка еталонних архівів
  Demo/                      замінник файлів викладача
  Program.cs                 команди програми
  Checks.cs                  контрольні точки для check
tests/SimpleArchiver.Tests/  автотести
data/                        матеріали викладача
experiments/                 результати експериментів і таблиці звіту
docs/decisions.md            лог інженерних рішень
docs/report/REPORT.md        фінальний аналітичний звіт з таблицями і висновками
META-USAGE.md                звіт про використання ШІ та верифікацію командою (п. 11.4)
.github/workflows/ci.yml     збірка і тести на GitHub Actions
```

## 12. Часті проблеми

- **`dotnet` не знайдено** — встановіть .NET 10 SDK і відкрийте новий термінал.
- **`файл або папку не знайдено: data/corpus`** — запускайте команди з кореневої папки проєкту.
- **`Помилка файлу: ... вже існує`** — у папці розпакування вже є такий файл; візьміть іншу папку.
- **`E_HMAC` при правильному паролі** — перевірте `SARC_PASSWORD` (без зайвих пробілів). На Windows краще латиниця: кирилиця в старому терміналі може прочитатися в іншому кодуванні.
- **`E_VOLUMES`** — передайте всі томи архіву, кожен по одному разу.
- **`вибірка порожня: перевірте labels.csv`** — у `labels.csv` немає рядків або імена не збігаються з файлами.
- **Кілька файлів через `*` на Windows** — PowerShell не розгортає `*`; передавайте папку (`data/corpus`) — програма розгорне її сама.
- **Незрозумілі символи замість українських літер у cmd** — виконайте `chcp 65001`.

## 13. Команди оптимізації, звіту, атаки та покриття тестів

Швидкий набір команд для демонстрації на захисті проєкту:

```bash
# Базовий та просунутий генетичний алгоритм (порівняння з Random Search і Hill Climbing)
dotnet run -c Release --project src/SimpleArchiver -- ga data/corpus
dotnet run -c Release --project src/SimpleArchiver -- ga --advanced data/corpus

# Багатокритеріальна оптимізація за (S, T) — фронт Парето (NSGA-II)
dotnet run -c Release --project src/SimpleArchiver -- pareto data/corpus

# Повний прогін усіх експериментів та генерація таблиць звіту
dotnet run -c Release --project src/SimpleArchiver -- report-all

# Модуль атаки: злам XOR/Віженера через ГА, атака на метадані та перевірка E_HMAC
dotnet run -c Release --project src/SimpleArchiver -- attack data/corpus

# Збір покриття автотестів (XPlat Code Coverage, Cobertura XML у TestResults/)
dotnet test SimpleArchiver.slnx --collect:"XPlat Code Coverage"
```
