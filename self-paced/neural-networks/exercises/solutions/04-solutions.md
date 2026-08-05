# Solutions — Set 04

Worked answers for [Exercise Set 04](../04-exercises.md).

## Part A — Answers

**1. MSE from maximum likelihood.**

Assume the target is the model's prediction plus independent Gaussian noise of constant variance: $y^{(i)} = f(\mathbf{x}^{(i)};\theta) + \epsilon^{(i)}$ with $\epsilon^{(i)} \sim \mathcal{N}(0, \sigma^2)$. Equivalently $p(y^{(i)}\mid \mathbf{x}^{(i)};\theta) = \mathcal{N}(y^{(i)}; f(\mathbf{x}^{(i)}), \sigma^2)$.

The log-likelihood of the dataset, using independence to turn the product into a sum:

$$\log \prod_i p(y^{(i)}\mid\mathbf{x}^{(i)}) = \sum_i \left[-\frac{(y^{(i)} - f(\mathbf{x}^{(i)}))^2}{2\sigma^2} - \log\sigma - \tfrac12\log 2\pi\right]$$

The last two terms do not depend on $\theta$, and $1/2\sigma^2$ is a positive constant. Maximizing the log-likelihood is therefore *exactly* minimizing $\sum_i (y^{(i)} - f(\mathbf{x}^{(i)}))^2$. MSE is not a convenient choice that happens to work; it is the negative log-likelihood of a Gaussian model, and the constant variance is why $\sigma$ vanishes.

If the variance is *not* constant — heteroscedastic noise, $\sigma_i^2$ per example — then $\sigma$ no longer factors out and you get $\sum_i (y^{(i)} - f_i)^2/2\sigma_i^2 + \sum_i \log\sigma_i$: a weighted least squares in which noisy examples count less. Plain MSE is then the wrong loss, and it will over-fit the noisy examples. The practical version is to have the network output *both* a mean and a variance and train on the full expression, which is how uncertainty-aware regression is done.

**2. Cross-entropy from maximum likelihood.**

Model the label as categorical: $p(y = k \mid \mathbf{x};\theta) = \hat{y}_k$ where $\hat{\mathbf{y}} = \text{softmax}(\mathbf{z})$. For one example with true class $c$, the likelihood is the single number $\hat{y}_c$. Writing the label one-hot lets you express it as a product, $p(y\mid\mathbf{x}) = \prod_{k=1}^{K}\hat{y}_k^{\,\mathbb{1}[k=c]}$, whose log is $\sum_k \mathbb{1}[k=c]\log\hat{y}_k$.

The $-\log$ comes from two separate places, which is worth separating. The $\log$ comes from converting the product over examples into a sum, so that gradients decompose and the numbers stay in range. The minus sign comes from the convention that optimizers *minimize*, so maximizing likelihood becomes minimizing negative log-likelihood.

The sum over classes collapses because $\mathbb{1}[k=c]$ is zero for every $k$ except $c$. So $\mathcal{L} = -\log\hat{y}_c$: the negative log probability the model assigned to the correct answer, and nothing else. Every other class enters only through the softmax normalizer. This is also why PyTorch takes an integer class index rather than a one-hot vector — the one-hot was only ever notation.

**3. Why the gradient is $\hat{\mathbf{y}} - \mathbf{y}$.**

With $\mathcal{L} = -\log\hat{y}_c$ and $\hat{y}_k = e^{z_k}/\sum_j e^{z_j}$, write $\mathcal{L} = -z_c + \log\sum_j e^{z_j}$ directly (this is the algebra, not an approximation). Differentiating with respect to $z_i$:

$$\frac{\partial\mathcal{L}}{\partial z_i} = -\mathbb{1}[i=c] + \frac{e^{z_i}}{\sum_j e^{z_j}} = \hat{y}_i - \mathbb{1}[i=c]$$

which is $\hat{\mathbf{y}} - \mathbf{y}_{\text{onehot}}$. The cancellation is visible in that second form: $\log$ and $\exp$ are inverses, so the log of the softmax is *linear* in $z_c$ plus a logsumexp term, and the derivative of a linear term is a constant. There is no factor of $\hat{y}(1-\hat{y})$ anywhere.

That absence is the whole point. Because the gradient contains **no activation derivative**, it cannot be shrunk by saturation. However confidently wrong the model is, the gradient magnitude is bounded below by the size of the error itself, so the correction is always proportional to how wrong you are. That is the property any well-designed loss should have and the one MSE-on-softmax lacks.

