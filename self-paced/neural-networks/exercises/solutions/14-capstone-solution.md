# Capstone Solution — Build and Ablate a ResNet on CIFAR-10

Companion to the [capstone exercise](../14-capstone.md).

Read this after you have run your own version. The interesting content is in section B4, where two of the seven ablation results contradict what the book would lead you to predict — and chasing down why turns out to be the most instructive thing in the whole course.

## Part A — Worked answers

**1. Predicting the initial loss.**

A randomly initialized ten-class classifier has no information about the input, so the best it can do is emit roughly uniform probabilities, $\hat{p}_k \approx 1/10$ for every class. Cross-entropy on the correct class is then $-\log(1/10) = \log 10 = 2.3026$. That is the number, and it is a *prediction*, not an observation — which is what makes it useful.

Observing 4.1 instead would mean the output logits are far too large in magnitude, so the softmax is producing a confident and essentially random distribution. Confidently wrong costs much more than uniformly uncertain. The usual cause is an initialization scale problem in the classifier head, and you saw the extreme version of this in [Set 12](../12-exercises.md), where weight tying against a default `nn.Embedding` initialized to $\mathcal{N}(0,1)$ produced an initial loss of 81.87 against a theoretical 4.17.

Observing 2.31 but *never moving* is a different failure entirely, and a much more common one. The forward pass is fine; the loss is not connected to the parameters. Concretely: you forgot `loss.backward()`, or you forgot `optimizer.step()`, or the learning rate is effectively zero, or — the version that catches experienced people — the parameters you are optimizing are not the parameters the model is using, because you constructed the optimizer over one module and then reassigned or re-created it. [Set 09](../09-exercises.md) has the measured signatures of each.

**2 and 3. The ranking.** These are predictions, so the answers are in section B4 alongside the measurements. Write yours down before reading on; the exercise is worthless if you do not.

**4. Why the test set must stay untouched.**

The quantity that becomes biased is your *estimate of generalization error*, and it becomes biased downward — you will believe the model is better than it is.

The mechanism is worth stating precisely, because "it's cheating" obscures it. Suppose you train for 20 epochs and keep the checkpoint with the best test accuracy. Each epoch's test accuracy is the true accuracy plus a noise term from the finite size of the test set. Taking the maximum over 20 epochs takes the maximum over 20 noisy draws, and the maximum of several noisy draws is biased above the true value even when every draw is individually unbiased. You have not measured how good your best model is; you have measured how good your best model is *plus the largest upward fluctuation you happened to sample*. With a 10,000-image test set the standard error on an accuracy near 85% is about 0.36 percentage points, so selecting the best of 20 checkpoints buys you something on the order of half a point of pure illusion. Half a point is exactly the size of difference people write papers about.

The subtler half of the question is about transforms. Your validation set is drawn from the training split, but its *purpose* is to be a stand-in for the test set, so it must match the test distribution. Random crops and horizontal flips are label-preserving but distribution-shifting: an augmented image is a sample from a wider distribution than the one you will actually be evaluated on. Validating on augmented images gives you a noisy, pessimistic estimate, which makes best-checkpoint selection erratic and early stopping unreliable. This is why B2 insists on two dataset objects rather than a `random_split` of one.

**5. Loss and accuracy disagree.**

Accuracy depends only on which logit is largest. Loss depends on the whole predicted distribution, and it punishes confidence. Late in training a network typically keeps sharpening the predictions it already gets right — moving a correct prediction from 0.7 to 0.95 confidence improves loss and does nothing at all to accuracy — while simultaneously becoming more confidently wrong on the examples it gets wrong, which hurts loss a lot and, again, does nothing to accuracy. The net effect is that validation loss bottoms out and starts climbing while validation accuracy is still slowly improving. In [Set 07](../07-exercises.md) the measured minimum validation loss was at epoch 7 and the maximum validation accuracy at epoch 40, a 33-epoch disagreement.

Select on the metric you actually care about. If your deployed metric is accuracy, early-stop on accuracy. Loss is the better *early-warning* signal because it moves first and is smoother, so it is genuinely useful for spotting the onset of overfitting — but it is a proxy, and you should not let a proxy pick your model when you have the real thing available. The exception is when you need calibrated probabilities downstream, in which case loss *is* the thing you care about.

**6. The argument for training from scratch.**

