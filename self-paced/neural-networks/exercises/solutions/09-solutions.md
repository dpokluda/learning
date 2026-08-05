# Solutions — Set 09

Worked answers for [Exercise Set 09](../09-exercises.md).

## Part A — Answers

**1. Loss pinned at 2.303.**

$\ln 10 = 2.3026$ is the loss of a uniform predictor over ten classes, so the model is outputting the same distribution for every input and never changing. Four causes, in decreasing order of likelihood.

The learning rate may be far too small, so the updates are numerically real but too tiny to move the decision boundary. The learning rate may be too *large* in a specific way — large enough that the first update saturated every unit into a dead state, after which nothing recovers. Gradients may not be reaching the parameters at all: a stray `torch.no_grad()`, a `.detach()` in the forward pass, a `requires_grad=False` left over from a freezing experiment, or a graph broken by converting to NumPy and back. Or the signal may be dying in the forward pass — the [Set 08](./08-solutions.md) default-initialization failure produced exactly 11.35% accuracy and a flat loss at depth 10, with no error message of any kind.

The diagnostic that separates them takes ten seconds: **print the gradient norm per layer after one backward pass.** All zeros means the graph is broken — nothing is connected, and no learning rate will help. Nonzero but shrinking sharply toward the input means vanishing signal, so fix initialization or add normalization. Nonzero and healthy throughout means the gradients are fine and the learning rate is the problem, at which point the LR range test from [Set 06](./06-solutions.md) tells you where to put it.

**2. Overfit one batch.**

Take a single batch of, say, 32 examples and train on it alone for a few hundred steps. A model with sufficient capacity and a working training loop must drive the loss to essentially zero, because it can simply memorize 32 input–output pairs. If it cannot, something is broken, and no amount of hyperparameter tuning will fix it.

This is diagnostic — not just encouraging — because it **removes generalization from the equation entirely**. On a full training run, poor loss could mean a bug, an architecture too small, a bad learning rate, insufficient data, a distribution mismatch, or a task genuinely harder than you thought. On one batch, all of those explanations except the first two are eliminated by construction. Failing the test is therefore near-proof of a bug or an optimization problem, which is a far sharper conclusion than "training isn't going well."

What it does **not** rule out is at least as important. It says nothing about generalization — a model that memorizes 32 examples may still be useless on new ones. It will not catch a train/eval discrepancy, since you never evaluated. It will not catch data leakage, an incorrect augmentation pipeline, or labels misaligned in a way that is consistent within the batch. Most sharply: **a model can pass this test with completely shuffled labels**, which is precisely the [Set 07](./07-solutions.md) result. So passing means "the loop works and the model has capacity," not "the setup is correct." The measured version from [Module 09](../../09-practical-training-and-debugging.md): initial loss 2.2942, then 0.00131 at step 50 and 0.00027 at step 200. Fifteen seconds, and it is the highest-value fifteen seconds in the whole process.

**3. Random versus grid search.**

The argument is about *effective dimensionality*. Suppose you have a budget of $n = 9$ trials over two hyperparameters. Grid search puts them on a $3\times3$ lattice, so each hyperparameter is tested at exactly **three** distinct values. Random search draws 9 independent points, so each hyperparameter is tested at **nine** distinct values.

That matters because hyperparameter importance is enormously unequal. In a typical setup the learning rate dominates and the second parameter barely matters. Grid search then spends its budget evaluating three learning rates three times each — the three replicates differ only in a parameter that has no effect, so six of the nine runs are wasted. Random search gets nine distinct learning rates for the same cost. Bergstra and Bengio's Figure 1 makes this visually in one glance, and the effect grows exponentially with dimension: at $k$ hyperparameters, a grid with $m$ values each costs $m^k$ trials while still testing only $m$ values of the one that matters.

Grid search is better when you genuinely have few parameters, all of them important, and you need reproducible coverage — an ablation table for a paper, for instance, where "we tried exactly these settings" is part of the claim. It is also the right choice for a discrete parameter with a handful of meaningful values, where randomizing buys nothing. For everything else, and certainly for the first pass on a new problem, random search over sensible ranges (log-uniform for learning rates and weight decay, uniform for dropout) is strictly better use of the same compute.

**4. Normalization statistics.**

Because computing statistics over data you will later evaluate on leaks information from that data into the model. The mean and standard deviation are summaries of the whole set, and every training example is transformed using a quantity that depends on the validation and test examples. That contaminates your estimate of generalization.

