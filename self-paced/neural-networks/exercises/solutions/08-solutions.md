# Solutions — Set 08

Worked answers for [Exercise Set 08](../08-exercises.md).

## Part A — Answers

**1. Deriving He initialization.**

Take a layer $\mathbf{z} = W\mathbf{a}$ with $n_{\text{in}}$ inputs, weights drawn i.i.d. with mean zero and variance $\sigma_w^2$, and inputs independent of the weights. Each output is a sum of $n_{\text{in}}$ independent zero-mean products, so variances add:

$$\mathrm{Var}(z_i) = n_{\text{in}}\,\sigma_w^2\,\mathbb{E}[a^2]$$

Now the ReLU. If $z$ from the previous layer is symmetric about zero, then $a = \max(0, z)$ zeroes exactly half the distribution and leaves the other half untouched, so

$$\mathbb{E}[a^2] = \tfrac{1}{2}\,\mathbb{E}[z^2] = \tfrac{1}{2}\mathrm{Var}(z)$$

Substituting, $\mathrm{Var}(z_{\ell+1}) = \frac{1}{2} n_{\text{in}} \sigma_w^2 \mathrm{Var}(z_\ell)$. Preserving variance means the factor equals 1:

$$\sigma_w^2 = \frac{2}{n_{\text{in}}}$$

**The factor of 2 is the ReLU throwing away half the signal's second moment**, and the initialization compensates by making the weights $\sqrt{2}$ times larger. Xavier was derived for activations that are approximately linear and symmetric near the origin — $\tanh$, in the regime where $\tanh(x)\approx x$ — where $\mathbb{E}[a^2] \approx \mathrm{Var}(z)$ with no halving, so no factor of 2 appears. Using Xavier with ReLU means every layer multiplies the variance by $1/2$, which at depth 50 is a factor of $2^{-50} \approx 10^{-15}$. The measurements below show exactly that.

**2. Xavier's compromise.**

The forward requirement is that activation variance be preserved, giving $\sigma_w^2 = 1/n_{\text{in}}$. But there is a second, symmetric requirement on the *backward* pass: gradients propagate through $W^\top$, and each gradient entry is a sum over the $n_{\text{out}}$ outputs the unit feeds. Preserving gradient variance therefore demands $\sigma_w^2 = 1/n_{\text{out}}$.

For a non-square layer these are contradictory — you cannot satisfy both with one number. Glorot and Bengio take the harmonic-mean-flavoured compromise

$$\sigma_w^2 = \frac{2}{n_{\text{in}} + n_{\text{out}}}$$

which reduces to $1/n$ when the layer is square and sits between the two requirements otherwise. It is a compromise rather than a solution because in general nothing is exactly preserved; it merely bounds the damage in both directions. In practice this is fine, partly because most modern architectures use roughly square hidden layers and partly because normalization layers now correct residual drift anyway. It is worth knowing that PyTorch's `kaiming_normal_` takes a `mode` argument for exactly this reason: `fan_in` preserves the forward pass, `fan_out` the backward.

**3. Symmetry breaking.**

Suppose every weight in a layer is the same constant $c$. Then every unit in that layer computes an identical function of the input, so all their activations are equal. On the backward pass, the gradient with respect to unit $j$'s incoming weights is $\delta_j \mathbf{a}^{(\ell-1)}$, and because the units are indistinguishable — same inputs, same outputs, same downstream weights — every $\delta_j$ is also identical. So every unit receives *the same gradient*, and after the update every weight is still the same constant.

The symmetry is a fixed point of gradient descent, and no amount of training escapes it. A layer of $n$ units permanently computes what one unit could compute. With $c = 0$ it is worse still, since the forward activations are also zero and the layer transmits nothing.

This is why weights are initialized *randomly* — the randomness is not about finding a good starting point, it is about making the units distinguishable so they can specialize. Biases, by contrast, can safely be initialized to zero, because the weights already break the symmetry.

**4. BatchNorm, training versus inference.**

At training time, for each feature $j$, using the statistics of the current minibatch:

$$\hat{x}_j = \frac{x_j - \mu_{\mathcal{B},j}}{\sqrt{\sigma^2_{\mathcal{B},j} + \epsilon}}, \qquad y_j = \gamma_j \hat{x}_j + \beta_j$$

At inference time the same formula is used, but $\mu$ and $\sigma^2$ are *running averages* accumulated during training rather than batch statistics.

They differ because inference must be deterministic and must work on a single example. Using batch statistics at test time would make a prediction depend on which other examples happened to be in the batch — the same input would get different answers — and would be undefined for a batch of one.

