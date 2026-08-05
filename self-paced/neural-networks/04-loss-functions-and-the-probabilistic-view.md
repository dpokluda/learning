# 04 — Loss functions and the probabilistic view

You now have a function with adjustable parameters. To train it you need a number that says how wrong it currently is — and the choice of that number is not arbitrary, not a matter of taste, and not something you should be selecting from a menu by trial and error. Nearly every loss function in mainstream deep learning is a negative log-likelihood, which means that choosing a loss is really choosing *what kind of probability distribution your model's output represents*. Once you internalize that, cross-entropy stops being a formula you memorized and becomes the only sensible thing to write down, and you gain a reliable procedure for inventing a loss when you meet a problem the textbooks do not cover.

This module derives the standard losses from that single principle, then deals with the numerical engineering that makes them work in floating point — which is where the practical bugs actually are.

> **Prerequisite:** [Module 02](./02-mathematical-foundations.md), specifically the probability section: likelihood, log-likelihood, entropy, and cross-entropy.

## The objective, stated properly

What you actually want is a model that performs well on data it has never seen. Written honestly, that is

$$J^*(\theta) = \mathbb{E}_{(\mathbf{x},y)\sim p_{\text{data}}}\big[\mathcal{L}(f(\mathbf{x};\theta),\, y)\big]$$

the expected loss over the true data-generating distribution. This quantity is not computable, because you do not have $p_{\text{data}}$ — if you did, you would not need to learn anything. What you have is a finite sample, so you substitute the average over your training set:

$$J(\theta) = \frac{1}{N}\sum_{i=1}^{N}\mathcal{L}\big(f(\mathbf{x}_i;\theta),\, y_i\big)$$

This substitution is called **empirical risk minimization**, and the entire discipline of Module 07 exists because the two quantities are not the same. Minimizing $J$ is a proxy for minimizing $J^*$, and the proxy fails exactly when the model becomes capable of exploiting the specific sample rather than the underlying pattern. Keep the distinction visible now and overfitting will feel inevitable rather than surprising later.

Note also the $1/N$. Averaging rather than summing means the gradient magnitude — and therefore the appropriate learning rate — does not depend on how many examples you happen to have in a batch. PyTorch loss functions default to `reduction='mean'` for this reason, and changing it to `'sum'` without changing your learning rate by a corresponding factor is a real and confusing bug.

## Maximum likelihood, and the machine that generates losses

Here is the principle that produces everything else. Treat the network as defining a conditional probability distribution over outputs given inputs, $p(y \mid \mathbf{x};\theta)$. Assume the training examples were drawn independently. Then the probability of having observed your entire training set under the model is the product

$$\prod_{i=1}^{N} p(y_i \mid \mathbf{x}_i;\theta)$$

and **maximum likelihood estimation** says to choose the $\theta$ that makes this as large as possible — the parameters under which the data you actually saw is least surprising. Products of thousands of sub-unit probabilities underflow to zero in floating point and differentiate awkwardly, so take the logarithm, which is monotonic and therefore preserves the argmax while turning the product into a sum. Negate to convert maximization into minimization, divide by $N$ to average, and you have

$$J(\theta) = -\frac{1}{N}\sum_{i=1}^{N} \log p(y_i \mid \mathbf{x}_i;\theta)$$

That is the **negative log-likelihood**, and it is the loss function. Not *a* loss function — for the overwhelming majority of supervised deep learning, it is *the* loss function, and the apparent variety of named losses is entirely a matter of which distribution you plugged in for $p$. The recipe is mechanical:

1. Decide what kind of thing $y$ is — a real number, a binary label, one of $K$ categories, a count.
2. Choose the distribution family that describes it.
3. Let the network output that distribution's parameters.
4. Write down the negative log of its density or mass function.
5. Discard additive constants and positive multiplicative constants, since they do not move the argmin.

Turn that crank three times and you get every loss you are likely to need.

## Gaussian → squared error

Suppose $y$ is a real number and you assume it is the network's prediction plus Gaussian noise of fixed variance: $p(y \mid \mathbf{x};\theta) = \mathcal{N}\big(y;\, \hat{y},\, \sigma^2\big)$ where $\hat{y} = f(\mathbf{x};\theta)$. The density is

$$p(y \mid \mathbf{x};\theta) = \frac{1}{\sqrt{2\pi\sigma^2}}\exp\!\left(-\frac{(y-\hat{y})^2}{2\sigma^2}\right)$$

Take the negative log and the exponential disappears, which is the entire reason exponential-family distributions are pleasant to work with:

$$-\log p = \frac{(y-\hat{y})^2}{2\sigma^2} + \tfrac{1}{2}\log(2\pi\sigma^2)$$

