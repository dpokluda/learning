# Solutions — Set 06

Worked answers for [Exercise Set 06](../06-exercises.md). The scripts here are the reference implementations cited by [Module 06](../../06-optimization.md).

## Part A — Answers

**1. Momentum without the analogy.**

The update is an exponentially weighted moving average of past gradients, used in place of the current gradient. Instead of $\theta \leftarrow \theta - \eta g_t$, you keep a running accumulator $v_t = \mu v_{t-1} + g_t$ and step along $v$.

What that buys you is *directional filtering*. Expand the recursion: $v_t = g_t + \mu g_{t-1} + \mu^2 g_{t-2} + \cdots$. Any component of the gradient that points the same way step after step gets added to itself repeatedly and grows toward a geometric sum. Any component that alternates in sign — the classic zigzag across a narrow valley — largely cancels against its own history. So the accumulator amplifies the consistent part of the signal and attenuates the inconsistent part.

Two consequences follow. It cancels minibatch noise, since sampling noise is by definition inconsistent across batches, giving a lower-variance descent direction at no extra gradient cost. And it fixes the ill-conditioning problem: on a long narrow valley the across-valley component oscillates and is damped, while the along-valley component is consistent and is amplified, so the effective step turns to follow the valley floor.

**2. Where the 10 comes from.**

If the gradient were exactly constant at $g$, the accumulator converges to a geometric series:

$$v_\infty = g(1 + \mu + \mu^2 + \cdots) = \frac{g}{1-\mu}$$

With $\mu = 0.9$ that is $10g$, so the asymptotic step is ten times what plain SGD would take with the same $\eta$. With $\mu = 0.99$ it would be $100\times$, which is why raising momentum usually requires lowering the learning rate.

The condition is in the derivation: the factor is achieved only in directions where the gradient is *consistent* — same sign, roughly constant magnitude — for at least the accumulator's effective horizon of about $1/(1-\mu) = 10$ steps. Directions that change sign get no speedup at all, which is precisely the intended asymmetry. A useful corollary: the speedup is not free early in training, since the accumulator starts at zero and needs roughly $1/(1-\mu)$ steps to reach the steady state, which is one reason very high momentum can make the first few dozen steps sluggish.

**3. Adam's two averages.**

$m_t$ is an exponentially weighted average of the gradient — an estimate of its **first moment**, its mean. It does the same job momentum does: filter noise, amplify consistent directions.

$v_t$ is an exponentially weighted average of the *squared* gradient, elementwise — an estimate of the **second moment**, and since the mean has been separately estimated, roughly the per-coordinate magnitude. It is used as a divisor.

The scale invariance comes from the shape of $m_t/\sqrt{v_t}$. Multiply every gradient by a constant $c$: $m_t$ scales by $c$, $v_t$ by $c^2$, $\sqrt{v_t}$ by $|c|$, and the ratio is unchanged. So Adam's step size is determined by the learning rate and the *consistency* of the gradient direction, not by its magnitude — the update is roughly bounded by $\eta$ in each coordinate.