Three practical consequences of the training-time behaviour. First, **BatchNorm makes examples in a batch interact**, which is a genuine violation of the independence you would otherwise assume, and it acts as a stochastic regularizer because each example is normalized by noisy, batch-dependent statistics. Second, **it degrades badly at small batch sizes** — with a batch of 2 the estimated mean and variance are almost noise, which is why detection and segmentation models, forced into small batches by memory, use GroupNorm instead. Third, **it creates a train/test discrepancy that shows up as bugs**: forgetting `model.eval()` leaves BatchNorm using batch statistics at evaluation, and fine-tuning with a badly mismatched data distribution corrupts the running averages. A fourth worth mentioning is that BatchNorm makes the preceding layer's bias redundant, since it is immediately subtracted away — which is why you see `bias=False` on convolutions followed by BatchNorm.

**5. LayerNorm's axis.**

BatchNorm normalizes each feature **across the batch**: for feature $j$, it computes the mean and variance over the $B$ examples. LayerNorm normalizes each example **across its features**: for example $i$, it computes the mean and variance over that example's own feature vector. BatchNorm's statistics depend on other examples; LayerNorm's depend only on the example itself.

That is the whole reason for the split. Transformers process sequences of varying length, are trained with batch sizes that vary by hardware, and — decisively — generate autoregressively one token at a time at inference. A normalization whose statistics depend on batch composition is unusable in that setting: there is no meaningful batch when you are decoding a single token, and padding tokens would poison batch statistics. LayerNorm is per-example, so it behaves identically at batch size 1 and batch size 1,024, needs no running averages, and has no train/test discrepancy at all.

Convolutional image models sit at the opposite extreme. Batches are large and uniform in shape, and the statistics of a channel across a batch of images are a genuinely meaningful quantity — "how bright is this edge detector's response, on average, over natural images." BatchNorm's per-channel statistics are estimated over $B \times H \times W$ values, which is a lot of samples and therefore a stable estimate, and the noise it injects acts as useful regularization. Both choices follow from what a batch means in each domain.

**6. Residual connections and degradation.**

Differentiate $\mathbf{y} = \mathbf{x} + F(\mathbf{x})$ with respect to the input:

$$\frac{\partial \mathbf{y}}{\partial \mathbf{x}} = I + \frac{\partial F}{\partial \mathbf{x}}$$

Backpropagating through $L$ such blocks multiplies $L$ of these together, and every factor contains the identity. The gradient reaching an early layer therefore contains a term that is the product of $L$ identities — that is, the raw downstream gradient, undiminished — plus a sum of terms involving the $\partial F/\partial \mathbf{x}$ Jacobians. **What this guarantees is that the gradient can never vanish purely from depth**: even if every $\partial F/\partial\mathbf{x}$ is tiny, the identity path delivers signal intact. In a plain network the corresponding product has no such floor, and if the typical Jacobian norm is below 1 the gradient decays geometrically in $L$.

The degradation problem is He et al.'s observation that a 56-layer plain CNN had *higher training error* than a 20-layer one. It is not overfitting, and the distinction is the entire argument of the ResNet paper: overfitting means low training error with high test error, whereas here the training error itself was worse. Nor is it a capacity problem, because the deeper network strictly contains the shallower one as a special case — set the extra layers to the identity and you recover it exactly. So the deeper model *can* represent everything the shallower one can, and optimization simply fails to find it. The residual formulation fixes this by making the identity the *default*: a block that learns $F = 0$ is an identity, and driving weights to zero is something gradient descent does very easily, whereas learning to reproduce the identity mapping from scratch through two nonlinear layers is not.

## Part B — Reference solutions

### Forward signal probe, 50 layers

```python
import torch, torch.nn as nn

def probe(scheme, act, depth=50, width=256, n=512):
    torch.manual_seed(0)
    h, stds = torch.randn(n, width), []
    for _ in range(depth):
        W = torch.empty(width, width)
        if   scheme == "naive_normal": W.normal_(0, 1.0)
        elif scheme == "small":        W.normal_(0, 0.01)
        elif scheme == "xavier":       nn.init.xavier_normal_(W)
        elif scheme == "he":           nn.init.kaiming_normal_(W, nonlinearity="relu")
        h = act(h @ W.T)
        stds.append(h.std().item())
    return stds
```

With ReLU:

