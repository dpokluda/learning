# 09 — Practical training: pipelines, tuning, and debugging

The first eight modules were about understanding. This one is about competence — the difference between knowing what backpropagation is and being able to take a new problem and reliably get a working model out of it. That difference is mostly process, and the process is learnable.

The single most valuable thing here is a debugging discipline. When a model does not learn, the cause is a bug roughly three times out of four, and a modelling problem the remaining quarter. Beginners reverse those odds and reach for a bigger network or a different optimizer when they have actually forgotten `zero_grad()`. The checklist in this module is ordered by how often each item is the culprit, and running it takes about ten minutes.

By the end you will have the complete reference pipeline that reaches **98.28% test accuracy** on MNIST in 25 seconds, and — more usefully — the tools to know why any given run is failing.[^m9-final]

> **Prerequisite:** [Module 08](./08-initialization-and-normalization.md). This module assembles Modules 03 through 08 into a working practice and assumes all of them.

## The data pipeline

Everything starts with getting data into the model correctly, and two abstractions do all the work. A **Dataset** knows how to produce one example — it implements `__len__` and `__getitem__` — and a **DataLoader** wraps it to produce shuffled, batched, optionally parallel-loaded tensors.

The first real decision is normalization, and it is not optional. Module 08 showed that a carefully initialized network assumes unit-variance inputs; feeding it raw pixels in $[0,1]$ with a mean of 0.13 breaks that assumption in the very first layer. Compute the statistics from your **training split only** — using the test set's statistics is a small but genuine form of data leakage — and apply the same fixed numbers everywhere:

```python
import torch, math
from torchvision import datasets, transforms
from torch.utils.data import DataLoader, random_split

raw = datasets.MNIST("./data", train=True, download=True, transform=transforms.ToTensor())
s = sq = n = 0.0
for x, _ in DataLoader(raw, batch_size=2048):
    s += x.sum().item(); sq += (x ** 2).sum().item(); n += x.numel()
mean = s / n
std  = math.sqrt(sq / n - mean ** 2)
print(f"mean={mean:.4f} std={std:.4f}")     # mean=0.1307 std=0.3081
```

Those are the canonical MNIST constants you will see hard-coded in tutorials everywhere, now derived rather than copied.[^m9-stats] With them in hand, build the three splits — and note the validation split is carved out of the training data, never out of the test set:

```python
tf = transforms.Compose([
    transforms.ToTensor(),
    transforms.Normalize((0.1307,), (0.3081,)),
])
full = datasets.MNIST("./data", train=True, download=True, transform=tf)
train_ds, val_ds = random_split(full, [55000, 5000],
                                generator=torch.Generator().manual_seed(0))
test_ds = datasets.MNIST("./data", train=False, download=True, transform=tf)

train_loader = DataLoader(train_ds, batch_size=128, shuffle=True,
                          num_workers=4, pin_memory=True, drop_last=True)
val_loader   = DataLoader(val_ds,   batch_size=1000, shuffle=False)
test_loader  = DataLoader(test_ds,  batch_size=1000, shuffle=False)
```

Each DataLoader argument earns its place. `shuffle=True` on training only — shuffling matters because consecutive examples in a sorted dataset are correlated, and a batch of all-sevens gives a badly biased gradient; shuffling the validation or test set is pointless since you average over all of it anyway. `num_workers` spawns subprocesses to load and transform data in parallel, which matters enormously once your model is fast enough that data loading is the bottleneck — a symptom you can spot by watching GPU utilization sit near zero. `pin_memory=True` uses page-locked host memory for faster CPU-to-GPU transfer. `drop_last=True` discards a ragged final batch, which is worth doing when you use BatchNorm, since a final batch of size 3 produces terrible batch statistics.

A caution about `num_workers` that costs people an afternoon: on Windows and macOS, worker subprocesses re-import your main module, so a script with top-level training code will fork-bomb itself. Guard with `if __name__ == "__main__":`. In notebooks, `num_workers=0` is often the pragmatic choice.

## The training loop, annotated

Here is the loop in full. Every line is something Modules 05 through 08 explained, so read it as a summary rather than as new material.

