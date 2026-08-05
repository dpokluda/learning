# Capstone — Build and Ablate a ResNet on CIFAR-10

Companion to the whole book. This is the last thing in it.

Every exercise up to now isolated one idea so you could see it clearly. That is the right way to learn a mechanism and the wrong way to learn engineering, because the thing nobody tells you about deep learning practice is that no single ingredient is decisive. A working model is a *stack* of decisions — initialization, normalization, architecture, augmentation, optimizer, schedule, checkpointing — each of which contributes a few points, and the difference between a model that gets 60% and one that gets 90% is usually not one brilliant choice but eight ordinary ones compounding.

So the capstone has two halves, and the second is the one that matters. First you build a small ResNet on CIFAR-10 using every technique in this book, and get it to a respectable accuracy. Then you take that working model apart, one ingredient at a time, and *measure* what each one was actually buying you. The first half proves you can assemble the pieces. The second half is where you find out which of your beliefs about them were true.

> **Prerequisite:** the whole book, but load-bearing especially are [Module 06](../06-optimization.md) (optimizers and schedules), [Module 07](../07-generalization-and-regularization.md) (augmentation, weight decay, early stopping), [Module 08](../08-initialization-and-normalization.md) (He init, BatchNorm), [Module 09](../09-practical-training-and-debugging.md) (the pipeline and the debugging discipline), [Module 10](../10-convolutional-networks.md) (convolution, residual blocks) and [Module 13](../13-transfer-learning-and-embeddings.md) (the fine-tuned baseline you will compare against).

Budget a few hours, most of it waiting. Nothing here needs a GPU, but a GPU makes it pleasant rather than tedious — see the note on runtime at the end.

## Part A — Questionnaire

These are harder than the per-module questions because they cut across modules. Answer them before you write any code; several of them are predictions you will then get to check.

1. You are about to train a network from scratch on 45,000 CIFAR-10 images. Before running anything, predict the initial training loss to two decimal places and justify the number. Then say what it would mean if you observed 4.1, and separately what it would mean if you observed 2.31 but it never moved.

2. Rank these six ingredients by how much test accuracy you expect each to contribute on this task, and commit to your ranking in writing: random-crop-and-flip augmentation, residual connections, BatchNorm, He initialization, a one-cycle learning-rate schedule, AdamW rather than plain SGD with momentum. For each one, state the *mechanism* by which you expect it to help — not "it regularizes" but which specific failure it prevents.

3. Your ranking in question 2 is for a 13-layer network trained for 20 epochs on 10,000 images. Name at least two entries whose position you would expect to change substantially if the network were 50 layers deep and trained for 200 epochs on the full dataset, and explain the direction of the change.

4. You will hold out 5,000 of the 50,000 training images as a validation set and never touch the 10,000-image test set until the very end. Explain precisely what goes wrong if you instead select your best checkpoint on the test set — not "it's cheating," but what quantity becomes biased and in which direction. Then explain why the validation set must use the *test-time* transform pipeline even though it is drawn from the training split.

5. Early stopping on validation *loss* and early stopping on validation *accuracy* frequently disagree, and in [Set 07](./07-exercises.md) you measured a case where they disagreed by 33 epochs. Explain the mechanism that makes them diverge, and say which one you should select on if your deployed metric is accuracy.

6. In [Module 13](../13-transfer-learning-and-embeddings.md) a fine-tuned ImageNet ResNet-18 reached 89.40% on a CIFAR-10 subset after three epochs and fourteen seconds. Your from-scratch model will take orders of magnitude longer to reach a comparable number. Given that, construct the strongest honest argument for ever training from scratch — and then state the conditions under which that argument fails.

## Part B — The project

### B1. Build the model

**The goal, in prose.** Write a small ResNet for 32×32 inputs, from scratch, with no `torchvision.models`. It should be a stem convolution, three stages of two residual blocks each with the channel count doubling and the spatial resolution halving between stages, global average pooling, and a linear classifier. Every design decision in it should be one you can defend from a specific module of this book.

**Specifics.** Use a 3×3 stem at stride 1 producing 64 channels — *not* the 7×7 stride-2 stem plus max-pool that ImageNet ResNets use, because on a 32×32 input that throws away three quarters of your resolution before the network has computed anything. This is the single most common mistake when porting an ImageNet architecture to CIFAR, and the original CIFAR ResNets in He et al. avoid it for exactly this reason.

Each residual block is two 3×3 convolutions, each followed by BatchNorm, with a ReLU after the first and after the addition. When a block changes shape — either stride or channel count — the shortcut needs a 1×1 convolution with matching stride and its own BatchNorm; when it does not, the shortcut is the identity, and it should be *literally* the identity, with no parameters. Set `bias=False` on every convolution that is followed by BatchNorm, and be ready to explain why (Module 08 gives the answer in one sentence).

Initialize every convolution with He normal in `fan_out` mode. Use stage widths of 64, 128, 256 and confirm your parameter count lands near 2.78 million.

### B2. Build the pipeline

**The goal, in prose.** Get the data handling right, because it is where the silent bugs live. Two rules govern everything: augmentation applies to training data only, and the split into train, validation and test must be made once and honoured absolutely.

**Specifics.** Split the 50,000-image CIFAR-10 training set into 45,000 for training and 5,000 for validation, using a fixed seed so the split is reproducible. The training pipeline is `RandomCrop(32, padding=4)`, then `RandomHorizontalFlip()`, then `ToTensor()`, then `Normalize()` with the CIFAR-10 channel statistics `mean = (0.4914, 0.4822, 0.4465)` and `std = (0.2470, 0.2435, 0.2616)`. The validation and test pipelines are `ToTensor()` and `Normalize()` only.