A concrete scenario. You are building a defect classifier for a production line and normalize using the mean and standard deviation of the full dataset. Unknown to you, the test split happens to contain images from a later shift where the lighting had been adjusted and everything is 15% brighter. Those images are pulled toward the global mean by a normalization that "knows" about them, and your validation and test scores look excellent. In production, the model normalizes each incoming image using the stored statistics — which no longer encode the current lighting, because a fresh distribution shift is not in the dataset at all — and accuracy drops sharply. You cannot reproduce your validation score and you have no idea why, because the leak was in a preprocessing line nobody looks at.

The rule generalizes past normalization: **anything fitted to data must be fitted to the training split only** — normalization statistics, vocabulary, PCA bases, feature scalers, imputation values, class weights. In scikit-learn terms, `fit` on train, `transform` everywhere, and never `fit_transform` on the whole thing.

**5. The four calls.**

`optimizer.zero_grad()` clears the `.grad` buffers. PyTorch *accumulates* gradients by design, because that is what recurrent networks and gradient accumulation across microbatches need. Omit it and each step uses the running sum of every gradient so far, which is an unbounded, badly-scaled update direction. Script 1 below shows the symptom, and it is nastier than divergence.

`model.train()` and `model.eval()` toggle the behaviour of layers that differ between phases — dropout on versus off, BatchNorm using batch versus running statistics. Omit `eval()` and your evaluation is noisy and pessimistic; omit `train()` after evaluating and you silently stop regularizing. Script 3 measures both.

`torch.no_grad()` disables graph construction. Omit it during evaluation and you build a computation graph you never use, wasting memory and time; on a large model this alone can cause an out-of-memory error during validation. It is not a correctness bug — the numbers are the same — which is exactly why it survives review.

`.item()` extracts a Python float from a one-element tensor, detached from the graph. Writing `total_loss += loss` instead keeps the *entire computation graph* of every batch alive, because the accumulated tensor holds references to all of them. Memory grows monotonically through the epoch and the job dies partway through, with a stack trace pointing at whatever allocation happened to be last — never at the logging line responsible.

**6. Keeping the test number honest.**

Three splits: train for fitting, validation for every decision, test used **once**. Architecture, learning rate, epoch count, early stopping, feature choices, and the decision to try a different model family all go against validation. When you are finished — genuinely finished — you evaluate on test once and report that number, whatever it is. If you then change something and re-evaluate, the number is no longer a clean estimate and you should say so.

The moment you find yourself checking test accuracy every epoch, you have converted your test set into a second validation set. Nothing dramatic happens on any single check; the damage is cumulative and statistical. Each look lets you make a decision — even an informal one like "that run looked bad, I'll kill it" — that is fitted to the test data, and after enough of them your test score is an optimistic estimate of performance on data you have effectively trained against. The published reason to care is that this is how a field ends up with benchmark numbers that do not reproduce; the personal reason is that you will deploy something worse than you think it is. If you need per-epoch curves, plot validation. Keep the test set sealed, and if you have burned it, the honest move is to hold out fresh data.

## Part B — Reference solutions

Every measurement below comes from `784 → 256 → ReLU → 10` on 10,000 MNIST examples, 3 epochs.

### Script 1 — missing `optimizer.zero_grad()`

| | first loss | step 10 | final loss | test accuracy |
| --- | --- | --- | --- | --- |
| broken | 2.2723 | 1.0353 | 1.1805 | **47.08%** |
| fixed | 2.2723 | 1.0928 | 0.2689 | **91.77%** |

**Symptom:** the loss drops normally for a while, then stalls at a plateau far above zero and jitters there. Accuracy lands around half of what it should be.

This is the most insidious bug in the set precisely because it *does not crash and does not obviously diverge*. If you saw only the broken run you would conclude the model needs more capacity, or a different learning rate, or more epochs. The loss went down, after all.

What is actually happening: without `zero_grad()`, `.grad` accumulates the sum of every gradient computed since the start. Early on the sum is dominated by recent, consistent gradients and the model improves. As the sum grows, the update direction becomes an average over an ever-longer history including gradients from a model state hundreds of steps stale, and its magnitude grows without bound. The optimizer ends up taking huge steps in a direction that describes nowhere in particular, and the loss settles into a noisy equilibrium.

The fastest confirmation is to print `p.grad.norm()` for one parameter across the first fifty steps. Healthy training gives a roughly stable norm; this bug gives one that climbs steadily and never comes back.

### Script 2 — softmax applied before `CrossEntropyLoss`