| initialization | layer 1 | layer 5 | layer 10 | layer 25 | layer 50 |
| --- | --- | --- | --- | --- | --- |
| $\mathcal{N}(0,1)$ | 6.4e+00 | 1.6e+02 | 2.8e+03 | 5.3e+07 | **nan** |
| $\mathcal{N}(0,0.01^2)$ | 6.4e-02 | 1.6e-05 | 2.8e-09 | 5.4e-22 | **0.0** |
| Xavier | 5.7e-01 | 2.0e-01 | 3.4e-02 | 1.4e-04 | 1.7e-08 |
| He | 8.0e-01 | 7.9e-01 | 6.5e-01 | 7.4e-01 | **6.6e-01** |

This is the module's central claim reduced to a single table. Weights that are too large multiply the signal up until it overflows float32 — `nan` before layer 50. Weights that are too small multiply it down until it underflows to exactly zero. **Both extremes destroy the network completely**, and neither is subtle: this is not slow training, it is a network whose output carries no information about its input.

Xavier is the interesting row, because it looks reasonable and is not. It decays by a steady factor per layer, reaching $10^{-8}$ by layer 50. That factor is $1/\sqrt{2}$ — precisely the ReLU halving the second moment that Xavier's derivation did not account for, since $(1/\sqrt2)^{50} \approx 10^{-7.5}$. He, which includes the factor of 2, holds the standard deviation between 0.65 and 0.8 for all fifty layers. A one-line change, and the difference between a network that trains and one that cannot.

With $\tanh$ the ranking flips:

| initialization | layer 1 | layer 10 | layer 50 |
| --- | --- | --- | --- |
| Xavier | 6.0e-01 | 2.1e-01 | 9.7e-02 |
| He | 7.4e-01 | 5.9e-01 | 5.6e-01 |

He is still more stable here, but the decisive point is that neither explodes, because $\tanh$ is bounded — it saturates rather than diverging, which is why the sigmoid era got away with initializations that would destroy a ReLU network. The cost is the mirror image: saturated units have derivatives near zero, so a signal that is too large stalls learning instead of overflowing. The general rule is that **the right initialization depends on the activation function**, which is why `kaiming_normal_` has a `nonlinearity` argument and why using the default when your activation is not ReLU is a silent error.

### What PyTorch actually does by default

`nn.Linear` initializes its weight with `kaiming_uniform_(w, a=math.sqrt(5))` — that is, He initialization *for a leaky ReLU with negative slope $\sqrt5$*, which is not an activation anyone uses. Working the constants through: the gain is $\sqrt{2/(1+5)} = 1/\sqrt3$, the uniform bound is $\sqrt{1/n_{\text{in}}}$, and the resulting weight standard deviation is $\sqrt{1/(3n_{\text{in}})}$ — a factor of $\sqrt{1/6} \approx 0.408$ below He's $\sqrt{2/n_{\text{in}}}$.

Probing a 10-hidden-layer ReLU MLP of width 128:

```
torch default init, act std by layer: 1.97e-01 8.61e-02 4.70e-02 3.38e-02 3.54e-02
                                      3.28e-02 3.11e-02 3.38e-02 2.91e-02 3.07e-02
He init,            act std by layer: 1.16e+00 1.10e+00 1.17e+00 1.19e+00 1.07e+00
                                      1.11e+00 9.61e-01 9.59e-01 1.08e+00 9.99e-01
```

He holds at 1.0. The default decays for the first few layers and then appears to level off around 0.03 — which brings us to the plateau question below.

Training all three variants on 10,000 MNIST examples, 8 epochs, SGD with momentum 0.9:

| learning rate | PyTorch default init | He init | BatchNorm |
| --- | --- | --- | --- |
| 0.01 | 11.35% | **93.88%** | **94.99%** |
| 0.1 | 11.35% | 9.80% | **94.68%** |
| 0.5 | 11.35% | 9.80% | **94.48%** |

Three findings, and the third is the one people get wrong.

**The default initialization fails completely at depth 10.** 11.35% is chance — the model predicts one class for everything, at every learning rate tried. This is not a contrived setup; it is `nn.Sequential` with `nn.Linear` and `nn.ReLU`, written the obvious way. If you build a deep MLP in PyTorch and it refuses to learn, this is a live suspect, and the fix is one loop over `model.modules()` calling `kaiming_normal_`. Note the practical implication: PyTorch's defaults are tuned for the shallow-to-moderate networks that dominate its examples, and most real architectures either apply their own initialization explicitly or rely on normalization layers to paper over it.