Here is the trap, and it catches nearly everyone: if you build one `Dataset` with the training transform and then `random_split` it, your validation set is augmented, which makes validation loss noisy and pessimistic and makes early stopping unreliable. Construct **two** dataset objects over the same underlying files with different transforms, and index into them with the same fixed permutation. Random crops and horizontal flips are not label-preserving in the sense that matters here — they are label-preserving but *distribution-shifting*, and your validation set must match the test distribution, not the training one.

### B3. Train it

**The goal, in prose.** Assemble the full recipe from Module 09 and run it, with the debugging checks *before* the long run rather than after it.

**Specifics.** Before training for real, do the two sanity checks: confirm the initial loss is near $\ln 10 = 2.3026$, and overfit a single batch to near-zero loss. If either fails, stop and fix it — you now know from [Set 09](./09-exercises.md) exactly what each failure mode looks like.

Then train with AdamW at a maximum learning rate of $10^{-3}$ with weight decay $5\times10^{-4}$, a `OneCycleLR` schedule stepped per batch, batch size 128, gradient clipping at global norm 1.0, and for at least 15 epochs. Evaluate on the validation set after every epoch, keep a deep copy of the state dict whenever validation loss improves, and restore that best checkpoint before touching the test set. Touch the test set exactly once.

Report final test accuracy, parameter count and wall-clock time.

### B4. Ablate it — the part that matters

**The goal, in prose.** You have a working model and a set of beliefs about why it works. Now falsify them. Take the working configuration as a baseline and remove exactly one ingredient at a time, holding everything else — seed, epochs, data, everything — fixed, and measure what each removal costs.

**Specifics.** To keep this tractable, shrink the problem: 10,000 training images, stage widths of 32/64/128, and 20 epochs per configuration. That is small enough that seven runs finish in an evening and large enough that the effects are real rather than noise. Keep the same 5,000-image validation set and the same test set so the numbers stay comparable.

Run these seven configurations:

| configuration | what changes |
| --- | --- |
| baseline | everything on |
| − augmentation | no random crop, no horizontal flip |
| − residual connections | blocks compute $\phi(f(x))$ instead of $\phi(f(x) + x)$ |
| − BatchNorm | all `BatchNorm2d` replaced by `Identity`, convolution biases restored |
| − He init | PyTorch's default `nn.Conv2d` initialization instead |
| − LR schedule | constant learning rate at $10^{-3}$ |
| SGD instead of AdamW | SGD with momentum 0.9 at learning rate 0.05, same weight decay |

For each, report best validation loss and final test accuracy. Then — and this is the actual exercise — write a paragraph for each row explaining the number you got, and compare it against the ranking you committed to in question 2. Where you were wrong, work out why. At least two of these results are likely to surprise you, and the surprises are worth more than the confirmations.

Two methodological warnings. First, this is a single seed per configuration, which means differences of a point or less are noise and you should not narrate them; only the large gaps are trustworthy. Second, and more subtly, an ablation at one scale does not transfer to another. A result about a 13-layer network trained for 20 epochs is a result about *that*, and question 3 asked you to predict which entries would move at 50 layers and 200 epochs. Hold onto your answer and check it against the solution.

### B5. Situate the result

**The goal, in prose.** Compare your from-scratch model against the transfer-learning baseline from Module 13, and draw the honest conclusion.

**Specifics.** In Module 13, a fine-tuned ImageNet-pretrained ResNet-18 reached 89.40% on 5,000 CIFAR-10 training images in 13.6 seconds. Your from-scratch model used nine times more data and several orders of magnitude more compute. Compare the two on accuracy, on data, and on wall-clock time, and write the conclusion out explicitly — including the conditions under which the from-scratch path is nonetheless the right one.

## Starter stub

```python
import torch, torch.nn as nn, torch.nn.functional as F, time, copy
import torchvision as tv
from torchvision import transforms as T
from torch.utils.data import DataLoader, Subset

MEAN, STD = (0.4914, 0.4822, 0.4465), (0.2470, 0.2435, 0.2616)

class Block(nn.Module):
    def __init__(self, cin, cout, stride=1, residual=True, norm=True):
        super().__init__()
        self.residual = residual
        N = (lambda c: nn.BatchNorm2d(c)) if norm else (lambda c: nn.Identity())
        # two 3x3 convs, each followed by norm; bias=False when norm follows
        ...
        # shortcut: identity when shape is unchanged, else 1x1 conv + norm
        self.short = ...

    def forward(self, x):
        ...

class ResNet(nn.Module):
    def __init__(self, residual=True, norm=True, widths=(64, 128, 256)):
        super().__init__()
        # 3x3 stride-1 stem -- NOT the 7x7 stride-2 ImageNet stem
        ...

    def forward(self, x):
        ...

def loaders(aug=True, n_train=45000, batch=128):
    """Two dataset objects over the same files, different transforms,
    indexed by the same fixed permutation."""
    ...

def run(tag, epochs, aug=True, residual=True, norm=True,
        opt="adamw", lr=1e-3, sched=True, he=True):
    """One configuration end to end. Returns test accuracy."""
    torch.manual_seed(0)
    ...
```

---

When you have finished, the [solution](./solutions/14-capstone-solution.md) has the complete working code, the measured numbers from both the full run and all seven ablation configurations, and a discussion of the two results that are genuinely counterintuitive.

Solutions: [Capstone solution](./solutions/14-capstone-solution.md) · Back to [Set 13](./13-exercises.md) · Index: [How to use](./00-HOW-TO-USE.md)