| | final loss | test accuracy |
| --- | --- | --- |
| broken | 1.5421 | 91.64% |
| fixed | 0.1204 | **94.51%** |

**Symptom:** it trains, it reaches respectable accuracy, and the loss refuses to go below roughly 1.46 no matter how long you run.

`nn.CrossEntropyLoss` expects **logits** and applies `log_softmax` internally, for the numerical-stability reasons worked through in [Set 04](./04-solutions.md). Feeding it probabilities applies softmax twice. The second softmax is applied to values already squashed into $[0,1]$, so the inputs to it span a range of at most 1 and the resulting distribution can never be sharper than $\text{softmax}([1,0,\dots,0])$.

That gives a computable floor. The best achievable per-example loss is

$$-\log\frac{e^{1}}{e^{1} + 9e^{0}} = -\log\frac{2.718}{11.718} = 1.4612$$

and the measured 1.5421 sits just above it. **A loss that converges to a specific non-zero constant is a strong hint that the loss function is being fed the wrong thing** — work out what the theoretical floor would be under your suspicion and see whether it matches.

Note how nearly this bug escapes: 91.64% versus 94.51% is exactly the kind of gap you would attribute to hyperparameters. Accuracy survives because the double softmax is monotonic and preserves the argmax; only the confidence, and therefore the loss and the gradients, is destroyed. In PyTorch the rule is simply that `CrossEntropyLoss` takes raw logits and `NLLLoss` takes `log_softmax` output — and there is never a reason to call `softmax` inside a training loop.

### Script 3 — never calling `model.eval()`

Three consecutive evaluations of the *same trained model* on the *same test set*:

```
86.56%   86.01%   86.75%          (train mode — dropout active)
92.47%                            (eval mode)
```

**Symptom:** evaluation results that differ between identical runs, and are consistently worse than they should be.

Dropout is still zeroing half the hidden units at test time, so each forward pass uses a different random sub-network. The result is 6 points of accuracy lost and — the giveaway — **non-determinism in a computation that should be exactly reproducible**. If running your evaluation twice gives two different numbers, you have either forgotten `eval()` or left randomness in the data pipeline, and those are the only two candidates worth checking.

BatchNorm produces a subtler version of the same bug, where evaluation uses batch statistics rather than the running averages. That one is not always visibly worse, and it makes results depend on evaluation batch size — a model that scores differently at batch size 1 than at batch size 256 has this bug.

The defensive habit is to write evaluation as a function that begins with `model.eval()` and is decorated with `@torch.no_grad()`, so the two calls that must always accompany each other cannot be separated:

```python
@torch.no_grad()
def evaluate(model, loader):
    model.eval()
    ...
```

Remember to call `model.train()` again before resuming — a run that mysteriously stops improving after the first validation pass is usually this.

### Script 4 — labels shuffled out of alignment with inputs

| | final training loss | test accuracy |
| --- | --- | --- |
| broken | 2.3014 | **12.44%** |
| fixed | 0.2018 | 93.34% |

**Symptom:** training loss stuck at $\ln 10 = 2.303$, accuracy at chance. Nothing learns at all.

`y[torch.randperm(len(y))]` permutes the labels without touching `X`, so image $i$ is paired with a random label. The comment calls it "shuffling the data," which is what makes it survive code review — shuffling *is* something you are supposed to do, just not to one tensor of a pair.

The distinguishing feature versus other flat-loss bugs is that here the **training** loss is also flat. If gradients were broken or the learning rate were microscopic, the training loss would be flat too — but on 10,000 examples this model has enough capacity to fit random labels eventually, so with more epochs the training loss *would* fall while test accuracy stayed at chance. That divergence is the signature, and it is the [Set 07](./07-solutions.md) random-label experiment reappearing as an accident rather than an experiment.

The prevention is not cleverness, it is looking: take ten examples from the loader you actually train on, render the images, print the labels, and check them by eye. Andrej Karpathy's recipe calls this "become one with the data," and it takes two minutes. Any transformation that touches inputs and labels must touch them together — pass indices through a `Dataset`, or index both tensors with the same permutation.

### Script 5 — learning rate far too high

| step | 0 | 5 | 10 | 20 |
| --- | --- | --- | --- | --- |
| $\eta = 10$ | 2.2723 | **2670.31** | **3181.68** | 375.59 |
| $\eta = 0.1$ | 2.2723 | 0.8382 | 0.7789 | 0.5105 |

**Symptom:** the loss explodes to enormous values within a handful of steps, then thrashes. With a slightly higher rate or a deeper model it reaches `inf` and then `nan`, after which every parameter is `nan` and the run is unrecoverable.

