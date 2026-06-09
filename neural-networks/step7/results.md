# Step 7 — Convolutional Network

The stretch goal: replace the MLP with a small CNN that exploits the image's spatial
structure, and push past the ~98% MLP ceiling from Step 6.

Setup unless noted: `CrossEntropyLoss`, batch size 64. Train loss is an EMA of per-batch
loss (noisy — trust test accuracy as the real signal).

---

## The architecture

```
Input                          [N,  1, 28, 28]
Conv2d(1, 32, 3, padding=1)    [N, 32, 28, 28]   # 32 filters → 32 feature maps
ReLU
MaxPool2d(2)                   [N, 32, 14, 14]    # halve H,W
Conv2d(32, 64, 3, padding=1)   [N, 64, 14, 14]    # in=32 (matches prev out)
ReLU
MaxPool2d(2)                   [N, 64,  7,  7]     # halve again
Flatten                        [N, 64*7*7 = 3136]
Linear(3136, 128) → ReLU → Linear(128, 10)        # same head as the Step 5 MLP
```

Key reframe vs. the MLP: the MLP flattened **first** and reasoned over raw pixels, throwing
away which pixels neighbor which. The CNN does its spatial reasoning **first** (convolutions
learn local edge/curve/blob detectors, reused at every position via parameter sharing), then
flattens the learned features and hands them to an MLP-style head for the final 10-class call.

---

## Experiment 1 — CNN with SGD lr=0.01, 5 epochs

| stage | train loss |
|------:|-----------:|
| initial | 2.300 (= ln 10 ✓) |
| epoch 1 | 0.363 |
| epoch 3 | 0.176 |
| epoch 5 | 0.096 |

**Test accuracy: 96.81%**

**Finding:** A *worse* result than the Step 5 MLP (~97%) — but not because the CNN is weaker.
The loss was still in free-fall at epoch 5 (0.141 → 0.096, no plateau): the model was
**undertrained**. Same lesson as Step 4 — SGD lr=0.01 crawls, and on a CNN's larger, harder
loss landscape it crawls more. *Never read final accuracy without checking whether the loss
curve has plateaued.*

---

## Experiment 2 — Optimizer swap: SGD → Adam (lr=0.001)

**5 epochs**

| stage | train loss |
|------:|-----------:|
| initial | 2.306 |
| epoch 1 | 0.075 |
| epoch 3 | 0.029 |
| epoch 5 | 0.027 |

**Test accuracy: 99.10%** — target hit.

**10 epochs**

| stage | train loss |
|------:|-----------:|
| epoch 1  | 0.050 |
| epoch 5  | 0.019 |
| epoch 8  | 0.005 |
| epoch 10 | 0.006 |

**Test accuracy: 99.10%** — identical.

**Finding:** Adam reaches in 1 epoch what SGD couldn't in 5. The single optimizer swap moved
the model from 96.81% → 99.10%, confirming Experiment 1 was an optimization problem, not an
architecture one.

---

## The headline lesson — diminishing returns / overfitting

| run | final train loss | test accuracy |
|-----|-----------------:|--------------:|
| Adam, 5 epochs  | 0.027 | **99.10%** |
| Adam, 10 epochs | 0.006 | **99.10%** |

Doubling the epochs drove train loss **4.5× lower** (0.027 → 0.006) and moved test accuracy
**zero**. Those extra epochs were the model *memorizing the training images harder* — precision
that does not transfer to unseen data. This is the textbook signature of **overfitting /
diminishing returns**: train loss keeps falling while test performance flatlines. When you see
that divergence, more training is wasted effort at best.

The last ~1% of MNIST is a genuine ceiling for this approach (ambiguous/mislabeled digits).
Pushing past 99% needs *different* tools (augmentation, dropout, LR schedules, ensembles), not
more of the same.

---

## The whole arc

| model | test accuracy | what bought the gain |
|-------|--------------:|----------------------|
| Linear(784,10)            | ~92%   | baseline |
| MLP + Dropout (Step 6)    | ~98%   | depth + nonlinearity |
| **CNN (this step)**       | **99.1%** | exploiting spatial structure |

Each architectural jump bought *real* generalization — unlike epoch-doubling, which bought
nothing. That contrast is the point: you now have firsthand evidence for why CNNs dominated
computer vision.

---

## Sanity checks that passed

- **Initial loss = 2.300 = ln(10).** Untrained net outputs a ~uniform distribution over 10
  classes → `-ln(1/10) = ln(10)`. Generalizes to `ln(num_classes)`; the standard first-batch
  diagnostic.
- **Shape contract.** Conv layers need the 4-D `[N,1,28,28]` input — so the manual flatten the
  MLP used (`images.view(-1, 784)`) had to be removed from **both** `train()` and `evaluate()`;
  `nn.Flatten()` inside the model handles flattening *after* the conv work. (Missing the second
  call site is an easy bug — it trains fine, then crashes at eval.)
