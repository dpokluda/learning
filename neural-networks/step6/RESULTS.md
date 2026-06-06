# Step 6 — Experiment Log

The "real learning" step: change **one knob at a time**, measure, write down what happened.

Setup unless noted: MLP, `CrossEntropyLoss`, batch size 64, `torch.manual_seed(0)`, test
accuracy evaluated after every epoch. Train loss is an EMA of per-batch loss (noisy — trust
test accuracy as the real signal).

---

## Experiment 1 — More epochs (baseline, SGD)

Model: `Linear(784,128) → ReLU → Linear(128,10)` · Optimizer: SGD lr=0.01 · 15 epochs

| epoch | train loss | test acc |
|------:|-----------:|---------:|
| 1  | 0.619 | 86.55% |
| 5  | 0.306 | 91.45% |
| 10 | 0.273 | 93.00% |
| 13 | 0.188 | 93.61% |
| 14 | 0.206 | 93.87% |
| 15 | 0.237 | 94.01% |

**Finding:** Still climbing at epoch 15 (no plateau). The Step 4 model wasn't weak, it was
**undertrained** — same architecture, more steps → 87% → 94%. But SGD lr=0.01 is crawling
toward the plan's promised ~97%; the optimizer is the bottleneck, not the model.

---

## Experiment 2 — Optimizer swap: SGD → Adam

Same model · Optimizer: **Adam lr=0.001** · 15 epochs

| epoch | train loss | test acc |
|------:|-----------:|---------:|
| 1  | 0.197 | 94.51% |
| 3  | 0.113 | 96.96% |
| 8  | 0.038 | 97.77% |
| 13 | 0.014 | **97.93%** |
| 15 | 0.010 | 97.77% |

**Finding:** Adam is dramatically faster — **epoch 1 (94.5%) already beats SGD's entire
15-epoch run.** Hits ~97% by epoch 3, plateaus ~97.9% by epoch ~8. First clear sign of
**mild overfitting**: train loss keeps falling (0.021 → 0.010) while test acc stalls and
even dips. Best checkpoint (97.93% @ ep13) is mid-run, not the end — more epochs isn't free.

---

## Experiment 3 — Learning-rate sweep (Adam, 15 epochs)

| lr     | best test acc | behavior |
|--------|--------------:|----------|
| 0.001  | **97.9%**     | smooth climb, clean plateau (sweet spot) |
| 0.01   | ~97.0%        | works but jittery — bounces, never settles |
| 0.1    | ~73%          | thrashing — loss stuck ~0.9–1.2, never converges |
| 1.0    | ~10%          | **collapsed** — loss pinned at ln(10)≈2.30, acc = chance |

`lr=1.0` detail (loss ~2.35–2.39 every epoch, acc 9–11%): the model emits a uniform
distribution over 10 classes — pure guessing.

**Finding:** The canonical curve **good → noisy → broken → dead**. Two failure signatures
worth recognizing:
- **Loss → NaN** = exploded (typical of plain SGD with too-high lr).
- **Loss stuck ~2.30, acc ~10%** = collapsed to uniform guessing. Adam's `sqrt(variance)`
  normalization caps step size, so at lr=1.0 it collapses to chance rather than NaN-ing.

Debugging bonus: lr=0.1 and lr=1.0 first produced byte-identical output → with a fixed seed
that's impossible across different lrs, which exposed a stale-config bug. Reproducibility is
a debugging tool.

---

## Experiment 4 — Add a second hidden layer

Model: `Linear(784,128) → ReLU → Linear(128,128) → ReLU → Linear(128,10)` · Adam lr=0.001 · 15 epochs

| epoch | train loss | test acc |
|------:|-----------:|---------:|
| 1  | 0.186 | 94.87% |
| 6  | 0.033 | 97.74% |
| 14 | 0.016 | **97.81%** |
| 15 | 0.019 | 97.81% |

**Finding:** Extra depth bought **~0 gain** (97.81% vs 97.93% with one hidden layer — a hair
worse). The MLP is **saturated on MNIST at ~98%**; the bottleneck is that an MLP discards
the image's spatial structure (what a CNN fixes in Step 7). Lesson: "make it bigger" is not
a reliable knob.

---

## Experiment 5 — Dropout sweep (2 hidden layers, Adam lr=0.001, 15 epochs)

`nn.Dropout(p)` after each ReLU. Requires correct `model.train()` / `model.eval()` toggling
(train = dropout on, eval = dropout off) — already handled in the training loop.

| dropout p | train loss (end) | best test acc | verdict |
|----------:|-----------------:|--------------:|---------|
| 0.0       | ~0.016 | 97.81% | overfits slightly |
| **0.2**   | ~0.035 | **98.07%** | **just right — first run past 98%** |
| 0.5       | ~0.13  | 97.54% | too strong — underfits |

p=0.2 epoch tail: 97.71 → 97.99 → 98.02 → 98.06 → 98.07 → 97.98 → 98.01 (still climbing,
clean).

**Finding:** Regularization has a sweet spot. p=0.5 raised train loss 8× and suppressed
overfitting but *hurt* test acc (too aggressive on a barely-overfitting problem); p=0.2
landed train loss in the middle and **pushed test acc to 98.07%, the best of every
experiment**. Lesson contradicts the naïve "dropout is good practice" instinct: add
regularization only when overfitting is **measurable**, and tune the dose.

---

## Summary — the five axes

| Axis | What it taught |
|------|----------------|
| **Epochs** | The model was undertrained, not weak. Watch for the plateau. |
| **Optimizer** | Adam ≫ plain SGD here — faster convergence, reached ceilings SGD couldn't. |
| **Learning rate** | Most sensitive knob. Sweet spot → noisy → thrashing → collapsed. |
| **Capacity (depth)** | Bigger ≠ better. MLP saturated ~98% on MNIST. |
| **Regularization** | Dropout has a sweet spot (0.2). Only worth it with real overfitting. |

**Best result: 98.07%** — 2-hidden-layer MLP, Adam lr=0.001, Dropout(0.2), 15 epochs.

**Next (Step 7):** CNN to break past the ~98% MLP ceiling by exploiting spatial structure.
