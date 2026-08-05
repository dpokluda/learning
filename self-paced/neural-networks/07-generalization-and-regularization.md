# 07 — Generalization, overfitting, and regularization

Everything so far has been machinery for driving the training loss down. This module explains why that is not the goal, and why doing it too well is actively harmful.

Here is the experiment that makes the problem impossible to ignore. Take 1,000 MNIST images, throw away the labels, and replace them with uniformly random digits — so there is no relationship whatsoever between any image and its target. Now train a 784→512→512→10 MLP on this nonsense with Adam for a hundred epochs. It reaches **100% training accuracy**. It has perfectly learned a function that maps each specific image to its assigned random label, and its test accuracy is **8.53%**, which is chance.[^m7-random] For comparison, the identical network on the identical images with *correct* labels also reaches 100% training accuracy, and gets 88.95% on the test set.

Two runs, identical training loss, wildly different value. Whatever "the model learned" means, training loss cannot measure it. This experiment is a small reproduction of Zhang and colleagues' *Understanding deep learning requires rethinking generalization*, which demonstrated the same thing at ImageNet scale and forced the field to admit that its classical explanations of generalization were inadequate.[^m7-zhang]

> **Prerequisite:** [Module 06](./06-optimization.md), and the empirical-risk framing from [Module 04](./04-loss-functions-and-the-probabilistic-view.md) — the distinction between expected loss over the data distribution and the average over your sample.

## Three splits, and the discipline they enforce

If training performance cannot be trusted, you need held-out data — and you need *two* held-out sets, not one, for a reason that is easy to rationalize away and expensive to get wrong.

The **training set** is what gradients are computed on. The **validation set** is what you use to make decisions: which architecture, which learning rate, when to stop, which of your fourteen experiments to keep. The **test set** is touched once, at the very end, to estimate performance on genuinely unseen data.

The reason validation and test must be separate is that *any* decision made using a dataset leaks information from it into the model. If you try fifty configurations and pick the one with the best validation accuracy, you have optimized against the validation set — not by gradient descent, but by selection — and its estimate is now optimistically biased. The bias is real and can be several percent. The test set stays sealed precisely so that you retain one unbiased number. Typical splits are 80/10/10, or for large datasets a fixed 10,000-example validation set is plenty; MNIST and CIFAR-10 ship with a designated test set, so carve your validation set out of the training portion and leave the test set alone until you are done.

The gap between training and held-out performance is the **generalization gap**, and it is the quantity this module is about.

## Bias, variance, and capacity

The classical account is worth having because its vocabulary is universal, even though — as the next section shows — its picture is incomplete for deep networks.

Suppose the truth is $y = f(\mathbf{x}) + \varepsilon$ with noise of variance $\sigma^2$, and $\hat{f}$ is your model, itself random because it depends on which training sample you happened to draw. The expected squared error at a point decomposes exactly into three terms:

$$\mathbb{E}\big[(y - \hat{f}(\mathbf{x}))^2\big] = \underbrace{\big(\mathbb{E}[\hat{f}(\mathbf{x})] - f(\mathbf{x})\big)^2}_{\text{bias}^2} + \underbrace{\mathbb{E}\big[(\hat{f}(\mathbf{x}) - \mathbb{E}[\hat{f}(\mathbf{x})])^2\big]}_{\text{variance}} + \underbrace{\sigma^2}_{\text{irreducible}}$$

**Bias** is systematic error from the model being too rigid to represent the truth — a straight line fitting a curve is biased no matter how much data you give it. **Variance** is sensitivity to the particular training sample — a model so flexible that a different draw of 1,000 examples would produce a substantially different function. The third term is noise in the world and no model can beat it, which is a useful humility check when you are chasing the last percent.

The classical story says these trade off against each other as capacity grows, producing a U-shaped test error: high bias and low variance on the left (**underfitting**), the sweet spot in the middle, high variance and low bias on the right (**overfitting**). The diagnostic signature of each is unambiguous in the training curves. Underfitting: training loss is high and validation loss tracks it closely — the model cannot even fit the data it has seen. Overfitting: training loss is low and still falling while validation loss has flattened or turned upward. That divergence point is the single most informative feature of a loss curve, and learning to read it is most of what Module 09's debugging practice amounts to.

## Where the classical picture breaks

