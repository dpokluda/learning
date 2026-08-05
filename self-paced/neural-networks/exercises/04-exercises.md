# Exercise Set 04 — Loss functions and the probabilistic view

Companion to [Module 04](../04-loss-functions-and-the-probabilistic-view.md).

## Part A — Questionnaire

1. Derive mean squared error from maximum likelihood. State the noise model you are assuming, and say what happens to the derivation if the noise variance is not constant across examples.

2. Derive cross-entropy from maximum likelihood for a $K$-class problem. Where exactly does the $-\log$ come from, and why does the sum over classes collapse to a single term?

3. The gradient of cross-entropy with respect to the logits is $\hat{\mathbf{y}} - \mathbf{y}$. Show why the softmax derivative and the log derivative cancel, and explain in one sentence why this cancellation is the reason cross-entropy trains classifiers well.

4. Someone trains a classifier with MSE on softmax outputs. It trains, slowly, and gets stuck. Explain precisely what goes wrong, in terms of the gradient at a confidently *wrong* prediction.

5. `F.cross_entropy` expects logits, not probabilities. What exactly goes wrong if you apply a softmax first and pass those in? Will you notice? What will the symptoms look like?

6. Name a task where neither cross-entropy nor MSE is the right loss, say what distribution you would assume instead, and write down the resulting negative log-likelihood.

## Part B — Coding

**The goal, in prose.** Understand cross-entropy well enough to implement it correctly from the definition, break the naive implementation on purpose, and then measure the gradient pathology that makes MSE the wrong loss for classification. This is a short set with a high ratio of insight to code.

**Specifics.**

First, implement cross-entropy three ways from scratch — the naive $-\log(\text{softmax}(z)_y)$, a version using the max-shift trick, and the $-z_y + \operatorname{logsumexp}(z)$ form — and verify all three agree with `F.cross_entropy` on ordinary logits. Then feed all four the logits `[800.0, 801.0, 802.0]` and see which survive. Explain why the shifted and logsumexp forms give the correct answer while the naive one does not, and confirm that `[1, 2, 3]` and `[800, 801, 802]` produce identical losses.

Second, the gradient comparison. For a binary problem, take a single logit $z$ and a target of 1, and compute $\partial\mathcal{L}/\partial z$ for both binary cross-entropy and squared error on the sigmoid output, at $z = -2$, $-5$ and $-10$ — that is, at predictions that are increasingly confidently wrong. Tabulate both gradients. The result is the whole argument of the module.

Third, a small empirical check: train the same MNIST MLP twice, once with cross-entropy and once with MSE on one-hot targets, same seed and learning rate, and compare the loss curves.

**Starter stub.**

```python
import torch, torch.nn.functional as F

def ce_naive(logits, target):
    p = logits.exp() / logits.exp().sum(dim=1, keepdim=True)
    return -p[range(len(target)), target].log().mean()

def ce_shifted(logits, target):
    ...   # subtract the row max before exponentiating

def ce_logsumexp(logits, target):
    ...   # -z_y + logsumexp(z)

logits = torch.tensor([[1.0, 2.0, 3.0]]); target = torch.tensor([2])
big    = torch.tensor([[800.0, 801.0, 802.0]])
```

---

Solutions: [`solutions/04-solutions.md`](./solutions/04-solutions.md) · Next: Set 05
