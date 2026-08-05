# Solutions — Set 07

Worked answers for [Exercise Set 07](../07-exercises.md).

## Part A — Answers

**1. Error versus gap.**

Test error is what you care about; the generalization gap is only a diagnostic. The gap is $\text{err}_{\text{test}} - \text{err}_{\text{train}}$, and it tells you how much of your training performance failed to transfer — nothing about the absolute quality of the model.

The first model has test error $2 + 1 = 3\%$. The second has $5 + 6 = 11\%$. The first is better by a wide margin despite a "worse-looking" gap being absent, because the gap is not the objective. A model that predicts a constant has a gap of zero and is useless.

The reason this matters practically is that a small gap is often evidence of *under*fitting, not of a well-tuned model. If you regularize until the gap closes, you will usually have regularized past the optimum. The correct procedure is to watch validation error directly and pick its minimum, treating the gap as a hint about *which* remedy to reach for — a large gap points to regularization, a small gap with high error points to capacity or optimization.

**2. What the random-label experiment rules out.**

Zhang et al. trained standard architectures to *zero* training error on CIFAR-10 with the labels randomly permuted, and — critically — on images with the pixels randomly permuted too. The networks memorized perfectly. Test accuracy was chance.

What this rules out is any explanation of generalization that depends only on **the hypothesis class and the training error**. Classical statistical learning theory bounds test error by training error plus a complexity term (VC dimension, Rademacher complexity) that depends on the function class. But a class rich enough to fit arbitrary random labels on $n$ points has effective capacity at least $n$, so the bound becomes vacuous — it permits arbitrarily bad test error. Yet the *same* network, the *same* optimizer and the *same* hyperparameters generalize well on real labels. Whatever explains generalization therefore cannot be a property of the architecture alone.

It does not mean deep networks fail to generalize; the experiment shows they generalize *when the data has structure*. The resolution has to involve the interaction between data, architecture and optimization: real data has learnable regularity, networks learn regular structure faster than they memorize noise, and SGD started near zero and stopped early lands in low-complexity solutions. That last cluster of ideas travels under the name *implicit regularization*, and it is an active research area rather than a settled theory — an honest thing to know about the state of the field.

**3. Weight decay two ways.**

Add $\frac{\lambda}{2}\lVert\theta\rVert^2$ to $J(\theta)$. The gradient of the penalty is $\lambda\theta$, so

$$\theta \leftarrow \theta - \eta\left(\nabla J + \lambda\theta\right) = (1 - \eta\lambda)\,\theta - \eta\nabla J$$

The first term is the multiplicative shrinkage: before the gradient step, every weight is scaled by the constant $1 - \eta\lambda$, slightly below 1. With $\eta = 0.1$ and $\lambda = 10^{-3}$ the factor is 0.9999 — a weight untouched by any gradient signal decays geometrically toward zero. So weight decay is a constant pull toward the origin that only survives where the data pushes back.

Bayesian reading: MAP estimation maximizes $\log p(\mathcal{D}\mid\theta) + \log p(\theta)$. Take $p(\theta) = \mathcal{N}(0, \sigma^2 I)$; its log density is $-\lVert\theta\rVert^2/(2\sigma^2)$ plus a constant. Negating to get a loss gives exactly the L2 penalty with $\lambda = 1/\sigma^2$. So **L2 regularization is a zero-mean isotropic Gaussian prior on the weights**, and $\lambda$ is its inverse variance: larger $\lambda$ means a tighter prior, a stronger belief that weights are near zero. This also explains why you should not decay bias terms or normalization parameters — you have no prior belief that they are near zero, and shrinking a BatchNorm $\gamma$ toward zero is actively harmful.

**4. Dropout, both readings.**

*Co-adaptation.* Setting each unit to zero independently with probability $p$ means no unit can rely on any particular other unit being present. A feature that only works in combination with a specific partner is fragile: half the time the partner is gone. Under dropout, features are pressured to be individually useful, which yields a more redundant, more robust representation — analogous to why a team where everyone can cover two roles survives absences better than one built on irreplaceable specialists.

*Ensemble.* A network with $n$ droppable units has $2^n$ possible thinned sub-networks. Each minibatch trains one of them, all sharing weights. Training with dropout therefore approximately trains an exponentially large ensemble at the cost of one model. At test time, rather than averaging $2^n$ forward passes, you use the full network once, which the original paper argues approximates the geometric mean of the ensemble's predictions.

*Test time.* Dropout is turned off — every unit is present. That changes the expected input to the next layer: during training a unit received on average a fraction $(1-p)$ of its inputs, so with all of them present the pre-activation would be inflated by $1/(1-p)$. A correction is required. PyTorch uses **inverted dropout**: it divides by $(1-p)$ *during training*, so the expected activation matches the full network and inference is entirely unmodified. That placement matters practically — it means the deployed model is a plain network with no dropout-specific code, and it means the only thing you must remember is to call `model.eval()`, which flips `nn.Dropout` to the identity. Forgetting that call is one of the most common bugs in PyTorch training code, and its signature is evaluation metrics that are noisy and worse than training metrics on the same data.

