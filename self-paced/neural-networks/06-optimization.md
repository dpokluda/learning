# 06 — Optimization: from gradient descent to Adam

Module 05 gave you the gradient. This module is about what to do with it, and the honest summary is that the obvious answer — take a small step downhill and repeat — is a surprisingly poor algorithm that needed thirty years of patching to become reliable. Understanding *what each patch fixes* is the difference between choosing an optimizer and its hyperparameters deliberately, and copying `Adam(lr=1e-3)` from a tutorial and hoping.

The concrete stakes: on the running MNIST MLP, three epochs of plain SGD at learning rate 0.01 reaches 88.2% test accuracy, the same optimizer at 0.1 reaches 93.8%, and adding momentum at that same learning rate reaches 97.3%.[^m6-sweep] Identical model, identical data, identical gradients. Nine points of accuracy, entirely from the update rule.

> **Prerequisite:** [Module 05](./05-backpropagation-and-autodiff.md) — you should know what `.grad` contains after `loss.backward()` and why gradients accumulate.

## Gradient descent and the learning rate

The core rule is one line. Compute the gradient of the objective and step against it:

$$\theta_{t+1} = \theta_t - \eta\,\nabla_\theta J(\theta_t)$$

The gradient points uphill, the minus sign turns you around, and $\eta$ — the **learning rate** — sets the stride. That is the whole algorithm, and everything in this module is a modification of it.

The learning rate is the most consequential hyperparameter in deep learning, and its behavior is asymmetric in an important way. Too small, and training is merely slow: the loss descends, just tediously, and you may stop before reaching anywhere good. Too large, and training does not slow down, it *breaks* — you overshoot the minimum, land somewhere with a larger gradient, overshoot further, and diverge to `nan` within a handful of steps. In the measured sweep above, Adam at $\eta=0.1$ scored 87.5%, seven points *worse* than Adam at $\eta=0.001$, not because it learned slowly but because it was thrashing.

The clean way to see this is a one-dimensional quadratic, $J(\theta) = \frac{1}{2}a\theta^2$ with gradient $a\theta$. The update becomes $\theta_{t+1} = \theta_t - \eta a\theta_t = (1-\eta a)\theta_t$, so after $t$ steps $\theta_t = (1-\eta a)^t\theta_0$. Convergence requires $|1-\eta a| < 1$, that is $0 < \eta < 2/a$. Below $\eta = 1/a$ you approach smoothly; at exactly $1/a$ you land on the minimum in a single step; between $1/a$ and $2/a$ you oscillate around it while still converging; above $2/a$ you diverge, and the divergence is exponential. Two facts fall out of this that hold far beyond the quadratic case: there is a hard stability threshold rather than a gentle degradation, and that threshold depends on the *curvature* $a$ — which in a real network differs by orders of magnitude between directions, and changes as training proceeds. That is why a single fixed learning rate is fundamentally a compromise, and why schedules exist.

## Batch, stochastic, and minibatch

The objective is an average over $N$ training examples, so the exact gradient requires touching all of them. That is **batch gradient descent**: exact, smooth, and for $N = 60{,}000$ it means one parameter update per full pass over the dataset. You would perform perhaps a hundred updates in an entire training run, which is nowhere near enough.

The opposite extreme, **stochastic gradient descent** in the original sense, uses a single example per update. Each gradient is a wildly noisy estimate of the true one, but it is an *unbiased* estimate — its expectation over the random choice of example is the true gradient — and you get 60,000 updates per epoch instead of one. Noisy but plentiful beats exact but rare, decisively.

Practice sits in between, at **minibatch** sizes of 32 to 512. The gradient noise falls as $1/\sqrt{B}$, so going from batch 1 to batch 100 cuts the noise tenfold at a hundredfold cost in computation — sharply diminishing returns — while the wall-clock cost stays nearly flat until you saturate the hardware, because GPUs compute a batch of 128 in about the same time as a batch of 1. Minibatching is therefore mostly a hardware-utilization decision with a statistically acceptable side effect. Everyone says "SGD" and means minibatch.