The random-label experiment says something the U-curve cannot accommodate. A network with enough capacity to memorize arbitrary labels has, in the classical accounting, effectively unbounded variance and should be useless. Yet the same network, with the same capacity, trained on real labels generalizes well. Capacity alone therefore does not determine generalization; something about the *interaction* between the architecture, the optimizer, and the structure in real data does.

That something has a name — **implicit regularization** — and an honest status: it is an active research area rather than a solved problem. The observable facts are reasonably clear. Gradient descent does not find an arbitrary loss-zero solution; among the many parameter settings that fit the training data, it preferentially finds ones with small norm and low complexity, essentially because it starts near zero and stops as soon as the loss is minimized. Real data has structure that is easier to fit than noise is, and networks demonstrably learn the structured, generalizable patterns *first* and memorize the exceptions later — which is exactly why early stopping works. SGD's gradient noise appears to bias solutions toward flatter regions of the loss surface, which correlate with better generalization.

The most striking correction to the classical picture is **double descent**.[^m7-belkin] As you increase model capacity past the point where the model can exactly fit the training data — the *interpolation threshold* — test error first rises as classical theory predicts, and then, remarkably, falls again, often to below the classical sweet spot. Nakkiran and colleagues showed the same phenomenon as a function of training time and dataset size, not just model size.[^m7-nakkiran] The modern regime of enormously over-parameterized networks lives on the far right of that second descent, which is why "just make it bigger" has been such a productive strategy despite contradicting a generation of statistical intuition.

The practical stance to take from all of this: the classical vocabulary of bias, variance, and overfitting is still how you *diagnose* a model from its curves, and it is still correct that a small model trained on small data can be too rigid or too flexible. But the prescription "reduce capacity to reduce overfitting" is often the wrong move for deep networks. Increasing capacity and regularizing well is usually better, and that is the recipe Module 09 recommends.

## Weight decay

The oldest regularizer adds a penalty on the size of the weights to the objective:

$$\tilde{J}(\theta) = J(\theta) + \frac{\lambda}{2}\|\theta\|_2^2$$

Its effect on the update is the reason to think of it as *decay* rather than as a penalty. The gradient of the penalty is $\lambda\theta$, so the SGD update becomes

$$\theta_{t+1} = \theta_t - \eta\big(\nabla J + \lambda\theta_t\big) = (1-\eta\lambda)\,\theta_t - \eta\nabla J$$

Every step multiplies the parameters by a factor slightly below one before applying the gradient. Weights are continuously pulled toward zero, and only those that earn their keep — that reduce the data loss enough to overcome the pull — survive at large magnitude. Since the size of the weights controls how sharply the function can vary, this is a direct preference for smoother functions.

There is also a clean Bayesian reading: adding $\frac{\lambda}{2}\|\theta\|^2$ to a negative log-likelihood is exactly the negative log of a zero-mean Gaussian prior on the parameters, so weight decay is maximum *a posteriori* estimation with a Gaussian prior. Module 04's machine for generating losses extends to generating regularizers. Analogously, an L1 penalty $\lambda\|\theta\|_1$ corresponds to a Laplace prior and drives weights to exactly zero rather than merely small, producing genuine sparsity — useful for feature selection and compression, rarely used as the primary regularizer in deep networks.

Two implementation notes that matter. Recall from Module 06 that with Adam you should use `AdamW`, because L2-in-the-loss and true weight decay are not equivalent once the update is normalized per-parameter. And biases and normalization parameters are conventionally *excluded* from weight decay — there is no reason to shrink a bias toward zero, and decaying a BatchNorm scale parameter actively fights the normalization. PyTorch's default applies decay to everything you hand it, so excluding them requires parameter groups:

```python
decay, no_decay = [], []
for name, p in model.named_parameters():
    if p.ndim <= 1 or name.endswith(".bias"):     # biases, norm weights
        no_decay.append(p)
    else:
        decay.append(p)

optimizer = torch.optim.AdamW([
    {"params": decay,    "weight_decay": 0.01},
    {"params": no_decay, "weight_decay": 0.0},
], lr=3e-4)
```

## Dropout

Dropout is the regularizer most specific to neural networks and the one with the most interesting justification.[^m7-dropout] During training, each unit's output is set to zero independently with probability $p$ — a fresh random mask every forward pass. At test time nothing is dropped.

The obvious question is why this helps rather than simply degrading the network. Two complementary answers, both illuminating.

