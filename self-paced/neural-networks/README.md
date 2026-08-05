# Neural Networks — A Self-Paced Study Book

A complete, self-contained course on neural networks, from the perceptron to the Transformer. Sixteen narrative modules plus a PyTorch exercise companion with full worked solutions.

This is written as a **book**, not a reference: connected prose that explains why each piece exists, honest derivations of the mathematics, and code that implements exactly what was just derived. Every quantitative claim in the text was produced by running the experiment, and the scripts are included.

**Start reading:** [`00-START-HERE.md`](./00-START-HERE.md) · **Get set up:** [`SETUP.md`](./SETUP.md)

---

## What you will be able to do

By the end of this book you should be able to explain a neural network as a parameterized differentiable function and say precisely what training does to it; derive backpropagation from the chain rule and implement a working automatic-differentiation engine from scratch; derive the standard loss functions from maximum likelihood rather than accepting them as conventions; explain what momentum, RMSProp and Adam each fix and choose a learning-rate schedule deliberately; diagnose overfitting, underfitting and outright bugs from the training curves and fix them systematically; explain vanishing and exploding gradients quantitatively and say what initialization schemes and normalization layers actually do about them; compute convolution shapes and parameter counts by hand and explain the LeNet → AlexNet → VGG → ResNet progression as a sequence of specific fixes to specific problems; explain why recurrence struggles with long dependencies and how gating addresses it; implement multi-head attention from scratch and account for every component of a Transformer block; fine-tune a pretrained model correctly and reason about embeddings as a reusable representation; and read a modern paper's architecture section without hand-waving.

## Prerequisites

You need to be comfortable programming and unintimidated by reading code. Python specifically is assumed for the exercises, but if you are fluent in another language you will keep up; no advanced Python features are used.

You need to have seen calculus and linear algebra at some point, even if the details have gone. [Module 02](./02-mathematical-foundations.md) rebuilds exactly the parts required — matrix-vector products, partial derivatives, the chain rule, the gradient, and basic probability — and deliberately nothing more.

You do **not** need a GPU. Every exercise is sized to run on a free Google Colab instance or a laptop CPU in minutes. You do not need prior machine learning experience, though if you have trained a classifier before and were not quite sure what was happening underneath, that is the ideal starting position.

## Module index

### Estimated time

The estimates below assume active reading — typing the code, working derivations with a pen, and doing each module's exercise set. Halve them if you only read.

| # | Module | Focus | Time |
|---|--------|-------|------|
| — | [Start here](./00-START-HERE.md) | Orientation, how to read the book, source tiers | 15 min |
| — | [Setup](./SETUP.md) | Colab and local installation, exact package list | 30 min |
| 01 | [What a neural network is](./01-what-is-a-neural-network.md) | Perceptron, the historical arc, why deep learning works now | 2.5 h |
| 02 | [Just enough mathematics](./02-mathematical-foundations.md) | Linear algebra, calculus, probability — applied, and the notation used throughout | 3 h |
| 03 | [Feedforward networks and activations](./03-feedforward-networks-and-activations.md) | MLPs, activation functions, universal approximation | 2.5 h |
| 04 | [Loss functions and the probabilistic view](./04-loss-functions-and-the-probabilistic-view.md) | MLE, cross-entropy, MSE, numerical stability | 3 h |
| 05 | [Backpropagation and autodiff](./05-backpropagation-and-autodiff.md) | Derived by hand, then a working autograd engine in 60 lines | 5 h |
| 06 | [Optimization](./06-optimization.md) | SGD, momentum, RMSProp, Adam, learning-rate schedules | 3.5 h |
| 07 | [Generalization and regularization](./07-generalization-and-regularization.md) | Overfitting, L2, dropout, early stopping, augmentation, double descent | 3.5 h |
| 08 | [Initialization and normalization](./08-initialization-and-normalization.md) | Xavier/He, BatchNorm, LayerNorm, vanishing/exploding gradients | 3.5 h |
| 09 | [Practical training and debugging](./09-practical-training-and-debugging.md) | Data pipelines, hyperparameter tuning, a model that won't learn | 3.5 h |
| 10 | [Convolutional networks](./10-convolutional-networks.md) | Convolution, pooling, LeNet → AlexNet → VGG → ResNet | 5 h |
| 11 | [Sequence models](./11-sequence-models.md) | RNN, BPTT, LSTM/GRU, seq2seq and its bottleneck | 4.5 h |
| 12 | [Attention and Transformers](./12-attention-and-transformers.md) | Self-attention, multi-head, positional encoding, the full block | 5 h |
| 13 | [Transfer learning and embeddings](./13-transfer-learning-and-embeddings.md) | Fine-tuning, linear probing, LoRA, embeddings | 3.5 h |
| 14 | [The modern landscape](./14-modern-landscape.md) | Self-supervised learning, generative models, scaling laws, LLMs | 1.5 h |
| 15 | [Reference](./15-reference.md) | Glossary, formula sheet, debugging table, full bibliography | — |
| ★ | [Capstone](./exercises/14-capstone.md) | Build a ResNet on CIFAR-10 from scratch, then ablate it and measure what each ingredient was worth | 8 h |