This is the one bug in the set that announces itself, which is why it is the least dangerous. The response is mechanical: divide the learning rate by ten and rerun. If it still explodes, divide again. If it explodes only after hundreds of healthy steps rather than immediately, suspect exploding gradients rather than the base rate, and add `clip_grad_norm_` — the discriminating experiment from [Set 06](./06-solutions.md).

Worth noting that the loss *comes back down* to 375 by step 20 rather than diverging monotonically. Large-learning-rate dynamics are chaotic, not simply divergent, and a run that appears to be recovering is not evidence that the rate is acceptable.

### The training loop you would actually use

```python
import torch, torch.nn as nn, time
from torch.utils.data import DataLoader, random_split
from torchvision import datasets, transforms

def get_device():
    if torch.cuda.is_available():          return torch.device("cuda")
    if torch.backends.mps.is_available():  return torch.device("mps")
    return torch.device("cpu")

device = get_device()
tf = transforms.Compose([transforms.ToTensor(), transforms.Normalize((0.1307,), (0.3081,))])
full = datasets.MNIST("./data", train=True, download=True, transform=tf)
train_ds, val_ds = random_split(full, [55000, 5000],
                                generator=torch.Generator().manual_seed(0))
test_ds = datasets.MNIST("./data", train=False, transform=tf)
train_loader = DataLoader(train_ds, batch_size=128, shuffle=True)
val_loader   = DataLoader(val_ds,   batch_size=1000)
test_loader  = DataLoader(test_ds,  batch_size=1000)

torch.manual_seed(0)
model = nn.Sequential(nn.Flatten(), nn.Linear(784, 256), nn.ReLU(),
                      nn.Dropout(0.2), nn.Linear(256, 10)).to(device)
criterion = nn.CrossEntropyLoss()
optimizer = torch.optim.AdamW(model.parameters(), lr=1e-3, weight_decay=1e-2)
scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(optimizer, T_max=15)

@torch.no_grad()
def evaluate(loader):
    model.eval()
    correct = total = 0; loss_sum = 0.0
    for x, y in loader:
        x, y = x.to(device), y.to(device)
        out = model(x)
        loss_sum += criterion(out, y).item() * y.numel()
        correct  += (out.argmax(1) == y).sum().item(); total += y.numel()
    return loss_sum / total, 100 * correct / total

best, best_state, patience = float("inf"), None, 0
for epoch in range(15):
    model.train()
    for x, y in train_loader:
        x, y = x.to(device), y.to(device)
        optimizer.zero_grad()
        loss = criterion(model(x), y)
        loss.backward()
        torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
        optimizer.step()
    scheduler.step()

    val_loss, val_acc = evaluate(val_loader)
    if val_loss < best:
        best, patience = val_loss, 0
        best_state = {k: v.detach().clone() for k, v in model.state_dict().items()}
    else:
        patience += 1
        if patience >= 5:
            print(f"early stop at epoch {epoch+1}"); break

model.load_state_dict(best_state)          # restore best, not last
test_loss, test_acc = evaluate(test_loader)
print(f"FINAL test_loss={test_loss:.4f} test_acc={test_acc:.2f}%")
```
```
epoch  1 val_loss=0.1746 val_acc=94.72%  lr=9.89e-04
epoch 15 val_loss=0.0619 val_acc=98.20%  lr=0.00e+00
FINAL test_loss=0.0581 test_acc=98.28%   (25s on mps)
```

Every element earns its place. The 55k/5k split gives a validation set for stopping so the test set stays sealed. AdamW rather than Adam applies weight decay properly ([Set 06](./06-solutions.md)). Cosine annealing takes large steps early and small ones at the end, which is the schedule that consistently outperforms step decay on a fixed budget. Gradient clipping costs nothing and prevents a single bad batch from destroying a run. Early stopping tracks validation *loss* and — critically — **restores the best state dict** rather than keeping the final weights.

Before running this you would have already done the two sanity checks: initial loss 2.2942 against the predicted $\ln 10 = 2.3026$, and a single batch driven to 0.00027 in 200 steps. Both take seconds and both were passed, which is why the 25-second real run was worth starting.

98.28% is roughly the ceiling for a fully-connected model on MNIST. Getting past it needs a different function class, which is [Module 10](../../10-convolutional-networks.md).

---

Back to [Set 09](../09-exercises.md) · Next solutions: [Set 10](./10-solutions.md)