The **ensemble** view: a network with $n$ units has $2^n$ possible dropout masks, so training with dropout approximately trains $2^n$ sub-networks that share parameters, and testing without dropout approximates averaging their predictions. Ensembles reliably beat their members — this is why random forests work — and dropout buys an exponentially large ensemble at the cost of one model.

The **co-adaptation** view, which is the one that guides practice: without dropout, units can develop fragile dependencies, where unit A is only useful because unit B compensates for its errors. Such conspiracies fit the training data and break on new data. When any unit may vanish at any moment, no unit can rely on a specific partner, so each is forced to be independently useful. Dropout makes the learned features redundant and robust rather than specialized and brittle.

The implementation detail worth knowing is **inverted dropout**, which is what all modern frameworks use. Naively, dropping a fraction $p$ of units reduces the expected input to the next layer by a factor $(1-p)$, so training-time and test-time statistics differ and you would need to scale activations at test time. Inverted dropout instead divides the surviving activations by $(1-p)$ *during training*, restoring the expectation immediately and leaving inference untouched:

$$\text{train:}\quad \tilde{a}_i = \frac{m_i}{1-p}\,a_i,\;\; m_i\sim\text{Bernoulli}(1-p) \qquad\qquad \text{test:}\quad \tilde{a}_i = a_i$$

This is the source of the most common dropout bug: forgetting `model.eval()`. In eval mode `nn.Dropout` becomes the identity; in train mode it keeps sampling masks. Evaluate a model in train mode and you get noisy, inconsistent, and pessimistic accuracy — and the symptom is confusing because the model is not broken, just being measured wrong.

```python
model.train()   # dropout ON, BatchNorm updates running stats
# ... training loop ...
model.eval()    # dropout OFF, BatchNorm uses running stats
with torch.no_grad():
    ...
```

Rates of 0.5 are standard for fully-connected layers, 0.1–0.3 for convolutional ones (which have fewer parameters and are less prone to overfit), and 0.1 for Transformers. Dropout is placed *after* the activation. Its use has declined somewhat in convolutional vision models, where BatchNorm's own noise provides regularization and heavy dropout can hurt, but it remains standard in MLPs and Transformers.

## Early stopping

The cheapest regularizer is simply to stop. Monitor validation loss each epoch, keep a copy of the parameters from the best epoch so far, and halt when it has not improved for some number of epochs — the *patience*, typically 5 to 20. At the end, restore the best checkpoint rather than keeping the final one.

Restoring the best checkpoint is the part people skip and it is the part that matters; if you stop at epoch 30 because epoch 20 was the best, and you keep the epoch-30 weights, you have gained nothing. Early stopping is regularization in a real sense, not just economy: it limits how far the parameters can travel from their initialization, which for quadratic objectives can be shown to be equivalent to an L2 penalty whose strength is set by the number of steps.[^m7-goodfellow-es] It also costs nothing and gives you the training-time saving for free, which is why it belongs in every training loop.

```python
best_val, best_state, patience, bad = float("inf"), None, 10, 0
for epoch in range(max_epochs):
    train_one_epoch(...)
    val_loss = evaluate(model, val_loader)
    if val_loss < best_val:
        best_val, bad = val_loss, 0
        best_state = {k: v.detach().clone() for k, v in model.state_dict().items()}
    else:
        bad += 1
        if bad >= patience:
            break
model.load_state_dict(best_state)      # the step everyone forgets
```

## Data augmentation

Every regularizer so far constrains the model. Augmentation attacks the problem from the other end by enlarging the data, and when it is applicable it is usually the most effective option available — more data is the only thing that reduces variance without increasing bias.

The principle is to apply transformations that change the input while provably preserving the label. A shifted, slightly rotated, slightly scaled handwritten 7 is still a 7, so training on those variants teaches the network an invariance it would otherwise have to infer from limited examples. Augmentation is thus a way of *injecting your prior knowledge about the task* into training — the same role architecture plays in Modules 10 through 12, delivered through the data instead.

Which transformations are label-preserving is entirely domain-specific, and getting it wrong is a real failure mode. Horizontal flips are excellent for natural photographs and catastrophic for digits and text, where they turn a 2 into something that is not a 2. Heavy color jitter is fine for object recognition and destructive for a task where color is the signal. Always look at your augmented samples before training on them; it takes two minutes and catches the errors that would otherwise cost you a day.