The noise turns out to be a feature and not merely a tolerated cost. Stochastic gradients let the trajectory escape shallow basins that would trap exact descent, and there is a substantial line of evidence that small-batch noise biases training toward flatter minima that generalize better, with large-batch training showing a measurable generalization gap.[^m6-keskar] That last claim comes with real caveats — much of the gap can be closed by scaling the learning rate and adding warmup, as Goyal et al. showed by training ImageNet at batch size 8192 — so treat "small batches generalize better" as a robust empirical tendency with known workarounds rather than a law.[^m6-goyal] The practical rule they established is the **linear scaling rule**: when you multiply batch size by $k$, multiply the learning rate by $k$ too, since each step now averages $k$ times more information and can afford to be $k$ times bolder.

## Why plain descent struggles: ravines and saddles

Steepest descent has a specific and fixable weakness, and seeing it clearly motivates everything that follows. Consider a loss surface shaped like a long narrow valley — steep walls in one direction, a gentle slope along the floor. This is called an **ill-conditioned** problem, and it is the normal case for neural networks, where the ratio of largest to smallest curvature can exceed $10^4$.

The gradient at a point on the valley wall points mostly *across* the valley, not along it. So gradient descent bounces from wall to wall, making rapid progress in the direction you do not care about and crawling along the direction you do. Worse, the learning rate is capped by the steep direction — exceed $2/a_{\max}$ and you diverge — so you are forced to take tiny steps along the shallow direction where you would like large ones. One knob, two conflicting requirements.

The second structural feature of high-dimensional loss surfaces is more encouraging than folklore suggests. The common worry is local minima: gradient descent finding a mediocre basin and getting stuck. In high dimensions this is largely the wrong thing to fear. A critical point is a local minimum only if the curvature is positive in *every one* of a million directions; if even one direction curves downward, it is a **saddle point** and there is an escape route. Dauphin and colleagues argued on both theoretical and empirical grounds that saddle points, not local minima, dominate the critical points of high-dimensional non-convex losses, and that most local minima found in practice have loss values close to the global minimum.[^m6-dauphin] The practical consequence is liberating: you are not searching for the one good basin among many bad ones. You are trying to move efficiently through a landscape of plateaus and saddles, most of whose destinations are about equally good. Optimization in deep learning is a problem of *speed and stability*, not of getting stuck.

## Momentum

The fix for the ravine is to give the parameters inertia. Instead of stepping along the current gradient, accumulate an exponentially-weighted running average of past gradients and step along that:

$$\mathbf{v}_{t} = \mu\,\mathbf{v}_{t-1} + \nabla_\theta J(\theta_t), \qquad \theta_{t+1} = \theta_t - \eta\,\mathbf{v}_t$$

with $\mu$, the momentum coefficient, typically 0.9. The physical reading is a ball rolling downhill with mass: it does not instantly change direction when the local slope does.

Why this fixes the ravine is worth working out rather than accepting. In the steep across-valley direction, the gradient flips sign on every bounce, so consecutive terms in the running sum cancel and the accumulated velocity stays small — the oscillation is *damped*. In the shallow along-valley direction, the gradient points the same way every step, so the terms reinforce and the velocity builds. With $\mu = 0.9$ the geometric series $1 + \mu + \mu^2 + \cdots$ sums to $1/(1-\mu) = 10$, so a consistent gradient produces an effective step ten times larger than a single gradient would. Momentum damps the direction you want damped and amplifies the direction you want amplified, using nothing but the sign consistency of the gradient to tell them apart. That is the whole trick, and it is why momentum is nearly free and nearly always worth using — in the measured sweep, it took SGD at $\eta = 0.1$ from 93.8% to 97.3% at essentially zero extra cost.

**Nesterov accelerated gradient** refines this by evaluating the gradient at the point momentum is about to carry you to, rather than where you currently are — a look-ahead correction that lets the update "brake" before overshooting.[^m6-nesterov] It has better theoretical convergence guarantees on convex problems and usually gives a small empirical improvement; in PyTorch it is `nesterov=True` on `torch.optim.SGD`.

A caution on conventions: PyTorch's `SGD` implements $v_{t} = \mu v_{t-1} + g_t$ and $\theta_{t+1} = \theta_t - \eta v_t$, which differs from the formulation in some textbooks that fold $\eta$ into the velocity, $v_t = \mu v_{t-1} - \eta g_t$. The trajectories differ when you change $\eta$ mid-training, which schedules do. The from-scratch implementation below reproduces PyTorch's convention exactly, to sixteen decimal places.[^m6-verified]