What that buys you is a per-coordinate step size, chosen automatically. Parameters receiving persistently small gradients (a rare word's embedding, an early layer under attenuation) get divided by a small number and take proportionally larger steps; parameters with large gradients get damped. This is why Adam works acceptably out of the box on architectures where SGD would need careful per-layer tuning, and why it is the default for Transformers, where gradient scales differ enormously across layers. The cost is memory — two extra tensors per parameter, tripling optimizer state.

**4. Bias correction.**

$m_0$ and $v_0$ are initialized to zero, which is a *biased* starting point. After one step, $m_1 = (1-\beta_1)g_1 = 0.1 g_1$ — a tenth of the true gradient — and $v_1 = 0.001 g_1^2$. Both are pulled toward zero because the average is dominated by the fictitious zero history.

The correction divides by $1 - \beta^t$, which is exactly the total weight actually accumulated so far. At $t=1$ this is $1-\beta_1 = 0.1$ for the first moment, cancelling the factor precisely; the estimate becomes unbiased. As $t$ grows, $\beta^t \to 0$ and the divisor $\to 1$, so the correction fades out. With $\beta_2 = 0.999$, $\beta_2^t$ falls below 0.01 by about $t = 4600$, so the correction matters for the first few thousand steps and is irrelevant thereafter.

Without it, the two biases do not cancel — they compound in the wrong direction. Because $\beta_2 > \beta_1$, $v_t$ is *more* underestimated than $m_t$, so $\sqrt{v_t}$ in the denominator is too small, and the resulting steps are far too large in the first few dozen iterations. In practice you see instability or divergence right at the start of training. This is also part of why Transformer training uses learning-rate warmup even *with* Adam: the second-moment estimate is noisy as well as biased when it has seen only a handful of samples.

**5. AdamW's decoupling.**

Decoupled from the **adaptive scaling** — that is, from the division by $\sqrt{\hat v_t}$.

For SGD the two are identical. Adding $\frac{\lambda}{2}\lVert\theta\rVert^2$ to the loss adds $\lambda\theta$ to the gradient, so the update becomes $\theta \leftarrow \theta - \eta(g + \lambda\theta) = (1-\eta\lambda)\theta - \eta g$. That is exactly "shrink $\theta$ by a constant factor, then take a gradient step" — L2 penalty and weight decay coincide.

For Adam they diverge. The L2 term enters through $g$, so it is carried into $m_t$ and $v_t$ and then *divided by $\sqrt{\hat v_t}$ along with everything else*:

$$\theta \leftarrow \theta - \eta\,\frac{\widehat{m_t(g + \lambda\theta)}}{\sqrt{\hat v_t} + \epsilon}$$

The effective decay on each coordinate is now $\lambda/\sqrt{\hat v_i}$ — it depends on that coordinate's gradient history. Parameters with large gradients get *less* decay, which is backwards: those are typically the parameters most in need of constraint. Worse, the decay strength becomes coupled to the learning rate and to the loss scale, so tuning one changes the other.

AdamW applies the decay directly to the parameters, outside the adaptive machinery:

$$\theta \leftarrow \theta - \eta\,\frac{\hat m_t}{\sqrt{\hat v_t}+\epsilon} - \eta\lambda\theta$$

Now every parameter decays at the same rate, and $\lambda$ can be tuned independently of $\eta$. This is not cosmetic: Loshchilov and Hutter showed it materially improves generalization, and AdamW is the default in essentially all modern training. If you write `torch.optim.Adam(..., weight_decay=0.01)` you are getting the *coupled* version, which is almost never what you want.

**6. Loss goes `nan` after 200 steps.**

In order: (1) learning rate too high, with the loss having climbed a wall it could not come back from; (2) exploding gradients, especially in a recurrent or very deep model; (3) a $\log(0)$ or division by zero in the loss — a manual softmax, a `log` of a probability that reached zero, a division by a standard deviation that reached zero; (4) bad data — a `nan` or `inf` in an input or label, which will propagate to everything and is far more common than people expect; (5) numerical overflow in mixed precision, where float16 tops out around 65,504.

The single experiment distinguishing the top two: **add gradient clipping and rerun**. If clipping alone fixes it, the problem was exploding gradients and the architecture or initialization deserves attention. If it still diverges with clipping, the learning rate itself is too high and you should divide it by ten. As a cheap accompaniment, log the gradient norm every step — a smooth trend that spikes just before the `nan` implicates gradients, whereas a norm that was stable right up to the failure points at the loss computation or the data instead. And `torch.autograd.set_detect_anomaly(True)` will tell you exactly which operation first produced the `nan`, at the cost of a large slowdown.

## Part B — Reference solutions

### Implementing the optimizers

```python
import torch, torch.nn as nn
torch.set_default_dtype(torch.float64)          # so agreement is unambiguous

def make():
    torch.manual_seed(0)
    return nn.Sequential(nn.Linear(4, 8), nn.ReLU(), nn.Linear(8, 3))

X = torch.randn(16, 4); y = torch.randint(0, 3, (16,))
criterion = nn.CrossEntropyLoss()

# --- reference ---
m1 = make(); o1 = torch.optim.Adam(m1.parameters(), lr=1e-2, betas=(0.9, 0.999), eps=1e-8)
for _ in range(20):
    o1.zero_grad(); criterion(m1(X), y).backward(); o1.step()

# --- from scratch ---
m2 = make()
params = list(m2.parameters())
m_buf = [torch.zeros_like(p) for p in params]
v_buf = [torch.zeros_like(p) for p in params]
beta1, beta2, eps, lr = 0.9, 0.999, 1e-8, 1e-2

for t in range(1, 21):
    m2.zero_grad(); criterion(m2(X), y).backward()
    with torch.no_grad():
        for i, p in enumerate(params):
            g = p.grad
            m_buf[i] = beta1 * m_buf[i] + (1 - beta1) * g
            v_buf[i] = beta2 * v_buf[i] + (1 - beta2) * g * g
            m_hat = m_buf[i] / (1 - beta1**t)
            v_hat = v_buf[i] / (1 - beta2**t)
            p -= lr * m_hat / (v_hat.sqrt() + eps)

print("Adam max param diff:",
      max((a - b).abs().max().item() for a, b in zip(m1.parameters(), m2.parameters())))

# --- SGD with momentum: PyTorch uses v = mu*v + g ; p -= lr*v ---
m3 = make(); o3 = torch.optim.SGD(m3.parameters(), lr=0.1, momentum=0.9)
for _ in range(20):
    o3.zero_grad(); criterion(m3(X), y).backward(); o3.step()

m4 = make(); params = list(m4.parameters())
v = [torch.zeros_like(p) for p in params]
for _ in range(20):
    m4.zero_grad(); criterion(m4(X), y).backward()
    with torch.no_grad():
        for i, p in enumerate(params):
            v[i] = 0.9 * v[i] + p.grad
            p -= 0.1 * v[i]

print("SGD-mom max param diff:",
      max((a - b).abs().max().item() for a, b in zip(m3.parameters(), m4.parameters())))
```
```
Adam max param diff:    1.1102230246251565e-16
SGD-mom max param diff: 1.5265566588595902e-16
```

Machine epsilon. Adam is genuinely twelve lines, and once you have written those twelve lines it stops being a black box.

The momentum convention is the trap. PyTorch computes $v \leftarrow \mu v + g$ and then $\theta \leftarrow \theta - \eta v$, keeping $\eta$ *outside* the velocity. Several textbooks write $v \leftarrow \mu v - \eta g$, $\theta \leftarrow \theta + v$, folding $\eta$ *in*. At constant $\eta$ the two give identical trajectories, so you may never notice — until you add a learning-rate schedule, at which point PyTorch's version rescales the entire accumulated history the instant $\eta$ changes while the textbook version does not, and the trajectories diverge. If you implement momentum yourself and it drifts from `torch.optim` only after your scheduler fires, this is why.

### Optimizer sweep on MNIST

Three epochs, batch size 128, `784 → 128 → ReLU → 10`, identical seed and initialization for every row.

| optimizer | learning rate | test accuracy |
| --- | --- | --- |
| SGD | 0.01 | 88.24% |
| SGD | 0.1 | 93.76% |
| SGD + momentum 0.9 | 0.1 | **97.26%** |
| Adam | $10^{-3}$ | 96.29% |
| Adam | $10^{-1}$ | 87.45% |

Three things to take from this.

**The learning rate matters more than the optimizer.** Same SGD, ten-fold change in $\eta$, 5.5 points of accuracy — a larger swing than switching optimizer families at a sensible setting. If you tune exactly one hyperparameter, tune this one.

**Momentum is nearly free.** SGD with momentum at the same $\eta$ gains 3.5 points over plain SGD, costs one extra tensor per parameter, and requires no tuning beyond accepting $\mu = 0.9$. There is essentially never a reason to use plain SGD.

**Adam at the wrong learning rate is worse than SGD at the right one.** Adam at $10^{-1}$ scores 87.45%, below every SGD row. Adam's adaptivity normalizes *direction*, not step size — $\eta$ still sets the scale, and $10^{-1}$ is a hundred times Adam's usual regime. The widespread belief that "Adam doesn't need tuning" is false; what is true is that Adam's *default* of $10^{-3}$ is a good starting point across many problems, which is a much weaker claim.

Three epochs understates every configuration's eventual ceiling — the Module 09 recipe reaches 98.28% — but the ordering is the point and it is stable.

### Learning-rate range test

Increase the learning rate geometrically each batch across five orders of magnitude, recording the loss.

```python
import torch, torch.nn as nn
from torch.utils.data import DataLoader
from torchvision import datasets, transforms

tf = transforms.Compose([transforms.ToTensor(), transforms.Normalize((0.1307,), (0.3081,))])
loader = DataLoader(datasets.MNIST("./data", train=True, download=True, transform=tf),
                    batch_size=128, shuffle=True)

torch.manual_seed(0)
model = nn.Sequential(nn.Flatten(), nn.Linear(784, 128), nn.ReLU(), nn.Linear(128, 10))
criterion = nn.CrossEntropyLoss()
opt = torch.optim.SGD(model.parameters(), lr=1e-6)

lo, hi, n = 1e-6, 10.0, 300
mult = (hi / lo) ** (1 / n)
lr, history = lo, []
for i, (x, y) in enumerate(loader):
    if i >= n:
        break
    for g in opt.param_groups:
        g["lr"] = lr
    opt.zero_grad(); loss = criterion(model(x), y); loss.backward(); opt.step()
    history.append((lr, loss.item()))
    lr *= mult

best = min(history, key=lambda t: t[1])
print(f"min loss {best[1]:.4f} at lr={best[0]:.4g}")
```

| learning rate | loss |
| --- | --- |
| $1.0\times10^{-6}$ | 2.3114 |
| $2.5\times10^{-5}$ | 2.3251 |
| $6.3\times10^{-4}$ | 2.2753 |
| $3.2\times10^{-3}$ | 2.1721 |
| $1.6\times10^{-2}$ | 1.7803 |
| $7.9\times10^{-2}$ | **0.8607** |
| $3.98\times10^{-1}$ | 1.1048 |
| $2.0\times10^{0}$ | 2.2965 |

Minimum loss 0.5255 at $\eta \approx 0.23$.

The curve has the three characteristic regions. Below about $10^{-4}$ the loss is flat at $\ln 10 = 2.303$: steps are too small to accomplish anything. Between $10^{-3}$ and $10^{-1}$ it falls steeply — this is the usable band. Past a few tenths it turns and climbs back toward chance as the updates overshoot.

The standard recipe is *not* to use the minimum. Take roughly an order of magnitude below it — around 0.02–0.1 here — because the minimum is the edge of the stable region and the whole test is run on a fresh, un-warmed model at one batch per learning rate, so it is a noisy estimate. That recommendation brackets the 0.1 that the sweep independently found best, which is the cross-check worth noticing: two different procedures, one taking three epochs per configuration and one taking 300 batches total, agreeing on the answer.

One important detail if you compare against [Module 09](../../09-practical-training-and-debugging.md), which reports a minimum at $10^{-2}$: **that run used Adam, this one uses SGD.** Adam's normalization means its useful learning rates sit one to two orders of magnitude below SGD's, and the two results are consistent rather than contradictory. The range test must be rerun whenever you change optimizer — a learning rate is a property of the optimizer-model pair, never of the model alone.

---

Back to [Set 06](../06-exercises.md) · Next solutions: [Set 07](./07-solutions.md)