```python
def train_one_epoch(model, loader, criterion, optimizer, device):
    model.train()                                    # dropout ON, BN updates running stats
    total, count = 0.0, 0
    for x, y in loader:
        x, y = x.to(device), y.to(device)            # model and data on the same device
        optimizer.zero_grad()                        # gradients accumulate; clear them
        logits = model(x)                            # forward, builds the graph
        loss = criterion(logits, y)                  # scalar at the graph root
        loss.backward()                              # reverse sweep fills every .grad
        torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)   # cheap insurance
        optimizer.step()                             # apply the update
        total += loss.item() * y.size(0)             # .item() — do NOT accumulate tensors
        count += y.size(0)
    return total / count

@torch.no_grad()                                     # no graph, no activation memory
def evaluate(model, loader, criterion, device):
    model.eval()                                     # dropout OFF, BN uses running stats
    total, correct, count = 0.0, 0, 0
    for x, y in loader:
        x, y = x.to(device), y.to(device)
        logits = model(x)
        total   += criterion(logits, y).item() * y.size(0)
        correct += (logits.argmax(1) == y).sum().item()
        count   += y.size(0)
    return total / count, 100.0 * correct / count
```

Four of those lines are the most common bugs in the field, and it is worth being able to say what each failure looks like. Forgetting `zero_grad()` gives erratic, worsening loss as the accumulated gradient grows without bound. Forgetting `model.eval()` gives evaluation accuracy that is noisy and inexplicably below training accuracy, because dropout is still firing and BatchNorm is still using batch statistics. Accumulating `loss` instead of `loss.item()` keeps every batch's computational graph alive for the whole epoch and produces an out-of-memory error that looks like a model-size problem. And mismatched devices raise a `RuntimeError` that is at least loud.

Assembled with the recommendations from Modules 06 through 08 — AdamW, cosine schedule, dropout, early stopping with checkpoint restoration — the complete recipe on the running example produced 94.72% validation accuracy after one epoch and 98.28% test accuracy after fifteen, in 25 seconds.[^m9-final] That is the baseline an MLP saturates at; Module 10 breaks past it with a better architecture rather than a better recipe.

## The single most useful test: overfit one batch

Before training on the full dataset, take one small batch — 16 or 32 examples — and train on *only* that batch for a few hundred steps. A correct model with a correct training loop will drive the loss essentially to zero, because it has more than enough capacity to memorize 32 examples. If it cannot, you have a bug, and you have found it in fifteen seconds instead of after an hour-long run.

On the running example, a single batch of 32 went from an initial loss of 2.2942 to 0.0013 after 50 steps and 0.00027 after 200.[^m9-overfit] That is what success looks like.

```python
x, y = next(iter(train_loader))
x, y = x[:32].to(device), y[:32].to(device)

model.train()
for step in range(200):
    optimizer.zero_grad()
    loss = criterion(model(x), y)
    loss.backward()
    optimizer.step()
    if step % 50 == 0:
        print(step, loss.item())
# expect the loss to reach ~1e-3 or below; if it plateaus, something is wrong
```

This test is the highest-value habit in this module because it cleanly separates *bugs* from *modelling problems*. If you cannot overfit 32 examples, no amount of regularization, architecture search, or learning-rate tuning will help — something in the pipeline is broken. If you *can*, the machinery works and any remaining trouble is about generalization, which is a different and more interesting problem.

Alongside it, check the **initial loss**. An untrained $K$-class classifier should output roughly uniform probabilities and therefore a loss near $\ln K$ — 2.303 for MNIST's ten classes. The measured initial loss was 2.2942. If yours starts at 8, your outputs are wildly scaled or your labels are misaligned; if it starts at 0.4, something is leaking the answer. This is a two-second check that catches a whole class of setup errors.

## Finding the learning rate

Module 06 established that the learning rate dominates every other hyperparameter. There is a fast systematic way to find it, due to Leslie Smith: start at an absurdly small rate, multiply it by a constant factor every batch until it is absurdly large, and plot the loss.[^m9-lrfind] The whole test takes a few hundred batches.

The measured curve on the running example, sweeping from $10^{-6}$ to $1$ over 300 batches:[^m9-lrfind-run]