## Adaptive learning rates

Momentum addresses the direction of the step. The second family of fixes addresses its *size*, per parameter. The observation is that different parameters need different learning rates — a weight receiving rare, large gradients (from a rare input feature) and a weight receiving constant small ones should not be treated identically — and a single global $\eta$ cannot express that.

**AdaGrad** accumulates the sum of squared gradients per parameter and divides the step by its square root:

$$G_t = G_{t-1} + \mathbf{g}_t^2, \qquad \theta_{t+1} = \theta_t - \frac{\eta}{\sqrt{G_t} + \epsilon}\odot\mathbf{g}_t$$

Parameters with a history of large gradients get small steps; rarely-updated parameters get large ones. This works well for sparse features, and it has a fatal flaw for deep learning: $G_t$ only ever grows, so the effective learning rate decays monotonically toward zero and training stalls before convergence.[^m6-adagrad]

**RMSProp** fixes exactly that by replacing the running *sum* with a running *average*, which forgets:

$$\mathbb{E}[g^2]_t = \rho\,\mathbb{E}[g^2]_{t-1} + (1-\rho)\,\mathbf{g}_t^2, \qquad \theta_{t+1} = \theta_t - \frac{\eta}{\sqrt{\mathbb{E}[g^2]_t}+\epsilon}\odot\mathbf{g}_t$$

with $\rho \approx 0.9$. Now the denominator tracks the *recent* gradient magnitude rather than all of history, so the effective learning rate can rise again when the landscape changes. Dividing by the root-mean-square gradient normalizes each parameter's step to roughly unit scale regardless of its gradient's magnitude — which is precisely the per-direction rescaling the ravine problem asked for. RMSProp was proposed by Geoffrey Hinton in a Coursera lecture and never formally published, which is a nice illustration of how the field actually propagates ideas.[^m6-rmsprop]

## Adam

**Adam** is momentum and RMSProp combined, plus one correction, and it is the default optimizer of modern deep learning.[^m6-adam] Maintain two exponential moving averages — the first moment (mean) of the gradient, which is momentum, and the second moment (uncentered variance), which is RMSProp:

$$\mathbf{m}_t = \beta_1\mathbf{m}_{t-1} + (1-\beta_1)\mathbf{g}_t, \qquad \mathbf{v}_t = \beta_2\mathbf{v}_{t-1} + (1-\beta_2)\mathbf{g}_t^2$$

Both are initialized to zero, and that creates a real problem at the start of training. With $\beta_2 = 0.999$, after one step $\mathbf{v}_1 = 0.001\,\mathbf{g}_1^2$ — a thousand times smaller than the true second moment, because it is an average of one real value and a great many implicit zeros. Dividing by its too-small square root would produce an enormous first step. The fix is **bias correction**, and it is exact rather than a heuristic: since $\mathbb{E}[\mathbf{v}_t] \approx (1-\beta_2^t)\,\mathbb{E}[\mathbf{g}^2]$, dividing by $(1-\beta_2^t)$ removes the bias precisely.

$$\hat{\mathbf{m}}_t = \frac{\mathbf{m}_t}{1-\beta_1^t}, \qquad \hat{\mathbf{v}}_t = \frac{\mathbf{v}_t}{1-\beta_2^t}, \qquad \theta_{t+1} = \theta_t - \frac{\eta}{\sqrt{\hat{\mathbf{v}}_t}+\epsilon}\,\hat{\mathbf{m}}_t$$

Note that the correction factors approach 1 as $t$ grows, so this matters only for the first few hundred steps — but those are the steps where a bad update can wreck an initialization. The defaults $\beta_1 = 0.9$, $\beta_2 = 0.999$, $\epsilon = 10^{-8}$ are the paper's and are almost never worth changing; $\eta = 10^{-3}$ is the standard starting point and is what "Adam just works" refers to.

The complete algorithm is short enough to write out, and writing it out once is the best way to be sure you understand it. This implementation matches `torch.optim.Adam` to $1.1\times10^{-16}$ over twenty steps:[^m6-verified]

