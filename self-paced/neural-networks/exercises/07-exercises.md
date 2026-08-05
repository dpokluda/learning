# Exercise Set 07 — Generalization and Regularization

Companion to [Module 07](../07-generalization-and-regularization.md).

## Part A — Questionnaire

1. State the difference between an *error* and a *generalization gap*, and explain why a model with 2% training error and a 1-point gap may be worse than one with 5% training error and a 6-point gap.

2. Zhang et al. trained networks to zero error on randomly labelled data. Explain precisely what this rules out. Which theory of generalization does it break, and why does it not simply mean "deep networks don't generalize"?

3. Show that adding $\frac{\lambda}{2}\lVert\theta\rVert^2$ to the loss produces a *multiplicative* shrinkage of the weights each step, and interpret the same penalty as a Bayesian prior. What prior, with what parameter?

4. Dropout is claimed to do two different things: prevent co-adaptation, and approximate an ensemble. Explain both, then explain what the network does at test time and why the scaling factor is needed. Where does PyTorch put that scaling, and why there?

5. Early stopping is described in *Deep Learning* as equivalent to L2 regularization for a quadratic objective. Give the intuition for why they are related at all, then say what early stopping gives you that weight decay does not.

6. You have an overfitting model. In what order do you apply remedies, and why is "make the model smaller" not at the top of the list?

## Part B — Coding

**The goal, in prose.** Produce overfitting deliberately, then measure what each regularizer is actually worth against the same baseline. The point is not that regularization helps — you know that — but the *ordering* of the effects, which is not what most people guess.

**Specifics.**

*Reproduce the random-label experiment.* Take the first 1,000 MNIST training images. Train a `784 → 512 → 512 → 10` MLP twice from the same seed: once on the true labels, once on labels drawn uniformly at random. Run 100 epochs with Adam at $10^{-3}$. Report training and test accuracy for both. Predict all four numbers first.

*Run a regularizer bake-off* on the same 1,000-example subset for 40 epochs: no regularization; weight decay at $10^{-3}$ and $10^{-2}$; dropout 0.5; dropout 0.5 combined with weight decay $10^{-3}$; and data augmentation (small random rotation, translation and scale). Report training accuracy, test accuracy and **test loss** for each. Watch the test loss column especially — it will disagree with the accuracy column, and understanding why is the exercise.

*Produce an early-stopping curve.* Rerun the baseline, evaluating on the test set after every epoch, and record the epoch of minimum test loss and the epoch of maximum test accuracy. They will not be the same epoch. Explain the discrepancy and say which one you would actually stop on.

**Starter stub.**

```python
from torch.utils.data import Subset

subset = Subset(train_set, range(1000))     # small enough to memorize

aug_tf = transforms.Compose([
    transforms.RandomAffine(degrees=10, translate=(0.1, 0.1), scale=(0.9, 1.1)),
    transforms.ToTensor(),
])                                          # training data only — never the test set
```

---

Solutions: [`solutions/07-solutions.md`](./solutions/07-solutions.md) · Next: [Set 08](./08-exercises.md)