**5. Early stopping and L2.**

Intuition: gradient descent starting from $\theta \approx 0$ moves outward from the origin, fastest along the directions where the loss curves most steeply — the large-curvature eigen-directions of the Hessian. Stopping after $t$ steps means the parameters have travelled a bounded distance from zero, and travelled least far along the flat directions where the data provides little signal. L2 regularization also shrinks toward zero, and also shrinks most in the low-curvature directions where the penalty dominates the data term. For a quadratic objective these coincide exactly, with the number of steps $t$ playing the role of $1/\lambda$: training longer is regularizing less. *Deep Learning* §7.8 works the correspondence out.

What early stopping gives you that weight decay does not is that **it requires no tuning and costs nothing**. Weight decay needs a search over $\lambda$, with a full training run per value. Early stopping determines the equivalent amount of regularization from the validation curve during the single run you were doing anyway, and as a bonus it saves the compute you would have spent overfitting. That is why it should simply always be on. The one thing you must remember is to *restore the best checkpoint* — early stopping that halts training but hands back the final weights gives you the halting behaviour without the regularization benefit.

**6. Order of remedies.**

More data first, because it dominates everything else and has no downside. Data augmentation second, since it is the cheapest available approximation to more data. Architectural regularizers — dropout, weight decay — third, tuned against validation. Early stopping is not really on the list because it should already be running. Transfer learning from a pretrained model ([Module 13](../../13-transfer-learning-and-embeddings.md)) belongs near the top whenever a suitable pretrained model exists, since it is effectively borrowing someone else's data.

"Make the model smaller" is not at the top because the modern empirical picture inverts the classical prescription. Double descent shows test error can fall again *past* the interpolation threshold, so shrinking a model that is overfitting may move you from the good over-parameterized regime into the bad critical regime. And practically, a model too small to overfit is a model whose ceiling you have not measured. The reliable recipe is: build something large enough to reach ~100% training accuracy, confirm that it does, then regularize it back down. That gives you a known-capable model and a single clean knob.

## Part B — Reference solutions

### The random-label experiment

```python
import torch, torch.nn as nn
from torch.utils.data import DataLoader, TensorDataset
from torchvision import datasets, transforms

train = datasets.MNIST("./data", train=True, download=True, transform=transforms.ToTensor())
X      = torch.stack([train[i][0] for i in range(1000)])
y_true = torch.tensor([train[i][1] for i in range(1000)])
torch.manual_seed(0)
y_rand = torch.randint(0, 10, (1000,))

test_loader = DataLoader(datasets.MNIST("./data", train=False, transform=transforms.ToTensor()),
                         batch_size=1000)

def run(tag, y, epochs=100):
    torch.manual_seed(0)
    model = nn.Sequential(nn.Flatten(), nn.Linear(784, 512), nn.ReLU(),
                          nn.Linear(512, 512), nn.ReLU(), nn.Linear(512, 10))
    loader = DataLoader(TensorDataset(X, y), batch_size=64, shuffle=True)
    opt, criterion = torch.optim.Adam(model.parameters(), lr=1e-3), nn.CrossEntropyLoss()
    for _ in range(epochs):
        model.train()
        for a, b in loader:
            opt.zero_grad(); criterion(model(a), b).backward(); opt.step()
    model.eval()
    with torch.no_grad():
        tr = (model(X).argmax(1) == y).float().mean().item() * 100
        c = n = 0
        for a, b in test_loader:
            c += (model(a).argmax(1) == b).sum().item(); n += b.numel()
    print(f"{tag:16s} train_acc={tr:6.2f}%  test_acc={100*c/n:6.2f}%")

run("true labels",  y_true)
run("RANDOM labels", y_rand)
```
```
true labels      train_acc= 100.00%  test_acc= 88.79%
RANDOM labels    train_acc= 100.00%  test_acc=  8.53%
```

The random-label row is the whole point: **100% training accuracy, 8.53% test accuracy** — below the 10% you would get by guessing, because the model has confidently learned a mapping that is actively wrong. Nothing about the training loss distinguishes this run from the useful one. Both reached zero. If you had been monitoring only the training curve, these two models would have looked identical.

Two details worth noticing. The random-label run takes visibly more epochs to reach zero than the true-label run: real structure is genuinely easier to fit than noise, which is the empirical fact underpinning why early stopping works. And this is a real bug signature, not just a thought experiment — a shuffle applied to your labels but not your inputs produces exactly this, and the only thing that will tell you is a validation set.

### Regularizer bake-off

1,000 MNIST examples, `784 → 512 → 512 → 10`, Adam at $10^{-3}$, 40 epochs, identical seed.

| configuration | train acc | test acc | test loss |
| --- | --- | --- | --- |
| baseline, no regularization | 100.00% | 88.79% | 0.5861 |
| weight decay $10^{-3}$ | 100.00% | 88.63% | 0.4204 |
| weight decay $10^{-2}$ | 99.80% | 87.15% | 0.4128 |
| dropout 0.5 | 100.00% | 89.67% | 0.5041 |
| dropout 0.5 + wd $10^{-3}$ | 100.00% | 89.12% | 0.4098 |
| **data augmentation** | 98.30% | **93.43%** | **0.2032** |