```python
class Adam:
    def __init__(self, params, lr=1e-3, betas=(0.9, 0.999), eps=1e-8):
        self.params = list(params)
        self.lr, (self.b1, self.b2), self.eps = lr, betas, eps
        self.m = [torch.zeros_like(p) for p in self.params]
        self.v = [torch.zeros_like(p) for p in self.params]
        self.t = 0

    @torch.no_grad()
    def step(self):
        self.t += 1
        for i, p in enumerate(self.params):
            g = p.grad
            self.m[i] = self.b1 * self.m[i] + (1 - self.b1) * g        # 1st moment
            self.v[i] = self.b2 * self.v[i] + (1 - self.b2) * g * g    # 2nd moment
            m_hat = self.m[i] / (1 - self.b1 ** self.t)                # bias correction
            v_hat = self.v[i] / (1 - self.b2 ** self.t)
            p -= self.lr * m_hat / (v_hat.sqrt() + self.eps)

    def zero_grad(self):
        for p in self.params:
            p.grad = None
```

Two costs are worth knowing. Adam stores two extra tensors per parameter, so its optimizer state is **twice the model size** — for a 7-billion-parameter model in fp32 that is 56 GB of optimizer state on top of 28 GB of weights, which is why memory planning for large-model training is dominated by the optimizer. And there is a persistent, well-documented finding that well-tuned SGD with momentum generalizes slightly *better* than Adam on image classification, which is why ResNet-style vision training still often uses SGD while Transformers essentially always use Adam.[^m6-wilson] The field does not have a fully satisfying explanation for this split; treat it as an empirical fact to respect rather than a settled theory.

**AdamW** is the variant you should actually reach for when using weight decay. Loshchilov and Hutter showed that adding an L2 penalty to the loss — the textbook way to implement weight decay — interacts badly with Adam's per-parameter normalization, because the penalty's gradient gets divided by $\sqrt{\hat v}$ along with everything else, so parameters with large gradients are effectively decayed less.[^m6-adamw] AdamW decouples them, applying the decay directly to the parameters:

$$\theta_{t+1} = \theta_t - \eta\left(\frac{\hat{\mathbf{m}}_t}{\sqrt{\hat{\mathbf{v}}_t}+\epsilon} + \lambda\theta_t\right)$$

This is not a subtlety — it materially improves results, and `torch.optim.AdamW` is the standard optimizer for training Transformers. Module 07 returns to what weight decay is doing.

## Learning rate schedules

A fixed learning rate is a compromise between the large steps you want early and the small ones you want late, so vary it. Warmup then decay is the standard shape, and each half has a distinct justification.

**Warmup** ramps the learning rate up from near zero over the first few hundred to few thousand steps. Early in training the parameters are random, the gradients are large and poorly correlated, and Adam's second-moment estimate is based on almost no data — so a full-size step is both unnecessary and dangerous. Warmup is essentially mandatory for Transformers, where its absence commonly produces divergence in the first few hundred steps, and it is what makes very large batch sizes work.[^m6-goyal]

**Decay** shrinks the rate as training proceeds, because near a minimum a large step overshoots and the noise floor of stochastic gradients sets a limit on how precisely you can converge. **Step decay** multiplies by 0.1 at fixed epochs and is the classic ImageNet recipe; you can usually see its effect as a sharp drop in the loss curve at each step. **Cosine annealing** decays smoothly following a half-cosine from $\eta_{\max}$ to near zero, has no step boundaries to tune, and has become the modern default for both vision and language models.[^m6-cosine] **One-cycle** ramps up and back down within a single training run and can produce very fast convergence.[^m6-onecycle]

```python
optimizer = torch.optim.AdamW(model.parameters(), lr=3e-4, weight_decay=0.01)
scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(optimizer, T_max=num_epochs)

for epoch in range(num_epochs):
    for batch in train_loader:
        ...
        optimizer.step()
    scheduler.step()          # per epoch for this scheduler; some step per batch
```

The one API detail that trips people: whether `scheduler.step()` belongs inside or outside the batch loop depends on the scheduler, and calling it in the wrong place silently gives you a schedule that completes hundreds of times too fast. `CosineAnnealingLR` with `T_max=num_epochs` steps per epoch; `OneCycleLR` steps per batch. Print `optimizer.param_groups[0]['lr']` occasionally and confirm it is doing what you think.