| Learning rate | Loss |
|---|---|
| $1.0\times10^{-6}$ | 2.339 |
| $1.6\times10^{-5}$ | 2.259 |
| $2.5\times10^{-4}$ | 1.177 |
| $4.0\times10^{-3}$ | 0.320 |
| $1.0\times10^{-2}$ | **0.199 (minimum)** |
| $6.3\times10^{-2}$ | 1.188 |
| $9.6\times10^{-1}$ | 2.434 |

Read it as three regimes. At the low end nothing happens — the loss barely moves from its starting value. In the middle the loss falls steeply, and this is the useful range. Past the minimum it turns sharply upward as the steps begin to overshoot, and by $\eta \approx 1$ it is back above the untrained loss, thrashing. The conventional advice is to pick a rate about an order of magnitude below the minimum, or equivalently in the middle of the steepest descent — around $10^{-3}$ here, which is exactly the Adam default and exactly what the reference pipeline used.

```python
lrs, losses, lr = [], [], 1e-6
mult = (1.0 / 1e-6) ** (1 / 300)      # geometric sweep to lr=1 over 300 batches
model.train()
for i, (x, y) in enumerate(train_loader):
    if i >= 300: break
    for g in optimizer.param_groups: g["lr"] = lr
    x, y = x.to(device), y.to(device)
    optimizer.zero_grad()
    loss = criterion(model(x), y); loss.backward(); optimizer.step()
    lrs.append(lr); losses.append(loss.item()); lr *= mult
# plot losses against lrs on a log x-axis; pick ~10x below the minimum
```

Run this on a fresh model — the sweep leaves the parameters in a poor state — and re-run it if you substantially change the architecture or batch size.

## Tuning everything else

With the learning rate found, tune in descending order of impact: learning rate first and by a wide margin, then batch size and schedule, then architecture size, then regularization strength, then everything else. Do not tune activation functions or optimizer betas; the defaults are fine and your time is better spent elsewhere.

The one non-obvious result about *how* to search is that **random search beats grid search**, and by a lot. Bergstra and Bengio's argument is geometric rather than statistical: in a grid search over two hyperparameters with five values each, you evaluate only five distinct values of *each* parameter across 25 runs, because the grid repeats the same coordinates. Random search over the same 25 runs evaluates 25 distinct values of each. Since hyperparameter importance is typically very uneven — the learning rate matters enormously, some other knob barely at all — random search spends its budget resolving the important dimension finely while the grid wastes most of its runs re-testing values that differ only in an irrelevant coordinate.[^m9-bergstra] Sample scale-free parameters like learning rate and weight decay log-uniformly, since the meaningful question is the order of magnitude.

```python
import random
for trial in range(20):
    lr  = 10 ** random.uniform(-4.5, -2.0)     # log-uniform
    wd  = 10 ** random.uniform(-5, -1)
    drop = random.uniform(0.0, 0.5)
    hidden = random.choice([128, 256, 512])
    ...  # train briefly, record validation loss
```

Bayesian optimization (Optuna, Ray Tune) improves on random search by modelling the response surface and is worth reaching for when each run is expensive. For most learning-scale work, twenty random trials will get you close to the achievable ceiling.

Two disciplines make the search meaningful. **Change one thing at a time**, or you cannot attribute the result. And **keep a log** — a table of configuration and outcome — because the correlations you need are across runs and human memory is unreliable about this. The progress table in this repository's earlier MNIST project is a good model: every row records what changed and what happened.

## The debugging checklist

When the model will not learn, work this list in order. It is sorted by frequency, and most failures are resolved in the first five items.

**Does the loss start near $\ln K$?** If not, the problem is in the setup, not the training. Check for a stray softmax before `CrossEntropyLoss` (Module 04), an output layer with the wrong number of units, or unnormalized inputs producing enormous logits.

**Can it overfit one batch?** If not, stop and find the bug; nothing downstream will help.

**Are gradients actually flowing?** Print the gradient norm per layer after a backward pass. Any layer whose gradient norm is exactly zero is disconnected — a `detach()` in the wrong place, a `no_grad` block that swallowed the forward pass, or an activation that has saturated to death.