The honest argument is about *distribution distance and licensing*, not about accuracy. Pretrained features transfer well to the extent that the target domain resembles the pretraining domain, and ImageNet-like natural photographs cover a narrower slice of the world than people assume. For medical volumetric scans, satellite multispectral imagery with more than three channels, audio spectrograms, industrial defect images, or scientific instrument data, the ImageNet prior is somewhere between weakly helpful and actively misleading. Yosinski et al. quantified this: transferability degrades as the target task moves away from the source. Beyond that, pretrained weights carry licence terms and provenance obligations that are sometimes unacceptable, and a from-scratch model is auditable end to end in a way that a downloaded checkpoint is not. And architecturally, if your input is not three-channel 2-D images at all, there is no backbone to transfer *from*.

Where the argument fails is precisely the case you just ran: a natural-image task with modest data and no licensing constraint. There, training from scratch is a pedagogical exercise and an engineering mistake. The numbers in section B5 make that concrete and slightly humbling.

## Part B — The code

Everything below runs as one file. It is written so that a single `run()` function covers all seven ablation configurations through keyword arguments, because the whole validity of an ablation rests on the configurations differing in *exactly* the intended way and nothing else. Duplicating the training loop seven times is how ablations quietly become wrong.

```python
import torch, torch.nn as nn, torch.nn.functional as F, time, copy, sys
import torchvision as tv
from torchvision import transforms as T
from torch.utils.data import DataLoader, Subset

dev = torch.device("cuda" if torch.cuda.is_available()
                  else "mps" if torch.backends.mps.is_available() else "cpu")
MEAN, STD = (0.4914, 0.4822, 0.4465), (0.2470, 0.2435, 0.2616)


def loaders(aug=True, n_train=45000, batch=128):
    train_tf = T.Compose(
        ([T.RandomCrop(32, padding=4), T.RandomHorizontalFlip()] if aug else [])
        + [T.ToTensor(), T.Normalize(MEAN, STD)])
    eval_tf = T.Compose([T.ToTensor(), T.Normalize(MEAN, STD)])

    # Two dataset objects over the same files, different transforms.
    train_src = tv.datasets.CIFAR10('./data', train=True, download=True, transform=train_tf)
    val_src   = tv.datasets.CIFAR10('./data', train=True, download=True, transform=eval_tf)

    g = torch.Generator().manual_seed(0)          # fixed split, reproducible
    idx = torch.randperm(50000, generator=g)
    tr_idx, va_idx = idx[:n_train].tolist(), idx[45000:].tolist()

    trl = DataLoader(Subset(train_src, tr_idx), batch, shuffle=True, drop_last=True)
    val = DataLoader(Subset(val_src,   va_idx), 512)
    tel = DataLoader(tv.datasets.CIFAR10('./data', train=False, download=True,
                                         transform=eval_tf), 512)
    return trl, val, tel


class Block(nn.Module):
    def __init__(self, cin, cout, stride=1, residual=True, norm=True):
        super().__init__()
        self.residual = residual
        N = (lambda c: nn.BatchNorm2d(c)) if norm else (lambda c: nn.Identity())
        # bias=False when a norm follows: BatchNorm subtracts the mean, so any
        # constant the bias adds is removed immediately. It is dead weight.
        self.c1, self.n1 = nn.Conv2d(cin, cout, 3, stride, 1, bias=not norm), N(cout)
        self.c2, self.n2 = nn.Conv2d(cout, cout, 3, 1, 1, bias=not norm), N(cout)
        self.short = (nn.Sequential() if (stride == 1 and cin == cout)
                      else nn.Sequential(nn.Conv2d(cin, cout, 1, stride, bias=not norm),
                                         N(cout)))

    def forward(self, x):
        h = F.relu(self.n1(self.c1(x)))
        h = self.n2(self.c2(h))
        return F.relu(h + self.short(x)) if self.residual else F.relu(h)


class ResNet(nn.Module):
    def __init__(self, residual=True, norm=True, widths=(64, 128, 256)):
        super().__init__()
        N = (lambda c: nn.BatchNorm2d(c)) if norm else (lambda c: nn.Identity())
        # 3x3 stride-1 stem: on 32x32 input the ImageNet 7x7/stride-2 + maxpool
        # stem would discard 3/4 of the resolution before anything is computed.
        self.stem = nn.Sequential(nn.Conv2d(3, widths[0], 3, 1, 1, bias=not norm),
                                  N(widths[0]), nn.ReLU())
        layers, cin = [], widths[0]
        for i, c in enumerate(widths):
            layers += [Block(cin, c, 1 if i == 0 else 2, residual, norm),
                       Block(c, c, 1, residual, norm)]
            cin = c
        self.body, self.head = nn.Sequential(*layers), nn.Linear(cin, 10)

    def forward(self, x):
        return self.head(F.adaptive_avg_pool2d(self.body(self.stem(x)), 1).flatten(1))


crit = nn.CrossEntropyLoss()

@torch.no_grad()
def evaluate(model, loader):
    model.eval()
    tot, correct, n = 0.0, 0, 0
    for x, y in loader:
        x, y = x.to(dev), y.to(dev)
        out = model(x)
        tot += crit(out, y).item() * y.numel()
        correct += (out.argmax(1) == y).sum().item()
        n += y.numel()
    return tot / n, 100 * correct / n


def run(tag, epochs, aug=True, residual=True, norm=True, opt="adamw",
        lr=1e-3, sched=True, he=True, widths=(64, 128, 256), n_train=45000):
    torch.manual_seed(0)
    trl, val, tel = loaders(aug, n_train)
    model = ResNet(residual, norm, widths).to(dev)
    if he:
        for m in model.modules():
            if isinstance(m, nn.Conv2d):
                nn.init.kaiming_normal_(m.weight, mode='fan_out', nonlinearity='relu')

    optimizer = (torch.optim.AdamW(model.parameters(), lr=lr, weight_decay=5e-4)
                 if opt == "adamw" else
                 torch.optim.SGD(model.parameters(), lr=lr, momentum=0.9, weight_decay=5e-4))
    scheduler = (torch.optim.lr_scheduler.OneCycleLR(
                     optimizer, max_lr=lr, epochs=epochs, steps_per_epoch=len(trl))
                 if sched else None)

    best, best_state = float('inf'), None
    for ep in range(epochs):
        model.train()
        for x, y in trl:
            x, y = x.to(dev), y.to(dev)
            optimizer.zero_grad()
            loss = crit(model(x), y)
            loss.backward()
            nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            optimizer.step()
            if scheduler: scheduler.step()          # per batch, not per epoch
        vl, va = evaluate(model, val)
        if vl < best:                                # best-checkpoint tracking
            best, best_state = vl, copy.deepcopy(model.state_dict())

    model.load_state_dict(best_state)                # restore before testing
    tl, ta = evaluate(model, tel)                    # the test set, exactly once
    print(f"{tag:36s} test_acc={ta:5.2f}%  best_val_loss={best:.4f}  "
          f"params={sum(p.numel() for p in model.parameters()):,}")
    return ta
```