**4. MSE on softmax outputs.**

The gradient acquires the softmax derivative factor that cross-entropy cancels. For the binary case with $\hat{y} = \sigma(z)$ and target 1, $\mathcal{L} = (\sigma(z)-1)^2$ gives

$$\frac{\partial\mathcal{L}}{\partial z} = 2(\sigma(z)-1)\cdot\sigma(z)(1-\sigma(z))$$

As the model becomes *more* confidently wrong, $z \to -\infty$, the first factor approaches $-2$ but $\sigma(z)(1-\sigma(z)) \to 0$ exponentially. The product goes to zero. The gradient is smallest exactly where the model is most wrong.

Part B measures it: at $z=-10$ the model assigns probability $4.5\times10^{-5}$ to the correct class — as wrong as it is possible to be — and the MSE gradient is $9.1\times10^{-5}$ while cross-entropy's is $0.99996$. A factor of **11,000**. The model is stuck not because the loss is high but because the loss surface is flat there, and it will crawl out only over an enormous number of steps.

**5. Double softmax.**

`F.cross_entropy` internally applies `log_softmax`. Passing it probabilities means it computes `softmax(softmax(z))` — a softmax of numbers already squashed into $[0,1]$. Since those inputs span a range of at most 1, the second softmax's outputs are nearly uniform: for ten classes they land close to 0.1 regardless of what the model predicted.

You will probably not notice immediately, and that is the danger. The model still trains and accuracy still improves, because the *ordering* of the logits is preserved and `argmax` is unaffected. What you will see is that training is much slower than it should be, the loss sits stubbornly near $\ln 10 \approx 2.303$ and moves very little, and the reported probabilities are meaningless and radically underconfident. The tell is the loss scale: on a ten-class problem a model at 95% accuracy should have a loss well below 0.3, so a loss stuck near 2.2 alongside good accuracy is nearly diagnostic of this bug. It is on the checklist in [Module 09](../../09-practical-training-and-debugging.md) for exactly that reason.

**6. A task needing a different loss.**

Counting — say, predicting the number of events in a time window, where the target is a non-negative integer. MSE assumes symmetric Gaussian noise on an unbounded real line, which is wrong on both counts: counts cannot be negative, and their variance grows with their mean.

Assume a **Poisson** distribution, $p(y\mid\lambda) = \lambda^y e^{-\lambda}/y!$, with the network outputting $\lambda = \exp(z)$ so positivity is automatic. The negative log-likelihood is

$$\mathcal{L} = \lambda - y\log\lambda + \log(y!) = e^{z} - y\,z + \text{const}$$

where the constant does not depend on $\theta$ and is dropped. PyTorch provides this as `nn.PoissonNLLLoss(log_input=True)`.

Other defensible answers: a heavy-tailed target suggests a Laplace likelihood, whose NLL is mean *absolute* error; a bounded proportion suggests a Beta likelihood; multi-label classification (where classes are not mutually exclusive) needs $K$ independent Bernoullis, giving `BCEWithLogitsLoss` rather than softmax cross-entropy. The general recipe is the point: choose the distribution that matches the target's structure, then take the negative log-likelihood.

## Part B — Reference solution

### Three implementations, and breaking one

```python
import torch, torch.nn.functional as F

def ce_naive(logits, target):
    p = logits.exp() / logits.exp().sum(dim=1, keepdim=True)
    return -p[range(len(target)), target].log().mean()

def ce_shifted(logits, target):
    z = logits - logits.max(dim=1, keepdim=True).values
    p = z.exp() / z.exp().sum(dim=1, keepdim=True)
    return -p[range(len(target)), target].log().mean()

def ce_logsumexp(logits, target):
    return (-logits[range(len(target)), target] + torch.logsumexp(logits, dim=1)).mean()

small  = torch.tensor([[1.0, 2.0, 3.0]])
big    = torch.tensor([[800.0, 801.0, 802.0]])
target = torch.tensor([2])

for name, l in (("[1,2,3]", small), ("[800,801,802]", big)):
    print(f"{name:16s} naive={ce_naive(l, target).item():.6f} "
          f"shifted={ce_shifted(l, target).item():.6f} "
          f"logsumexp={ce_logsumexp(l, target).item():.6f} "
          f"torch={F.cross_entropy(l, target).item():.6f}")
```
```
[1,2,3]          naive=0.407606 shifted=0.407606 logsumexp=0.407606 torch=0.407606
[800,801,802]    naive=nan      shifted=0.407606 logsumexp=0.407593 torch=0.407606
```