```python
from torchvision import transforms

train_tf = transforms.Compose([
    transforms.RandomCrop(32, padding=4),        # translation invariance
    transforms.RandomHorizontalFlip(),           # fine for CIFAR, NOT for MNIST
    transforms.ToTensor(),
    transforms.Normalize((0.4914, 0.4822, 0.4465), (0.2470, 0.2435, 0.2616)),
])
test_tf = transforms.Compose([                   # never augment the test set
    transforms.ToTensor(),
    transforms.Normalize((0.4914, 0.4822, 0.4465), (0.2470, 0.2435, 0.2616)),
])
```

Note that augmentation applies to training data only. Beyond these classical transforms, **Mixup** trains on convex combinations of pairs of images *and* their labels, and **CutMix** pastes a patch of one image into another with proportionally mixed labels; both are standard in modern image classification and both are surprisingly effective given how strange they look.[^m7-mixup] For text, augmentation is harder because most edits change meaning, which is part of why the field turned to pretraining instead (Module 13).

## What actually happens: measured

Prescriptions are cheap, so here is a measured experiment on the running example. A 784→512→512→10 MLP, trained with Adam for 40 epochs on a deliberately tiny 1,000-example subset of MNIST so that overfitting is guaranteed, evaluated on the full 10,000-image test set:[^m7-reg]

| Configuration | Train acc | Train loss | Test acc | Test loss | Gap |
|---|---|---|---|---|---|
| No regularization | 100.00% | 0.0005 | 88.79% | 0.586 | 11.2 |
| Weight decay 1e-3 | 100.00% | 0.0097 | 88.63% | **0.420** | 11.4 |
| Weight decay 1e-2 | 99.50% | 0.0900 | 87.15% | 0.413 | 12.4 |
| Dropout 0.5 | 100.00% | 0.0010 | **89.67%** | 0.504 | 10.3 |
| Dropout 0.5 + wd 1e-3 | 100.00% | 0.0056 | 89.12% | 0.410 | 10.9 |

Three things in that table are worth more than the prescriptions above. First, the baseline is a textbook overfit: training loss of 0.0005 and 100% training accuracy against 88.8% test accuracy. Second, the accuracy gains from regularization are **small** — under a point — which is an honest and useful corrective to the impression that dropout is a magic switch. Third, and most interesting, the test *loss* improves dramatically, from 0.586 to about 0.41, even where accuracy barely moves. The regularized models are no better at picking the right answer but are substantially better **calibrated**: they are less confidently wrong. If your application uses the probabilities rather than just the argmax — thresholding, ranking, downstream decision-making — that difference matters more than the accuracy column does. And note that at weight decay 1e-2 the regularization is too strong: training accuracy starts to fall and test accuracy follows it down. Every regularizer has a dose, and past it you are simply adding bias.

## Diagnosing and treating, in order

Put the pieces together into a procedure. Read the two curves, identify which regime you are in, and act accordingly.

| Symptom | Diagnosis | What to do |
|---|---|---|
| Train loss high, val loss ≈ train loss | Underfitting | Bigger model, train longer, higher learning rate, *less* regularization |
| Train loss low, val loss much higher and rising | Overfitting | More data or augmentation first, then dropout / weight decay, then early stopping |
| Both losses low and close | Working | Stop, or scale up to see if you can do better |
| Train loss will not fall at all | Bug, not a modelling problem | Module 09 — this is almost never fixed by regularization |

The ordering inside the overfitting row is deliberate. More data beats every regularizer, and augmentation is the cheapest way to approximate more data. Then architectural regularizers. Early stopping is free and should simply always be on. And reducing model size is listed nowhere, because as the double-descent discussion above suggests, the modern recipe is to build a model large enough to overfit, confirm that it *does* overfit, and then regularize it back — which gives you a model that is at least capable enough, and a clear knob to turn.

One last technique that always works and is often forgotten: **ensembling**. Train five models with different random seeds and average their predicted probabilities. This reliably gives a percent or two, because independent models make partly independent errors that cancel in the average. It costs five times the compute and five times the inference, which is why it is standard practice in competitions and less common in production.

## Before you move on

Training loss is not the objective, and the random-label experiment proves it with a network that reaches 100% training accuracy while learning nothing. Validation and test sets must be separate because every decision you make against a dataset leaks information into the model. The classical bias–variance vocabulary is how you diagnose curves, but its prescription to shrink capacity is often wrong for deep networks, where double descent and implicit regularization put the good regime on the far side of interpolation. Weight decay multiplicatively shrinks weights each step and is a Gaussian prior; dropout forces redundant, non-co-adapted features and requires `model.eval()` at inference; early stopping is free and you must remember to restore the best checkpoint; and augmentation is the most effective of all when you can find label-preserving transformations.