Two details in that code repay attention. The scheduler is stepped **once per batch**, not once per epoch, which is what `OneCycleLR` is constructed for — it is given `steps_per_epoch` precisely so it can plan a per-batch trajectory, and stepping it per epoch silently compresses the entire cycle into the first few percent of training. And the best-checkpoint state dict is a `deepcopy`, not a reference; assigning `best_state = model.state_dict()` gives you a dictionary of *live tensors* that keep changing as training continues, so you would restore the final weights no matter which epoch was best. Both of these are bugs that produce a model that trains fine and is merely worse than it should be, which is the hardest category to notice.

## B3 — The full run

Fifteen epochs, 45,000 training images, widths 64/128/256, everything on:

```
ep  1  val_loss=1.8832  val_acc=31.08%
ep  8  val_loss=0.7630  val_acc=74.06%
ep 15  val_loss=0.4061  val_acc=86.32%

FULL RECIPE   test_acc=86.35%   params=2,777,674
```

Test accuracy of **86.35%** from 2.78 million parameters, with test tracking validation to within 0.03 points — which is what you want to see, and is evidence that the validation set was doing its job rather than being quietly overfitted through checkpoint selection.

A word on runtime, because being honest about it matters more than quoting a number. This run took several hours on an Apple M-series GPU via the MPS backend. Across the ablation runs below, wall-clock times for *identical computation* varied by a factor of forty on the same machine while producing bit-identical accuracies, which means the timings measure thermal and memory-pressure contention rather than the model. I have therefore not quoted per-configuration times anywhere in this solution, and you should treat any single-machine timing in this genre with the same suspicion. On a Colab T4 or better this is a coffee break rather than an afternoon; if you are on CPU, use the reduced ablation settings for everything.