The second term does not involve $\theta$ at all, so it cannot affect which $\theta$ minimizes the expression — drop it. The $1/2\sigma^2$ is a positive constant, so it merely rescales the loss and can be absorbed into the learning rate — drop it. What remains is

$$\mathcal{L}_{\text{MSE}} = (y - \hat{y})^2$$

**Mean squared error is maximum likelihood under a Gaussian noise assumption.** This is not a coincidence or a convenient analogy; it is a derivation, and it tells you exactly when MSE is the wrong choice. If your errors are not roughly Gaussian — if they have heavy tails, or your data contains outliers that a Gaussian would consider impossible — then squared error is optimizing the wrong likelihood, and it will chase those outliers hard because the quadratic penalty makes a single point ten units away as important as a hundred points one unit away.

The standard alternatives are exactly the standard alternative distributions. **Mean absolute error**, $|y - \hat{y}|$, is maximum likelihood under a Laplace distribution, whose exponential tails are far more tolerant of outliers; its cost is a discontinuous gradient at zero and a constant gradient magnitude that makes fine convergence slower. **Huber loss** splices the two, behaving quadratically within $\delta$ of zero and linearly beyond it, giving MSE's smooth convergence near the optimum and MAE's outlier resistance far from it:

$$\mathcal{L}_\delta(e) = \begin{cases} \tfrac{1}{2}e^2 & |e| \le \delta \\ \delta\left(|e| - \tfrac{1}{2}\delta\right) & \text{otherwise}\end{cases}$$

In PyTorch these are `nn.MSELoss`, `nn.L1Loss`, and `nn.HuberLoss` (or `nn.SmoothL1Loss`, the same idea with $\delta$ fixed at 1).

## Bernoulli → binary cross-entropy

Now let $y \in \{0,1\}$. The natural distribution is Bernoulli with a single parameter $p = P(y=1)$, and the network must produce a number in $(0,1)$ — which is what the sigmoid is for. Set $\hat{p} = \sigma(z)$ where $z$ is the network's raw output. The Bernoulli mass function admits a well-known single-expression form,

$$p(y \mid \hat{p}) = \hat{p}^{\,y}(1-\hat{p})^{1-y}$$

which works because one of the two exponents is always zero: at $y=1$ it reads $\hat{p}$, at $y=0$ it reads $1-\hat{p}$. Negative log:

$$\mathcal{L}_{\text{BCE}} = -\big[y\log\hat{p} + (1-y)\log(1-\hat{p})\big]$$

**Binary cross-entropy.** Read it as a switch rather than a formula: when the label is 1 only the first term survives and the loss is $-\log\hat{p}$; when the label is 0 only the second survives and the loss is $-\log(1-\hat{p})$. Either way you are paying the negative log of the probability the model assigned to what actually happened.

That reading exposes the property that makes cross-entropy so much better behaved than squared error for classification. As $\hat{p}$ for the true class approaches zero, $-\log\hat p$ approaches infinity. Confident and wrong is punished without bound, so the gradient is large exactly when the model is badly mistaken. Compare squared error on a sigmoid output: when the unit saturates, $\sigma'(z) \approx 0$, and the chain rule multiplies the gradient by that near-zero factor, so a confidently wrong saturated unit learns almost nothing. Cross-entropy's logarithm cancels the sigmoid's exponential precisely, and the gradient with respect to the logit collapses to the beautifully simple $\hat{p} - y$ — prediction minus target, with no saturation factor at all. That cancellation is why classification uses cross-entropy and not MSE, and it is a good example of a loss and an output activation being designed as a matched pair.

## Categorical → cross-entropy and softmax

For $K$ mutually exclusive classes, the distribution is categorical: $K$ probabilities that are non-negative and sum to one. The network emits $K$ unconstrained real numbers — the **logits** $\mathbf{z}$ — and the **softmax** converts them:

$$\hat{p}_k = \mathrm{softmax}(\mathbf{z})_k = \frac{e^{z_k}}{\sum_{j=1}^{K}e^{z_j}}$$

The exponential guarantees positivity, and dividing by the sum guarantees normalization. Because $\exp$ is monotonic, softmax preserves the ordering of the logits, so `argmax` gives the same answer before and after — which is why you never need softmax at inference time if all you want is the predicted class.

Softmax has a property worth knowing because it explains a numerical trick and a common confusion. It is **shift-invariant**: adding the same constant $c$ to every logit leaves the output unchanged, since $e^{z_k+c}/\sum_j e^{z_j+c} = e^c e^{z_k}/(e^c\sum_j e^{z_j})$ and the $e^c$ cancels. So logits are only meaningful up to a common additive offset; only their differences carry information. It is also **temperature-controlled**: dividing the logits by $T$ before the exponential sharpens the distribution as $T \to 0$ toward a one-hot vector and flattens it as $T \to \infty$ toward uniform. This is exactly the "temperature" knob on a language model's sampling, and it is the same softmax you are looking at here.