**Total: roughly 55–60 hours** of active study. At five to six focused hours a week, that is a ten-to-twelve-week course; at an hour a night, it lands around two months.

## The exercise companion

Every teaching module has a matching exercise set in [`exercises/`](./exercises/), with complete worked solutions in [`exercises/solutions/`](./exercises/solutions/). Read [`exercises/00-HOW-TO-USE.md`](./exercises/00-HOW-TO-USE.md) before starting.

Each set has two parts. **Part A** is a written questionnaire of about six conceptual questions, to be answered from memory with the module closed — this is the part most people skip and the part that most reliably reveals what you only think you understood. **Part B** is a PyTorch coding task that states its goal in prose first, then gives concrete specifics and a starter stub. The tasks escalate: early ones have you implement mechanisms from scratch and verify them against PyTorch's own implementation to floating-point precision, later ones have you train real models, and the [capstone](./exercises/14-capstone.md) ties the whole book into one project.

The intended loop is: answer Part A from memory, then do Part B in your editor or a Colab notebook, then check both against the solutions, and reread the module section if you fumbled either. Solutions are in a separate folder specifically so that peeking requires a deliberate act.

Everything runs in Colab or locally on CPU. See [`SETUP.md`](./SETUP.md) for the exact package list and a half-hour setup.

## Suggested pacing

The book is about 200 pages of prose plus roughly 55–60 hours of active work with the exercises. Three sensible schedules:

**Thorough (10–12 weeks, ~5–6 hours/week).** One ordinary module with its exercise set fits a week of evening work; pair the short orientation, setup and survey pieces with neighboring weeks. Modules 05, 10 and 12, plus the capstone, deserve two sessions each — backpropagation, ResNet and attention are the three places where slowing down pays the most. This is the schedule the book is designed for.

**Intensive (2–3 weeks, ~20–30 hours/week).** Two shorter modules or one hard module a day, exercises for every one. Do not skip Part A; under time pressure it is the first thing to go and the thing whose absence you will feel later.

**Survey (a long weekend, ~12–15 hours).** Read [Module 01](./01-what-is-a-neural-network.md), [04](./04-loss-functions-and-the-probabilistic-view.md), [05](./05-backpropagation-and-autodiff.md), [10](./10-convolutional-networks.md) and [12](./12-attention-and-transformers.md), and do the exercises for 05 and 12. You will not be a practitioner, but the literature will be legible.

Whichever you pick, the modules build strictly on one another and each names the single module it depends on. Read in order; when something does not click, the fix is almost always to go back one module rather than forward.

## About the numbers in this book

Every measured result quoted in the text — accuracies, gradient magnitudes, parameter counts, timings — was produced by running the experiment on the machine this book was written on, and each is footnoted with the exact setup. The scripts are reproduced in the solutions files.

Several of them came out differently from the standard story. An LSTM failed a long-dependency task that a plain RNN solved, until its forget-gate bias was initialized correctly. A residual-network demonstration did not reproduce the published effect until the block's internal structure was right. Regularization improved calibration far more than it improved accuracy. Those results are reported as they came out, with the failed first attempts described rather than quietly dropped, because the habit of checking the claim is the most transferable thing this book has to teach.

## Sources

The book is grounded in public, authoritative sources and cites them reachably: the original papers (Rumelhart et al. on backpropagation, LeCun's LeNet, Krizhevsky's AlexNet, He et al. on ResNet, Ioffe and Szegedy on BatchNorm, Srivastava et al. on dropout, Kingma and Ba on Adam, Vaswani et al. on the Transformer, and many more), the canonical textbooks ([*Deep Learning*](https://www.deeplearningbook.org/) and [*Dive into Deep Learning*](https://d2l.ai/), both free online), the Stanford [CS231n](https://cs231n.github.io/) and [CS224n](https://web.stanford.edu/class/cs224n/) course notes, and the official [PyTorch documentation](https://pytorch.org/docs/stable/index.html). Each module ends with a `Sources` section of footnotes and a further-reading paragraph; [Module 15](./15-reference.md) consolidates the full bibliography.

Where the field genuinely disagrees — on why BatchNorm works, on whether Adam generalizes worse than SGD, on whether attention weights are explanations, on emergent abilities — the book says so and points at both sides rather than manufacturing a consensus.

## License

Prose and code in this directory are provided under the repository's [LICENSE](../../LICENSE). All cited works belong to their respective authors; links are provided so you can read the originals.