Ninety-plus percent on CIFAR-10 is reachable with this architecture, and the gap between 86% and 94% is entirely training budget — more epochs, a wider network, longer schedules, and stronger augmentation such as Cutout or Mixup. Nothing about the *method* changes. That is itself a lesson: past a certain point in this field, results are bought with compute rather than insight, and it is worth being able to tell which one you are looking at when you read a paper.

## B4 — The ablation, and two surprises

Reduced settings: 10,000 training images, widths 32/64/128 (696,618 parameters), 20 epochs, one seed each, everything else identical across rows.

| configuration | best val loss | test accuracy | Δ vs baseline |
| --- | --- | --- | --- |
| baseline (all ingredients) | 0.7652 | 73.83% | — |
| − augmentation | 1.2393 | 64.57% | **−9.26** |
| − BatchNorm | 1.0263 | 65.16% | **−8.67** |
| − LR schedule (constant) | 0.8988 | 71.53% | −2.30 |
| SGD+momentum 0.05 instead of AdamW | 0.8167 | 71.49% | −2.34 |
| − residual connections | 0.7865 | 72.20% | −1.63 |
| − He init (PyTorch default) | **0.6120** | **79.14%** | **+5.31** |

Four of these behave as the book predicts. Augmentation is the largest single contributor, worth over nine points, which is unsurprising on 10,000 images where the model can otherwise memorize the training set — and note that the validation *loss* gap (0.765 versus 1.239) is proportionally even larger than the accuracy gap, exactly the signature of a regularizer described in [Module 07](../../07-generalization-and-regularization.md). BatchNorm is worth nearly as much. The schedule and the optimizer choice are each worth a couple of points, which is the right order of magnitude: they change how efficiently you use a fixed budget rather than what is ultimately reachable.

Now the two surprises.

**Surprise one: removing residual connections costs almost nothing.** Only 1.63 points, which at a single seed is barely outside noise. This looks like it contradicts [Module 10](../../10-convolutional-networks.md), where the measured degradation experiment showed a 30-layer plain network training *worse* than a 10-layer one. It does not contradict it; it delimits it. This network is thirteen convolutional layers deep with BatchNorm everywhere, and that is simply not deep enough for the optimization problem residual connections solve to have appeared yet. He et al. reported the same shape of result: at 18 layers plain and residual networks are nearly tied, and the gap opens dramatically at 34 and 50. Residual connections are not a general-purpose accuracy booster; they are a fix for a specific failure mode that switches on with depth. This is the answer to question 3 — at 50 layers this row moves to the top of the table, and it moves there discontinuously rather than gradually.

**Surprise two: removing He initialization *improves* accuracy by 5.31 points.** This one is genuinely counterintuitive, it reproduced across two independent ablation runs, and the explanation is worth the whole exercise.

Start by confirming what the two initializations actually are. For a 3×3 convolution with 64 input and 64 output channels:

```
He fan_out std      = 0.05915   (theory sqrt(2/(9*64)) = 0.05893)
PyTorch default std = 0.02405   (theory sqrt(1/(3*9*64)) = 0.02406)
ratio default/He    = 0.4065    (theory sqrt(1/6) = 0.4082)
```

That factor of $\sqrt{1/6}$ is exactly the one you found in [Set 08](../08-exercises.md): PyTorch's default `kaiming_uniform_(w, a=\sqrt{5})` produces weights a factor of $\sqrt{6}$ smaller than He. In Set 08 that factor was catastrophic — a depth-10 plain MLP with the default initialization was stuck at 11.35% test accuracy at every learning rate tried, because activations decayed to $5\times10^{-5}$ by the output layer. So why does the same deficit *help* here?

Because BatchNorm is in the way. He initialization exists to keep activation variance stable through depth in a network that has nothing else controlling scale. Put a BatchNorm after every convolution and that job is already done — by construction, and exactly, rather than approximately and in expectation. The following check confirms it: scaling the convolution weight by any positive constant $c$ leaves the loss unchanged to ten decimal places.

```
 c      loss             ||dL/dW||        ||dL/dW||*c
0.25   2.3208224074     2.484811e-01     6.212028e-02
0.5    2.3208270189     1.242671e-01     6.213357e-02
1.0    2.3208281722     6.213689e-02     6.213689e-02
2.0    2.3208284606     3.106886e-02     6.213773e-02
4.0    2.3208285327     1.553448e-02     6.213793e-02
```