Taking the negative log of the categorical likelihood with a one-hot target, and using $\mathbf{p}$ for the true distribution:

$$\mathcal{L}_{\text{CE}} = -\sum_{k=1}^{K} p_k\log\hat{p}_k = -\log \hat{p}_{y}$$

The sum collapses because every $p_k$ is zero except at the true class. **Cross-entropy with a hard label is just the negative log probability of the correct class** — the same conclusion Module 02 reached arithmetically, now derived. Substituting the softmax gives a form that is worth writing out, because it is the one you differentiate in Module 05:

$$\mathcal{L}_{\text{CE}} = -z_y + \log\sum_{j=1}^{K}e^{z_j}$$

And the gradient with respect to the logits is, once again, remarkably clean:

$$\frac{\partial \mathcal{L}_{\text{CE}}}{\partial z_k} = \hat{p}_k - p_k$$

Predicted distribution minus true distribution. That one expression is the starting point of every backward pass in a classifier you will ever train, and its simplicity is the mathematical payoff of pairing softmax with cross-entropy rather than mixing and matching.

Recall from Module 02 that minimizing cross-entropy is equivalent to minimizing the KL divergence $D_{\mathrm{KL}}(p\|\hat p)$, since they differ by the entropy of the fixed true distribution. So "maximize likelihood," "minimize cross-entropy," and "make the predicted distribution close to the true one" are three names for one procedure — and the loss reported in your training logs has a direct interpretation: it is the average number of nats of surprise your model experiences per example.

## The numerical engineering that actually matters

The mathematics above is exact; floating-point arithmetic is not, and the naive implementation breaks. Consider a logit of 800. Computing $e^{800}$ overflows to `inf` in float32 — the maximum representable value is about $3.4\times10^{38}$, and $e^{89}$ already exceeds it. Now `inf/inf` gives `nan`, the `nan` propagates through the backward pass, and every parameter in your network becomes `nan` in a single step. This is one of the most common ways a training run dies.

The fix uses the shift-invariance property from above. Subtract the maximum logit before exponentiating:

$$\mathrm{softmax}(\mathbf{z})_k = \frac{e^{z_k - \max_j z_j}}{\sum_i e^{z_i - \max_j z_j}}$$

The result is mathematically identical, but now the largest exponent is exactly $e^0 = 1$, and everything else is smaller, so overflow is impossible. Underflow to zero can still occur for very negative shifted logits, but a term that underflows was negligible anyway. The same trick applied to the logarithm gives the **log-sum-exp** identity:

$$\log\sum_j e^{z_j} = m + \log\sum_j e^{z_j - m}, \qquad m = \max_j z_j$$

This is why the loss you should call is `nn.CrossEntropyLoss`, which takes **logits** and does softmax and log in one numerically stable fused operation, rather than applying `softmax` yourself and then taking a log. Applying softmax before `CrossEntropyLoss` is not a syntax error and will not crash — it will train, badly, because you have applied softmax twice and squashed your effective logit range. It is one of the most common silent bugs in beginner PyTorch code. The same reasoning gives `nn.BCEWithLogitsLoss` its preference over `nn.BCELoss`.

The whole story in code:

```python
import torch, torch.nn as nn, torch.nn.functional as F

logits = torch.tensor([[1.0, 2.0, 3.0]])
target = torch.tensor([2])

# these three are the same computation, from most to least assembled
print(F.cross_entropy(logits, target).item())                        # 0.40761
print(F.nll_loss(F.log_softmax(logits, dim=1), target).item())       # 0.40761
print((-logits[0, 2] + torch.logsumexp(logits, dim=1)).item())       # 0.40761

# naive vs stable, on logits large enough to overflow float32
big = torch.tensor([[800.0, 801.0, 802.0]])
print(torch.exp(big) / torch.exp(big).sum())        # tensor([[nan, nan, nan]])
print(F.softmax(big, dim=1))                        # tensor([[0.0900, 0.2447, 0.6652]])
print(F.cross_entropy(big, target).item())          # 0.40761 — same as the small logits!
```

That last line is the shift-invariance property demonstrated: `[800, 801, 802]` and `[1, 2, 3]` differ by a constant offset, so they define the identical distribution and the identical loss — but only the stable implementation can compute it.[^m4-verified]

## Deviating from the recipe, deliberately

Maximum likelihood is the default, not a law, and there are a few well-motivated departures worth knowing.