Read the accuracy column and the loss column against each other, because they tell different stories.

**Augmentation wins decisively** — 4.6 points of accuracy over the baseline, roughly ten times the effect of any weight-space regularizer, and the only configuration that fails to reach 100% training accuracy. That is not a defect; it is the mechanism. Rotation, translation and scaling generate effectively unlimited fresh examples, so the model never sees the same input twice and cannot memorize the training set. This is the single most important result in the set, and it is why the ordering in [Module 07](../../07-generalization-and-regularization.md)'s remedy table puts more data and augmentation ahead of everything else. Augmentation is an approximation to more data, and more data beats cleverness.

**Weight decay barely moves accuracy but transforms the loss.** At $10^{-3}$ accuracy is *down* 0.16 points while test loss falls 28%, from 0.586 to 0.420. The predictions are not much more often right; they are much better calibrated. The baseline is confidently wrong on the examples it gets wrong — driven to near-zero training loss, it has pushed its logits to extremes — and cross-entropy punishes that heavily. Weight decay caps the logit magnitudes and the confidence with them. If your downstream use needs probabilities rather than argmaxes, this column is the one that matters and the accuracy column understates the benefit substantially.

**Dropout gives the best accuracy among the weight-space methods** (+0.88) and combines with weight decay to give the best loss of the non-augmented runs (0.4098). The combination is the standard recipe for a reason: they regularize differently, dropout on the representation and decay on the parameters, so the effects partly add.

**Too much of a good thing hurts.** Weight decay at $10^{-2}$ is worse than at $10^{-3}$ on accuracy — the model is now slightly underfitting, visible in the training accuracy dropping below 100%. Every regularizer has an optimum and you find it on validation data.

A caveat to state plainly: 1,000 examples is a deliberately extreme setting chosen so overfitting is unambiguous, and the *magnitudes* here would shrink on the full 60,000-example set where the baseline already generalizes well. The *ordering* is what transfers.

### Early-stopping curve

Baseline configuration, evaluated on the test set after every epoch.

| epoch | train acc | train loss | test acc | test loss |
| --- | --- | --- | --- | --- |
| 1 | 80.50% | 0.7574 | 75.79% | 0.8684 |
| 2 | 90.30% | 0.3341 | 85.44% | 0.4854 |
| 3 | 93.10% | 0.2303 | 86.11% | 0.4412 |
| **7** | — | — | 87.57% | **0.4397** |
| 8 | 99.90% | 0.0294 | 87.74% | 0.4456 |
| 10 | 100.00% | 0.0145 | 87.74% | 0.4783 |
| 15 | 100.00% | 0.0039 | 88.56% | 0.4995 |
| 20 | 100.00% | 0.0020 | 88.63% | 0.5247 |
| 30 | 100.00% | 0.0009 | 88.55% | 0.5624 |
| **40** | 100.00% | 0.0005 | **88.79%** | 0.5861 |

This is the textbook picture and then a twist. Training loss falls monotonically toward zero, exactly as it must. Test loss falls for seven epochs, bottoms at 0.4397, and then climbs steadily for the remaining thirty-three — the classic overfitting signature, and the reason early stopping exists.

The twist is that **test accuracy keeps improving the whole time**, peaking at epoch 40. Minimum test loss is at epoch 7; maximum test accuracy is at epoch 40. Stopping on loss would have cost you 1.2 points of accuracy.

The explanation is that the two metrics measure different things. Cross-entropy is sensitive to *confidence*; accuracy only to which logit is largest. Between epochs 7 and 40 the model becomes far more confident on everything, which increases the penalty on its mistakes faster than it reduces the penalty on its successes — so mean loss rises. Meanwhile a handful of borderline examples cross the decision boundary in the right direction, so accuracy creeps up. The model is getting slightly more often right and substantially more overconfident at the same time.

Which to stop on is a genuine decision, not a technicality, and the answer is *stop on the metric you are actually going to be judged by*. If you need a ranking, a threshold, or calibrated probabilities — anything downstream of the confidence, including ensembling — stop on loss. If a hard argmax is the deliverable, stop on accuracy. What you should not do is monitor one and report the other. In practice, tracking both and looking at the shape is cheap; the divergence itself is informative, because a widening gap between "loss is worsening" and "accuracy is improving" is the readable signature of a model drifting into overconfidence, and it is the cue to reach for weight decay or label smoothing.

One last discipline point: the code above evaluates on the *test* set every epoch for pedagogical clarity, which in real work is exactly the leak [Module 09](../../09-practical-training-and-debugging.md) warns against. Every early-stopping decision made against a set contaminates it. Use a held-out validation split for stopping, and touch the test set once, at the end.

---

Back to [Set 07](../07-exercises.md) · Next solutions: [Set 08](./08-solutions.md)