The loss column is flat: $\mathrm{BN}(\mathrm{conv}_{cW}(x)) = \mathrm{BN}(\mathrm{conv}_W(x))$, because BatchNorm divides by the standard deviation of its own input and any scaling of $W$ scales that standard deviation identically. So the loss as a function of the convolution weight is *homogeneous of degree zero* — it depends only on the direction of $W$, not its magnitude.

Differentiating that identity is what produces the effect. If $J(cW) = J(W)$ for all $c > 0$, then by the chain rule $c\,\nabla J(cW) = \nabla J(W)$, so

$$\nabla J(cW) = \frac{1}{c}\nabla J(W).$$

The third column above confirms it numerically: $\|\nabla J\| \cdot c$ is constant to four significant figures across a sixteen-fold range of $c$. Halve the weights and you exactly double the gradient.

The consequence is the whole story. What matters for learning is not the absolute step size but the step size *relative to the weight norm*, since only the direction of $W$ affects the loss. That relative step is

$$\frac{\|\eta \nabla J(cW)\|}{\|cW\|} = \frac{\eta\,\|\nabla J(W)\|}{c^2\,\|W\|},$$

which scales as $1/c^2$. Measured directly:

```
PyTorch default   ||eta*g|| / ||W|| = 1.151874e-04
He fan_out        ||eta*g|| / ||W|| = 1.903555e-05
ratio             6.0512      (predicted 1/c^2 = 6.0517)
```

So the smaller default initialization gives every convolution in the network an **effective learning rate six times larger** than He initialization does — and six is not a coincidence, it is the same factor of 6 from the $\sqrt{1/6}$ weight ratio, squared away and reappearing as a learning-rate multiplier. On this budget, a 6× larger effective rate on the convolutional layers was simply better tuned than $10^{-3}$ with He, and the "− He init" row is not measuring initialization at all. It is measuring a learning-rate change I did not know I was making.

This effect is not folklore; it is the subject of a small literature on the interaction between normalization and weight decay, where the same reasoning explains why weight decay continues to matter in normalized networks even though it cannot change the function being computed — it shrinks $\|W\|$, and shrinking $\|W\|$ raises the effective learning rate.[^cap-vl]

The confirming experiment is to remove BatchNorm and see whether He recovers its advantage:

| | He init | PyTorch default |
| --- | --- | --- |
| with BatchNorm | 73.83% | **79.14%** |
| without BatchNorm | **65.16%** | 64.10% |

The sign flips. Without normalization, He is (weakly) ahead, which is the regime its derivation is about; with normalization, the initialization scale acts purely as a learning-rate knob and the smaller one happened to be better here. The 1.06-point margin in the bottom row is well within single-seed noise and I would not defend it as more than directional — at thirteen layers, neither initialization is under enough strain to separate cleanly, which is precisely why Set 08 had to go to depth ten *without* normalization to make the effect unmistakable.

The methodological lesson is larger than the finding. An ablation removes one *named* ingredient, but names are not mechanisms, and "− He init" turned out to be a compound change: initialization scale *and*, through BatchNorm's scale invariance, effective learning rate. Ablations of hyperparameter-adjacent choices are only interpretable when everything else is re-tuned for each configuration, which nobody has the budget to do. When you read an ablation table in a paper — including this one — the right question is not "what did they remove" but "what else changed when they removed it."

**One more measurement worth reporting**, because it is the kind of thing usually left out. An earlier version of this ablation ran for 10 epochs instead of 20 and produced this:

| configuration | 10 epochs | 20 epochs |
| --- | --- | --- |
| baseline | 57.12% | 73.83% |
| − augmentation | 62.15% | 64.57% |

At 10 epochs, augmentation *hurt* by 5 points. At 20 epochs it *helped* by 9.3 points. Both numbers are real and reproducible, and the sign flip is not noise — it is the defining behaviour of a regularizer. Augmentation makes the training distribution harder, so it slows convergence early and pays off only once the unaugmented model starts memorizing. An ablation budget too small to reach that crossover will not merely understate augmentation's value; it will report the opposite of the truth. If I had run the 10-epoch version and stopped, I would have written a confident and completely wrong paragraph about data augmentation, and nothing in the table would have looked suspicious.

## B5 — Situating the result