## Gradient clipping

One more safety mechanism, indispensable in Modules 11 and 12. Occasionally a batch produces an enormous gradient — a steep cliff in the loss surface — and a single normal-sized step along it throws the parameters somewhere useless, destroying hours of training. **Gradient clipping** rescales the whole gradient vector whenever its norm exceeds a threshold:

$$\text{if } \|\mathbf{g}\| > c: \quad \mathbf{g} \leftarrow c\,\frac{\mathbf{g}}{\|\mathbf{g}\|}$$

Rescaling by norm rather than clipping each component individually preserves the *direction* of the gradient and changes only its magnitude, which is the property you want. One line, immediately before `optimizer.step()`:

```python
loss.backward()
torch.nn.utils.clip_grad_norm_(model.parameters(), max_norm=1.0)
optimizer.step()
```

Recurrent networks essentially require it, Transformers conventionally use it with a threshold of 1.0, and feedforward networks usually do not need it. It is cheap insurance.

## What to actually do

The measured results on the running MNIST MLP, three epochs each, make the practical hierarchy concrete:

| Optimizer | Learning rate | Test accuracy | Test loss |
|---|---|---|---|
| SGD | 0.01 | 88.24% | 0.462 |
| SGD | 0.1 | 93.76% | 0.217 |
| SGD + momentum 0.9 | 0.1 | **97.26%** | **0.089** |
| Adam | 0.001 | 96.29% | 0.126 |
| Adam | 0.1 | 87.45% | 0.569 |

Momentum is the single largest improvement available, and a learning rate two orders of magnitude off is worse than a poor optimizer.[^m6-sweep] Note also that Adam at its default rate is *not* the winner here — on a small, easy problem, tuned SGD with momentum wins, which is a useful corrective to the belief that Adam is uniformly better.

The default recipe: start with AdamW at $\eta = 3\times10^{-4}$ and the standard betas. If the loss diverges or produces `nan`, divide the learning rate by ten, and add warmup and gradient clipping if you are training anything recurrent or attention-based. If you are training a convolutional vision model and want the last fraction of a percent, tune SGD with momentum 0.9 and a cosine schedule instead. Add a schedule before you tune anything else, because the gain-per-unit-effort is high. And find the learning rate first, always: it dominates every other hyperparameter, and Module 09 gives you a systematic way to find it in a couple of minutes.

## Before you move on

Gradient descent has a hard stability threshold set by the curvature, not a gentle degradation, which is why a learning rate an order of magnitude too high does not train slowly but breaks. Minibatching is a hardware decision whose noise turns out to help. The characteristic difficulty of neural loss surfaces is ill-conditioning — ravines that force a single learning rate to satisfy two conflicting demands — and saddles rather than bad local minima. Momentum fixes the ravine by damping oscillating directions and amplifying consistent ones. RMSProp fixes it from the other side by normalizing each parameter's step by its recent gradient magnitude. Adam is both, with an exact bias correction for the zero-initialized moment estimates, and AdamW is what you use when weight decay is involved.

If you can explain why momentum's geometric series gives a $1/(1-\mu)$ effective speedup along a consistent direction, why Adam's bias correction is necessary and why it stops mattering after a few hundred steps, and why AdamW is not merely a cosmetic rearrangement of Adam plus L2, then you understand the field's optimization toolkit. Writing the twelve-line Adam yourself, as [Exercise Set 06](./exercises/06-exercises.md) asks, is the fastest way to be certain; the same set has you run a learning-rate range test and find the minimum empirically.

Next, [Module 07](./07-generalization-and-regularization.md) confronts the fact you have been quietly ignoring: minimizing the training loss is not the goal, and doing it too well is actively harmful.

## Sources

[^m6-sweep]: Measured while writing this module: `784→128→ReLU→10` MLP, MNIST, batch size 128, three epochs, identical seed and initialization across runs. Full script in the [Module 06 solutions](./exercises/solutions/06-solutions.md). Three epochs is short, so these numbers understate what every configuration eventually reaches; the *ordering* is the point.