**Label smoothing** replaces the one-hot target with a slightly softened version — $1-\epsilon$ on the true class and $\epsilon/(K-1)$ spread over the rest, with $\epsilon$ typically 0.1. Under a hard target, cross-entropy is minimized only as the true logit runs off to infinity, which drives the model toward extreme overconfidence; smoothing gives it a finite optimum and empirically improves both calibration and accuracy. It was introduced with Inception-v3 and is standard in modern image classification and machine translation.[^m4-smoothing] In PyTorch it is one argument: `nn.CrossEntropyLoss(label_smoothing=0.1)`.

**Class weighting** addresses imbalance. If 99% of your examples are negative, a model that always predicts negative achieves 99% accuracy and zero usefulness, and unweighted cross-entropy is largely indifferent to this because the rare class contributes little to the average. Passing `weight=` to `nn.CrossEntropyLoss` scales each class's contribution, typically inversely to its frequency. Focal loss goes further, multiplying by $(1-\hat p_y)^\gamma$ so that easy, already-correct examples contribute almost nothing and the gradient concentrates on hard ones — it was designed for dense object detection, where the background/foreground imbalance is extreme.[^m4-focal]

**Task-specific losses** exist where likelihood is awkward to express. Contrastive and triplet losses for metric learning optimize relative distances rather than a likelihood; the losses in generative adversarial networks come from a game-theoretic minimax formulation rather than MLE. These are genuine departures and Module 14 places them on the map. But when you meet a new supervised problem, start by asking what distribution the output represents, and you will be right far more often than not.

## Before you move on

A loss function is a negative log-likelihood, so choosing a loss means choosing a distribution: Gaussian gives you squared error, Bernoulli gives you binary cross-entropy, categorical gives you softmax cross-entropy. Cross-entropy beats squared error for classification because the logarithm cancels the output nonlinearity's exponential, leaving the gradient $\hat{p} - y$ with no saturation factor, which means a confidently wrong model receives a large corrective signal rather than a vanishing one. And the numerically stable fused implementation is not an optimization detail — feeding probabilities to `CrossEntropyLoss` instead of logits is the single most common silent bug in this material.

If you can derive MSE from the Gaussian likelihood in four lines, explain why softmax is shift-invariant and what that buys you numerically, and say what the gradient of cross-entropy with respect to the logits is without looking it up, you are ready. That last expression is about to become the first thing you compute in every backward pass. [Exercise Set 04](./exercises/04-exercises.md) has you implement stable softmax and cross-entropy from scratch, verify them against PyTorch, and measure exactly what the softmax-then-`CrossEntropyLoss` bug costs.

Next, [Module 05](./05-backpropagation-and-autodiff.md) closes the loop. You have a function and a measure of its wrongness; what remains is to compute the gradient of the second with respect to the parameters of the first — efficiently enough to do it millions of times.

## Sources

[^m4-verified]: Every number in that snippet was executed and verified while writing this module, including the `nan` from the naive softmax on logits of 800.

[^m4-smoothing]: Christian Szegedy et al., ["Rethinking the Inception Architecture for Computer Vision"](https://arxiv.org/abs/1512.00567), CVPR 2016, Section 7, introduces label smoothing. For an analysis of *why* it helps, see Rafael Müller, Simon Kornblith and Geoffrey Hinton, ["When Does Label Smoothing Help?"](https://arxiv.org/abs/1906.02629), NeurIPS 2019 — which also documents a case where it hurts, namely knowledge distillation.

[^m4-focal]: Tsung-Yi Lin et al., ["Focal Loss for Dense Object Detection"](https://arxiv.org/abs/1708.02002), ICCV 2017.

**Further reading.** *Deep Learning* [Chapter 5.5](https://www.deeplearningbook.org/contents/ml.html) develops maximum likelihood estimation carefully, and [Chapter 6.2](https://www.deeplearningbook.org/contents/mlp.html) derives output units and their matched losses in exactly the framing used here — it is the single best source for this module's material. *Dive into Deep Learning* [Section 4.1](https://d2l.ai/chapter_linear-classification/softmax-regression.html) covers softmax regression with the information-theoretic reading and runnable code. The PyTorch documentation for [`nn.CrossEntropyLoss`](https://pytorch.org/docs/stable/generated/torch.nn.CrossEntropyLoss.html) and [`nn.BCEWithLogitsLoss`](https://pytorch.org/docs/stable/generated/torch.nn.BCEWithLogitsLoss.html) is authoritative on the fused-and-stable behavior, and both pages state explicitly that they expect logits. The [CS231n linear classification notes](https://cs231n.github.io/linear-classify/) compare softmax and SVM losses with unusually good intuition-building, including the numerical stability discussion.