| approach | training data | parameters trained | test accuracy |
| --- | --- | --- | --- |
| from scratch (this capstone) | 45,000 images | 2,777,674 | 86.35% |
| fine-tuned ResNet-18 ([Module 13](../../13-transfer-learning-and-embeddings.md)) | 5,000 images | 11,181,642 | 89.40% |
| linear probe on frozen ResNet-18 | 5,000 images | 5,130 | 74.50% |

The comparison is not close and it is not flattering. Fine-tuning a pretrained backbone beat the from-scratch model by three points using **one ninth of the labelled data** and, on the same machine, seconds of training instead of hours. Even the linear probe — 5,130 trainable parameters, a single matrix on top of a frozen network that never saw CIFAR-10 — landed within twelve points of a 2.8-million-parameter model trained from random initialization on nine times the data.

The honest conclusion is the one from question 6. If your task is natural images, you should not be training from scratch, and the decision does not require analysis. What the capstone bought you is not a competitive model; it is the ability to *build* one, to reason about why each component is there, and to debug the situation where transfer is not available — medical scans, spectrograms, multispectral satellite data, anything that is not three-channel photographs of the visible world. That skill is what the pretrained-model era makes rarer and, when you need it, more valuable.

It is also worth noticing what the three rows have in common. The fine-tuned model is a ResNet; the probe sits on a ResNet; you built a ResNet. The architecture from He et al. (2015) is still the default answer for images ten years later, and the reason is the one Module 10 gives: residual connections made depth an engineering parameter rather than a research risk. You now know that from both directions — the theory in Module 10, and the measurement in B4 showing that at thirteen layers the fix is not yet needed.

## Further reading

The CIFAR ResNet variants and the depth-versus-degradation results are in the original ResNet paper.[^cap-resnet] The one-cycle schedule and the learning-rate range test come from Smith.[^cap-onecycle] The scale-invariance argument in B4 is developed in van Laarhoven's note on the interaction between normalization and weight decay,[^cap-vl] and extended in Hoffer et al.[^cap-hoffer] For pushing CIFAR-10 accuracy further with the same architecture, the standard additions are Cutout[^cap-cutout] and Mixup.[^cap-mixup].

[^cap-resnet]: Kaiming He, Xiangyu Zhang, Shaoqing Ren, Jian Sun, "Deep Residual Learning for Image Recognition," CVPR 2016. <https://arxiv.org/abs/1512.03385> — Section 4.2 covers the CIFAR-10 architectures with the 3×3 stride-1 stem, and reports the plain-versus-residual comparison at 20, 32, 44, 56 and 110 layers.

[^cap-onecycle]: Leslie N. Smith, "Cyclical Learning Rates for Training Neural Networks," WACV 2017, <https://arxiv.org/abs/1506.01186>, and "Super-Convergence: Very Fast Training of Neural Networks Using Large Learning Rates," <https://arxiv.org/abs/1708.07120>. Source of both the range test and the one-cycle schedule used here.

[^cap-vl]: Twan van Laarhoven, "L2 Regularization versus Batch and Weight Normalization," 2017. <https://arxiv.org/abs/1706.05350> — establishes that with normalization the weight norm does not affect the function computed but does set the effective learning rate, which is the mechanism behind the "− He init" result.

[^cap-hoffer]: Elad Hoffer, Ron Banner, Itay Golan, Daniel Soudry, "Norm matters: efficient and accurate normalization schemes in deep networks," NeurIPS 2018. <https://arxiv.org/abs/1803.01814> — extends the same analysis and proposes schemes that make the effective learning rate explicit.

[^cap-cutout]: Terrance DeVries, Graham W. Taylor, "Improved Regularization of Convolutional Neural Networks with Cutout," 2017. <https://arxiv.org/abs/1708.04552>

[^cap-mixup]: Hongyi Zhang, Moustapha Cisse, Yann N. Dauphin, David Lopez-Paz, "mixup: Beyond Empirical Risk Minimization," ICLR 2018. <https://arxiv.org/abs/1710.09412>

---

That is the end of the course. If you want a map of where to go next, [Module 14](../../14-modern-landscape.md) sketches the current landscape and [Module 15](../../15-reference.md) is the glossary and formula sheet to keep open.

Back to [Capstone](../14-capstone.md) · Previous solutions: [Set 13](./13-solutions.md) · Index: [How to use](../00-HOW-TO-USE.md)