```python
loss.backward()
for name, p in model.named_parameters():
    if p.grad is None:
        print(f"{name}: NO GRADIENT")
    else:
        print(f"{name}: grad_norm={p.grad.norm():.3e}  param_norm={p.norm():.3e}")
```

Healthy networks show gradient norms within an order of magnitude or two across layers. A steep monotonic decay toward the input is the vanishing gradient of Module 08, and the fix is initialization, normalization, or residual connections.

**Is `zero_grad()` there, and is it inside the loop?** The symptom is a loss that decreases briefly and then behaves chaotically.

**Are `train()` and `eval()` called in the right places?** The symptom is a large, stable, unexplained gap between training and evaluation metrics on the *same* data.

**Are the labels aligned with the inputs?** An off-by-one from a shuffle applied to one but not the other produces a model that trains to exactly chance — which is the same signature as the random-label experiment in Module 07, and for the same reason. Print a few images with their labels and look at them.

**Is the learning rate in the right regime?** Loss stuck flat at $\ln K$ suggests too small (or dead ReLUs); loss to `nan` within a few steps means too large. Run the LR range test.

**Is anything leaking?** Validation accuracy that is suspiciously high, or higher than training accuracy, usually means duplicated examples across splits, normalization statistics computed over the full dataset, or a feature that encodes the target.

The useful quantities to monitor continuously, beyond the two loss curves, are the **gradient norm** (should be stable, not trending toward zero or exploding), the **update-to-weight ratio** $\eta\|\Delta\theta\|/\|\theta\|$ (a useful rule of thumb is around $10^{-3}$; much smaller means learning too slowly, much larger means instability), and the **fraction of dead ReLUs** (Module 03 — if a large share of units output zero for every input in a batch, lower the learning rate or switch to Leaky ReLU).

## Symptom-to-cause table

| What you see | Most likely cause | First thing to try |
|---|---|---|
| Loss pinned at $\ln K$ from step 1 | LR too small, dead units, or gradients not flowing | LR range test; print per-layer grad norms |
| Loss → `nan` in a few steps | LR too high, or a `log(0)`/division by zero | Divide LR by 10; add gradient clipping |
| Loss decreases then goes chaotic | Missing `zero_grad()`, or LR too high | Check the loop; lower LR |
| Train and val both high and flat | Underfitting — model too small or undertrained | Bigger model, longer training, less regularization |
| Train low, val high and rising | Overfitting | Module 07: augmentation, dropout, weight decay, early stopping |
| Eval much worse than train on same data | Forgot `model.eval()` | Check train/eval mode |
| GPU utilization near zero | Data loading is the bottleneck | Increase `num_workers`, `pin_memory=True` |
| Out of memory over time | Accumulating graph-connected tensors | `.item()` when logging; `torch.no_grad()` for eval |
| Cannot reproduce a result | Unseeded randomness | Seed Python, NumPy and torch (see `SETUP.md`) |

## Engineering that pays for itself

Three habits, each cheap.

**Checkpoint properly.** Save the model state dict, the optimizer state dict, and the epoch — the optimizer state matters because Adam's moment estimates take hundreds of steps to warm up, and resuming without them causes a visible loss spike.

```python
torch.save({"epoch": epoch,
            "model": model.state_dict(),
            "optimizer": optimizer.state_dict(),
            "best_val": best_val}, "checkpoint.pt")
```

**Log to a structure, not to stdout.** Even a list of dictionaries you convert to a table at the end beats scrolling through terminal output, and it makes plotting trivial. TensorBoard or Weights & Biases are worth the setup once you run more than a handful of experiments.

**Use mixed precision when models get large.** `torch.autocast` runs most operations in bfloat16 or float16 while keeping a float32 master copy of the weights, typically halving memory and substantially increasing throughput on modern GPUs. It matters not at all for MNIST and matters a great deal for anything in Module 12.

```python
scaler = torch.amp.GradScaler()
with torch.autocast(device_type="cuda", dtype=torch.float16):
    loss = criterion(model(x), y)
scaler.scale(loss).backward()
scaler.step(optimizer)
scaler.update()
```

