# Фронт Парето (багатокритеріальна оптимізація за S та T)

- Алгоритм: NSGA-II (оцінок: `40`, точок на фронті Парето: `9`, час пошуку: `20.58` с)
- Параметри метрики: `alpha = 0.7`, `beta = 0.3`, `t_ref = 30 с`, `max_volume_bytes = 65536`

| # | Конфігурація (хромосома) | B_arc (Б) | Час t (с) | S (Стиснення) | T (Швидкодія) | Q (довідково) |
|---:|---|---:|---:|---:|---:|---:|
| 1 | `Parser=Dp, MaxChain=256, BlockSize=32768, SplitUnit=4096, DpSplit=off, Predict=on, Strategy=off` | 118519 | 1.4432 | 0.738738 | 0.954102 | 0.803347 |
| 2 | `Parser=Dp, MaxChain=32, BlockSize=32768, SplitUnit=4096, DpSplit=off, Predict=on, Strategy=off` | 120141 | 0.7997 | 0.736106 | 0.974037 | 0.807486 |
| 3 | `Parser=Dp, MaxChain=256, BlockSize=32768, SplitUnit=2048, DpSplit=off, Predict=off, Strategy=off` | 120414 | 0.7637 | 0.735665 | 0.975176 | 0.807519 |
| 4 | `Parser=Lazy, MaxChain=256, BlockSize=32768, SplitUnit=4096, DpSplit=off, Predict=on, Strategy=off` | 121575 | 0.2938 | 0.733795 | 0.990300 | 0.810747 |
| 5 | `Parser=Lazy, MaxChain=64, BlockSize=32768, SplitUnit=4096, DpSplit=off, Predict=on, Strategy=off` | 122567 | 0.2672 | 0.732205 | 0.991171 | 0.809895 |
| 6 | `Parser=Lazy, MaxChain=256, BlockSize=32768, SplitUnit=512, DpSplit=off, Predict=off, Strategy=off` | 123127 | 0.2082 | 0.731310 | 0.993109 | 0.809850 |
| 7 | `Parser=Lazy, MaxChain=64, BlockSize=32768, SplitUnit=4096, DpSplit=off, Predict=off, Strategy=off` | 124061 | 0.1905 | 0.729822 | 0.993689 | 0.808982 |
| 8 | `Parser=Lazy, MaxChain=64, BlockSize=32768, SplitUnit=4096, DpSplit=off, Predict=off, Strategy=on` | 129512 | 0.1816 | 0.721260 | 0.993984 | 0.803077 |
| 9 | `Parser=Lazy, MaxChain=4, BlockSize=32768, SplitUnit=2048, DpSplit=off, Predict=off, Strategy=off` | 130176 | 0.1521 | 0.720231 | 0.994957 | 0.802649 |

## Ключові точки компромісу
- Максимум стиснення `S`: `0.738738` (`T = 0.954102`, `118519` байтів) — `Parser=Dp, MaxChain=256, BlockSize=32768, SplitUnit=4096, DpSplit=off, Predict=on, Strategy=off`
- Максимум швидкодії `T`: `0.994957` (`S = 0.720231`, `0.1521` с) — `Parser=Lazy, MaxChain=4, BlockSize=32768, SplitUnit=2048, DpSplit=off, Predict=off, Strategy=off`
- Максимум зваженої метрики `Q`: `0.810747` (`S = 0.733795`, `T = 0.990300`) — `Parser=Lazy, MaxChain=256, BlockSize=32768, SplitUnit=4096, DpSplit=off, Predict=on, Strategy=off`