**He initialization fixes the failure but not the fragility.** It trains at 0.01 and diverges at 0.1 and above. Correct initialization guarantees the signal is well-scaled *at step zero*; it says nothing about step 500, when the weights have moved and the scaling has drifted.

**BatchNorm's real gift is learning-rate robustness.** It wins every row, but the striking part is that its accuracy varies by half a point across a fifty-fold learning-rate range, while He init goes from working to dead across a ten-fold range. BatchNorm re-normalizes at every layer on every forward pass, so the scaling cannot drift no matter what the weights do — which decouples the loss landscape's curvature from the weight scale and lets you use learning rates that would otherwise diverge. That, rather than the "internal covariate shift" story in the original paper, is the effect you can actually measure. Santurkar et al. argue the ICS explanation is wrong, and the table above is consistent with their view: what BatchNorm demonstrably delivers here is a smoother, more forgiving optimization problem.

### Explaining the plateau

The default-init activation standard deviation stops at about 0.03 rather than continuing to decay. Since the derivation predicts a per-layer factor of $1/\sqrt6 = 0.408$ — which would reach $10^{-4}$ by layer 10 — something is propping it up.

It is the **biases**. `nn.Linear` initializes its bias uniformly on $\pm 1/\sqrt{n_{\text{in}}}$, giving a bias standard deviation of $1/\sqrt{3 n_{\text{in}}} \approx 0.051$ at width 128. Once the propagated signal falls below that, the measured activation standard deviation is dominated by bias noise, not by anything derived from the input. The two-line experiment is to zero the biases and re-probe:

```python
for mod in model:
    if isinstance(mod, nn.Linear):
        nn.init.zeros_(mod.bias)
```
```
bias as-is : 1.97e-01 8.61e-02 4.70e-02 3.38e-02 3.54e-02 3.28e-02 3.11e-02 3.38e-02 2.91e-02 3.07e-02
bias zeroed: 1.96e-01 8.32e-02 3.31e-02 1.33e-02 5.92e-03 2.32e-03 8.55e-04 3.68e-04 1.31e-04 4.97e-05
```

With the biases removed the decay continues cleanly to $5\times10^{-5}$, and the successive ratios — 0.424, 0.398, 0.402, 0.445, 0.392, … — hover around the predicted $1/\sqrt6 = 0.408$. Theory confirmed to two digits.

The direct measurement is more damning still. Perturb the input by 1% and measure the relative change in the output: **$2.4\times10^{-5}$**. The network's output is, for practical purposes, a constant that ignores its input entirely — which is exactly why it scores 11.35% no matter how you train it. This is worth internalizing as a debugging habit: a plateau in an activation-statistics plot does not mean the signal survived, and the way to tell the difference is to check sensitivity to the input rather than magnitude.

### Gradient flow through residual connections

A 30-layer stack, one backward pass, comparing the gradient norm at layer 1 with the gradient norm at layer 30:

| architecture | $\lVert g_{L1}\rVert / \lVert g_{L30}\rVert$ |
| --- | --- |
| plain | **236.3** |
| residual | **5.5** |

Read the ratio as a measure of how unevenly the gradient is distributed across depth. In the plain network the two ends differ by more than two orders of magnitude, so any single learning rate is badly wrong for at least one of them. With residual connections the spread is a factor of 5.5 — layers across the whole depth receive gradients of comparable size, and one learning rate suits them all. That is the identity term in $I + \partial F/\partial\mathbf{x}$ doing its job.

The corresponding training result from [Module 08](../../08-initialization-and-normalization.md), on a depth-10 versus depth-30 MLP with 15 epochs each, shows the same thing in the loss: final training loss goes from 0.1877 (plain) to 0.0760 (residual) to 0.0014 (residual with the final BatchNorm $\gamma$ zero-initialized) at depth 10, and from 2.0847 to 0.7711 to 0.0010 at depth 30. Note the plain network gets *worse* as it gets deeper — 0.19 to 2.08 — which is the degradation problem reproduced on a laptop.

One honest note carried over from the module: a naive `x + block(x)` residual MLP built without care will explode rather than help, because the variance of the sum grows with every block. The working demo needs BatchNorm as the last operation inside the branch, the ReLU applied after the addition rather than inside the branch, and zero-initialization of the final BatchNorm $\gamma$ so each block starts as an exact identity. That last trick — which is standard practice in modern ResNet implementations, sometimes called "zero-init residual" — is worth the two orders of magnitude it delivers in the table above.

---

Back to [Set 08](../08-exercises.md) · Next solutions: [Set 09](./09-solutions.md)