If you can explain why a model that reaches 100% accuracy on random labels invalidates a purely capacity-based theory of generalization, why inverted dropout scales during training rather than at test time, and why the regularized models in the measured table had barely better accuracy but much better test loss, then you can read a training curve properly — which is the skill the next module is built on. [Exercise Set 07](./exercises/07-exercises.md) has you run the random-label experiment and a six-way regularizer bake-off on a deliberately small training set, where the differences are large enough to see.

Next, [Module 08](./08-initialization-and-normalization.md) turns to the numerical health of a deep network: why gradients vanish or explode as a quantitative fact, how to initialize so they do not, and what BatchNorm and LayerNorm actually accomplish.

## Sources

[^m7-random]: Measured while writing this module: 784→512→512→10 MLP, Adam at 1e-3, 100 epochs on 1,000 MNIST images. With true labels, 100.00% train / 88.95% test. With uniformly random labels, 100.00% train / 8.53% test. Script in the [Module 07 solutions](./exercises/solutions/07-solutions.md).

[^m7-zhang]: Chiyuan Zhang, Samy Bengio, Moritz Hardt, Benjamin Recht and Oriol Vinyals, ["Understanding deep learning requires rethinking generalization"](https://arxiv.org/abs/1611.03530), ICLR 2017. Best-paper award; the random-label experiment above is a small reproduction of their Section 2.

[^m7-belkin]: Mikhail Belkin, Daniel Hsu, Siyuan Ma and Soumik Mandal, ["Reconciling modern machine learning practice and the bias-variance trade-off"](https://arxiv.org/abs/1812.11118), PNAS 116(32), 2019. Introduces the double-descent curve.

[^m7-nakkiran]: Preetum Nakkiran et al., ["Deep Double Descent: Where Bigger Models and More Data Hurt"](https://arxiv.org/abs/1912.02292), ICLR 2020. Extends double descent to training epochs and dataset size.

[^m7-dropout]: Nitish Srivastava, Geoffrey Hinton, Alex Krizhevsky, Ilya Sutskever and Ruslan Salakhutdinov, ["Dropout: A Simple Way to Prevent Neural Networks from Overfitting"](https://jmlr.org/papers/v15/srivastava14a.html), JMLR 15, 2014. Both the ensemble and co-adaptation arguments are the authors' own, in Sections 2 and 3.

[^m7-goodfellow-es]: Goodfellow, Bengio and Courville, *Deep Learning*, [Section 7.8](https://www.deeplearningbook.org/contents/regularization.html), gives the equivalence between early stopping and L2 regularization for a quadratic objective.

[^m7-mixup]: Hongyi Zhang et al., ["mixup: Beyond Empirical Risk Minimization"](https://arxiv.org/abs/1710.09412), ICLR 2018; Sangdoo Yun et al., ["CutMix"](https://arxiv.org/abs/1905.04899), ICCV 2019.

[^m7-reg]: Measured while writing this module, same setup as the random-label experiment but 40 epochs and varying regularization. Numbers are single-seed and the differences between the regularized rows are within run-to-run noise for accuracy but not for loss; the qualitative conclusions (large gap without regularization, calibration improves more than accuracy) held across seeds.

**Further reading.** *Deep Learning* [Chapter 7](https://www.deeplearningbook.org/contents/regularization.html) is the definitive survey of regularization for deep models and covers every technique here plus several this module omits. [Chapter 5](https://www.deeplearningbook.org/contents/ml.html) develops capacity, bias, variance, and the classical learning-theory framing. *Dive into Deep Learning* [Sections 3.6–3.7](https://d2l.ai/chapter_linear-regression/generalization.html) and [5.6](https://d2l.ai/chapter_multilayer-perceptrons/dropout.html) cover generalization and dropout with runnable experiments. The [CS231n neural networks notes, part 2](https://cs231n.github.io/neural-networks-2/) discuss regularization from a practitioner's angle, and [part 3](https://cs231n.github.io/neural-networks-3/) covers reading learning curves — both are excellent. For the modern generalization puzzle specifically, the Zhang and Nakkiran papers above are readable primary sources and repay the effort.