## Before you move on

Competence here is a process, not a set of facts. Normalize inputs using training-split statistics; build three splits and keep the test set sealed. Write the loop with `zero_grad`, `train`/`eval`, `no_grad`, and `.item()` in the right places, because those four are most of the bugs. Before any real run, verify the initial loss is near $\ln K$ and that the model can drive a single batch to essentially zero loss — that one test separates bugs from modelling problems in fifteen seconds. Find the learning rate with a range test rather than by guessing, then tune everything else by random search, one change at a time, with a log.

If you can explain why "overfit one batch" is diagnostic rather than merely encouraging, why random search beats grid search on the same budget, and what a loss stuck exactly at 2.303 on a ten-class problem tells you, you have the practice. The measured artifacts in this module — 2.2942 initial loss, 0.00027 after 200 steps on one batch, an LR curve with its minimum at $10^{-2}$, 98.28% final test accuracy — are all reproducible from the code above, and reproducing them yourself is [Exercise Set 09](./exercises/09-exercises.md) — which also hands you five deliberately broken training scripts and asks you to diagnose each one from its symptom alone.

Next, [Module 10](./10-convolutional-networks.md) begins the architecture half of the book. The MLP has saturated this task at around 98%, and the way past it is not a better recipe but a function class that knows images have spatial structure.

## Sources

[^m9-final]: Measured while writing this module: 784→256→ReLU→Dropout(0.2)→10, AdamW at 1e-3 with weight decay 1e-2, cosine annealing over 15 epochs, gradient clipping at 1.0, batch size 128, 55k/5k train/val split, early stopping with best-checkpoint restore. Validation accuracy 94.72% after epoch 1, 98.20% after epoch 15; final test accuracy 98.28%, test loss 0.0581, 25 seconds on an Apple M-series GPU. Full script in the [Module 09 solutions](./exercises/solutions/09-solutions.md).

[^m9-stats]: Computed from the MNIST training split only: mean 0.1307, standard deviation 0.3081, matching the values conventionally hard-coded in PyTorch examples.

[^m9-overfit]: Measured: batch of 32, Adam at 1e-3. Initial loss 2.2942 (against $\ln 10 = 2.3026$), then 2.2770 after one step, 0.00131 at step 50, 0.00089 at step 100, 0.00027 at step 200.

[^m9-lrfind]: Leslie Smith, ["Cyclical Learning Rates for Training Neural Networks"](https://arxiv.org/abs/1506.01186), WACV 2017, Section 3.3, introduces the LR range test.

[^m9-lrfind-run]: Measured: geometric sweep from $10^{-6}$ to $1$ over 300 batches of size 128 with Adam, on a freshly initialized model. Minimum loss 0.199 at $\eta = 1.0\times10^{-2}$.

[^m9-bergstra]: James Bergstra and Yoshua Bengio, ["Random Search for Hyper-Parameter Optimization"](https://jmlr.org/papers/v13/bergstra12a.html), JMLR 13, 2012. Figure 1 makes the geometric argument visually and is worth two minutes of your time.

**Further reading.** Andrej Karpathy's ["A Recipe for Training Neural Networks"](https://karpathy.github.io/2019/04/25/recipe/) is a practitioner source (secondary, but from someone who has done this at scale) and is the best single document on this subject — the "overfit one batch" test and the become-one-with-the-data discipline come from it. *Deep Learning* [Chapter 11](https://www.deeplearningbook.org/contents/guidelines.html), "Practical Methodology," covers performance metrics, baselines, and when to gather more data. The [CS231n neural networks notes, part 3](https://cs231n.github.io/neural-networks-3/) cover gradient checks, sanity checks, monitoring the update-to-weight ratio, and hyperparameter search. PyTorch's [data loading tutorial](https://pytorch.org/tutorials/beginner/basics/data_tutorial.html) and [`torch.utils.data` docs](https://pytorch.org/docs/stable/data.html) are authoritative on Dataset/DataLoader semantics, and the [automatic mixed precision recipe](https://pytorch.org/tutorials/recipes/recipes/amp_recipe.html) covers `autocast` and `GradScaler` properly.