[^m6-verified]: The from-scratch `Adam` above and an equivalent SGD-with-momentum were run for twenty steps against `torch.optim.Adam` and `torch.optim.SGD(momentum=0.9)` in float64; maximum absolute parameter difference was $1.1\times10^{-16}$ and $1.5\times10^{-16}$ respectively.

[^m6-keskar]: Nitish Shirish Keskar et al., ["On Large-Batch Training for Deep Learning: Generalization Gap and Sharp Minima"](https://arxiv.org/abs/1609.04836), ICLR 2017.

[^m6-goyal]: Priya Goyal et al., ["Accurate, Large Minibatch SGD: Training ImageNet in 1 Hour"](https://arxiv.org/abs/1706.02677), 2017. Source of the linear scaling rule and of gradual warmup; also the main counterweight to a strong reading of Keskar et al.

[^m6-dauphin]: Yann Dauphin et al., ["Identifying and attacking the saddle point problem in high-dimensional non-convex optimization"](https://arxiv.org/abs/1406.2572), NeurIPS 2014. The saddle-point claim is well-supported but remains an active research area rather than a closed question.

[^m6-nesterov]: Ilya Sutskever et al., ["On the importance of initialization and momentum in deep learning"](https://proceedings.mlr.press/v28/sutskever13.html), ICML 2013, is the paper that connected Nesterov's classical method to deep network training.

[^m6-adagrad]: John Duchi, Elad Hazan and Yoram Singer, ["Adaptive Subgradient Methods for Online Learning and Stochastic Optimization"](https://jmlr.org/papers/v12/duchi11a.html), JMLR 12, 2011.

[^m6-rmsprop]: Geoffrey Hinton, Nitish Srivastava and Kevin Swersky, [Neural Networks for Machine Learning, Lecture 6e](https://www.cs.toronto.edu/~tijmen/csc321/slides/lecture_slides_lec6.pdf). RMSProp has no journal publication; this lecture slide is the canonical citation.

[^m6-adam]: Diederik Kingma and Jimmy Ba, ["Adam: A Method for Stochastic Optimization"](https://arxiv.org/abs/1412.6980), ICLR 2015. Algorithm 1 is exactly the code in this module. Note that the paper's original convergence proof was later shown to be flawed by Reddi et al., ["On the Convergence of Adam and Beyond"](https://arxiv.org/abs/1904.09237), ICLR 2018 — the algorithm works extremely well in practice regardless, which is itself worth knowing about this field.

[^m6-adamw]: Ilya Loshchilov and Frank Hutter, ["Decoupled Weight Decay Regularization"](https://arxiv.org/abs/1711.05101), ICLR 2019.

[^m6-wilson]: Ashia Wilson et al., ["The Marginal Value of Adaptive Gradient Methods in Machine Learning"](https://arxiv.org/abs/1705.08292), NeurIPS 2017. The SGD-generalizes-better-than-Adam finding; contested in its strongest form, robust in its weak form.

[^m6-cosine]: Ilya Loshchilov and Frank Hutter, ["SGDR: Stochastic Gradient Descent with Warm Restarts"](https://arxiv.org/abs/1608.03983), ICLR 2017.

[^m6-onecycle]: Leslie Smith, ["Cyclical Learning Rates for Training Neural Networks"](https://arxiv.org/abs/1506.01186), WACV 2017, and ["Super-Convergence"](https://arxiv.org/abs/1708.07120), 2018.

**Further reading.** *Deep Learning* [Chapter 8](https://www.deeplearningbook.org/contents/optimization.html) is the most complete treatment of optimization for deep models available in one place, including ill-conditioning, saddle points, and the full family of adaptive methods. *Dive into Deep Learning* [Chapter 12](https://d2l.ai/chapter_optimization/index.html) covers the same algorithms with runnable implementations and good convergence visualizations. Sebastian Ruder's ["An overview of gradient descent optimization algorithms"](https://www.ruder.io/optimizing-gradient-descent/) is a practitioner survey (secondary source, but well-corroborated and unusually clear) that puts every optimizer in this module side by side. The [`torch.optim` documentation](https://pytorch.org/docs/stable/optim.html) gives exact update equations for every implemented optimizer and is the right place to check convention questions like the momentum formulation.
