# How to use the exercise companion

The modules explain. These exercises are where you find out whether the explanation took.

There is a specific loop that makes this work, and it is worth following even though two of its four steps feel unnecessary at the time.

**First, answer Part A from memory, with the module closed.** Each set opens with about six conceptual questions. They are not recall trivia — each targets a place where a plausible-sounding wrong understanding is common, and the point is to catch yourself holding one. Write the answers down, in sentences, before looking anything up. Writing is the part that matters: the difference between "I know this" and a paragraph you can actually produce is exactly the gap this step exists to expose. It takes ten or fifteen minutes and it is the step people skip.

**Second, do Part B in your editor or a Colab notebook.** Each coding task states its goal in prose first, so you can attempt it without the scaffolding, then gives concrete specifics and a starter stub if you want them. Try the prose version first. Where a task asks you to implement something PyTorch already provides — an optimizer, an attention layer, an LSTM cell — the exercise ends by checking your version against the library's to floating-point precision, which is an unusually satisfying form of feedback: either the difference is around $10^{-16}$ or you have a real bug, with no ambiguity in between.

**Third, check both against the solutions.** They are in [`solutions/`](./solutions/), one file per set, containing worked answers to every Part A question and complete runnable code for every Part B. Separate folder, deliberately, so that peeking takes an act of will.

**Fourth, and this is the one that compounds: if you fumbled either part, go back and reread that section of the module before moving on.** The modules build strictly, and a shaky Module 05 makes Module 12 feel like magic rather than mechanism.

## Setup

Everything here runs in [Google Colab](https://colab.research.google.com/) with no installation, or locally with a handful of packages. [`../SETUP.md`](../SETUP.md) covers both paths, gives the exact package list, and includes the `get_device()` helper and the seeding boilerplate that every solution uses. Do that first; it takes about five minutes.

Nothing requires a GPU. Every exercise is sized to finish in minutes on a laptop CPU, and the few that benefit from acceleration say so and stay small enough to run without it.

## The sets

| set | module | Part B in one line |
| --- | --- | --- |
| [01](./01-exercises.md) | [What a neural network is](../01-what-is-a-neural-network.md) | Reproduce the linear-vs-MLP MNIST comparison and find where linearity fails |
| [02](./02-exercises.md) | [Mathematics](../02-mathematical-foundations.md) | Tensor shape and broadcasting drills; compute a gradient by hand and confirm it |
| [03](./03-exercises.md) | [Feedforward networks](../03-feedforward-networks-and-activations.md) | Build the XOR network by hand, then measure activation functions against each other |
| [04](./04-exercises.md) | [Loss functions](../04-loss-functions-and-the-probabilistic-view.md) | Implement numerically stable cross-entropy and break the naive version |
| [05](./05-exercises.md) | [Backpropagation](../05-backpropagation-and-autodiff.md) | Write a scalar autograd engine from scratch and train an MLP with it |
| [06](./06-exercises.md) | [Optimization](../06-optimization.md) | Implement SGD+momentum and Adam from the equations; match `torch.optim` exactly |
| [07](./07-exercises.md) | [Generalization](../07-generalization-and-regularization.md) | Reproduce the random-label memorization result and measure regularizers |
| [08](./08-exercises.md) | [Initialization and normalization](../08-initialization-and-normalization.md) | Probe activation variance through 50 layers; implement BatchNorm and LayerNorm |
| [09](./09-exercises.md) | [Practical training](../09-practical-training-and-debugging.md) | Fix five deliberately broken training scripts |
| [10](./10-exercises.md) | [Convolutional networks](../10-convolutional-networks.md) | Implement conv2d by hand, then build a CNN to 99%+ on MNIST |
| [11](./11-exercises.md) | [Sequence models](../11-sequence-models.md) | Build an LSTM cell from scratch and reproduce the forget-bias result |
| [12](./12-exercises.md) | [Attention and Transformers](../12-attention-and-transformers.md) | Implement multi-head attention; train a character-level GPT |
| [13](./13-exercises.md) | [Transfer learning](../13-transfer-learning-and-embeddings.md) | Compare from-scratch, linear probe and fine-tuning on the same budget |
| [capstone](./14-capstone.md) | everything | One project, end to end, using the whole book |

Sets 05, 10 and 12 are the heavy ones and are worth a full sitting each. The capstone is a small project rather than an exercise and takes a few hours.

## A note on the code

The reference solutions are written for clarity over cleverness, and they all follow the same shape as the pipeline in [Module 09](../09-practical-training-and-debugging.md) so that the differences between exercises are the interesting parts rather than boilerplate. Every one of them was executed before being included; where a solution prints numbers, those are the numbers it printed. Your values will differ slightly with a different seed, device or library version, and the solution says which digits to trust.

Type the code rather than pasting it. It is slower and it works better.