On ordinary logits all four agree to six decimals. On the large ones the naive version returns `nan`, because $e^{802}$ overflows float32 (whose maximum is about $3.4\times10^{38}$, so anything past $e^{88}$ is `inf`), and the division `inf/inf` is `nan`.

The shifted and logsumexp forms survive for the same underlying reason. Softmax is **shift-invariant**: multiplying numerator and denominator by $e^{-m}$ gives $e^{z_i - m}/\sum_j e^{z_j - m}$, algebraically identical for any $m$. Choosing $m = \max_j z_j$ makes the largest exponent exactly $e^0 = 1$ and every other one smaller, so nothing can overflow. The logsumexp form does the same internally and additionally never materializes the probability at all, going straight from logits to loss.

Note that `[1,2,3]` and `[800,801,802]` differ by a constant offset of 799, so they define the *identical* distribution and must have the identical loss — and they do, 0.407606 both times. Shift-invariance is not just a numerical trick; it says the absolute scale of logits carries no information, only their differences.

The logsumexp version reads 0.407593 rather than 0.407606 at the large logits, a discrepancy of $1.3\times10^{-5}$. That is float32 precision loss, not a bug: near 800, consecutive representable floats are about $6\times10^{-5}$ apart, so the inputs themselves cannot represent the differences exactly. Run it in float64 and the agreement returns.

### The gradient comparison

```python
import torch

for z_val in (0.0, -2.0, -5.0, -10.0):
    z = torch.tensor([z_val], requires_grad=True)
    (-torch.log(torch.sigmoid(z))).backward()          # BCE, target 1
    g_bce = z.grad.item()

    z2 = torch.tensor([z_val], requires_grad=True)
    ((torch.sigmoid(z2) - 1)**2).backward()             # MSE on the probability
    g_mse = z2.grad.item()

    p = torch.sigmoid(torch.tensor(z_val)).item()
    print(f"z={z_val:6.1f}  p={p:.6f}  dBCE/dz={g_bce:+.6f}  dMSE/dz={g_mse:+.9f}  ratio={g_bce/g_mse:.1f}")
```

| logit $z$ | $p = \sigma(z)$ | $\partial\text{BCE}/\partial z$ | $\partial\text{MSE}/\partial z$ | ratio |
| --- | --- | --- | --- | --- |
| 0 | 0.500000 | $-0.500000$ | $-0.250000$ | 2.0 |
| $-2$ | 0.119203 | $-0.880797$ | $-0.184956$ | 4.8 |
| $-5$ | 0.006693 | $-0.993307$ | $-0.013207$ | 75.2 |
| $-10$ | 0.000045 | $-0.999955$ | $-0.000091$ | **11014.2** |

Read the two gradient columns downward and the argument is complete. Cross-entropy's gradient *grows* toward its bound of $-1$ as the model becomes more confidently wrong — exactly the behaviour you want, since a worse error should produce a stronger correction. MSE's gradient *collapses* toward zero over the same range, falling by a factor of 2,750 between $z=0$ and $z=-10$. At the point of maximum error the model receives essentially no signal telling it to change.

This is the same saturation phenomenon as Set 03's sigmoid network, arriving through the loss rather than the hidden activations, and it is why cross-entropy is not merely conventional.

### Empirical confirmation on MNIST

Same architecture, same seed, same optimizer and learning rate; only the loss differs.

```python
z = model(x)
loss = F.cross_entropy(z, y)                                       # or
loss = F.mse_loss(F.softmax(z, dim=1), F.one_hot(y, 10).float())
```

| epoch | 1 | 2 | 3 | 4 | 5 |
| --- | --- | --- | --- | --- | --- |
| cross-entropy | 94.33% | 96.30% | 96.54% | 97.22% | **97.52%** |
| MSE on softmax | 85.55% | 89.12% | 90.44% | 91.07% | **91.69%** |

MSE does train — this is the honest result, and it is worth stating because "MSE doesn't work for classification" is too strong. It trains *slower and to a worse plateau*, ending nearly six points behind after five epochs and, notably, five epochs of MSE is worse than *one* epoch of cross-entropy. The mechanism is the table above: every confidently-wrong example contributes almost nothing to the gradient, so the model's remaining errors are precisely the ones it learns least from.

---

Back to [Set 04](../04-exercises.md) · Next solutions: [Set 05](./05-solutions.md)
