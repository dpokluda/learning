# 00 — Start here

There are two kinds of neural network education available to you right now, and they fail in opposite directions. The first is the tutorial: forty lines of PyTorch, a downloaded dataset, a number that goes up, and a vague sense that something magical happened in the middle. The second is the textbook: rigorous, complete, and written for someone who intends to publish. This book is trying to be the thing in between — the one that explains *why* every piece exists, derives the mathematics honestly rather than waving at it, and then shows you the code that implements exactly what was just derived, so the symbols and the tensors are visibly the same object.

The organizing belief is that deep learning is much smaller than it looks. There are perhaps six genuinely load-bearing ideas in the entire field. A network is a stack of differentiable functions. Training is minimizing a loss by following its gradient. The gradient of a composition is computed by the chain rule applied backwards, which is cheap. The loss usually comes from maximum likelihood, which is why cross-entropy shows up everywhere. Generalization is a bias-variance bargain you negotiate with regularization. And architecture is the art of building the right assumptions — locality, recurrence, attention — into the function class so the optimizer has less to discover. Everything else, and there is a great deal of everything else, is engineering refinement on top of those six.

## Who this book assumes you are

You can program comfortably and you are not intimidated by reading code or a mathematical expression. You have seen calculus and linear algebra at some point, even if the details have faded — Module 02 rebuilds precisely the parts you need and nothing more. You may have already trained a small classifier and watched a loss curve descend without being certain what was happening underneath. That is the ideal starting position, because this book's job is to replace "it worked" with "of course it worked, and here is what would have broken it."

You do not need a GPU. Every exercise in the companion is sized to run on a free Colab instance or a laptop CPU in minutes, not hours.

## How the book is arranged

The modules build strictly on one another, and each opens by naming the single module it depends on. Reading in order is strongly recommended, because later modules reuse the exact notation, the running example, and the mental models established earlier.

| # | Module | What you'll be able to do afterwards |
|---|--------|--------------------------------------|
| [01](./01-what-is-a-neural-network.md) | What a neural network is | Explain a network as a parameterized function, trace the field's history, and say precisely why it works now and not in 1990 |
| [02](./02-mathematical-foundations.md) | Just enough mathematics | Read and write the matrix, gradient, and probability notation the rest of the book uses without hesitation |
| [03](./03-feedforward-networks-and-activations.md) | Feedforward networks and activations | Design an MLP, choose an activation for principled reasons, and state what universal approximation does and does not promise |
| [04](./04-loss-functions-and-the-probabilistic-view.md) | Loss functions and the probabilistic view | Derive cross-entropy and MSE from maximum likelihood, and pick the right loss for a new problem by choosing a distribution |
| [05](./05-backpropagation-and-autodiff.md) | Backpropagation and autodiff | Derive backprop by hand, implement a working autograd engine, and gradient-check any layer you write |
| [06](./06-optimization.md) | Optimization | Explain what momentum, RMSProp, and Adam each fix, and choose a learning rate and schedule deliberately |
| [07](./07-generalization-and-regularization.md) | Generalization and regularization | Diagnose overfitting from curves and apply weight decay, dropout, early stopping, and augmentation for the right reasons |
| [08](./08-initialization-and-normalization.md) | Initialization and normalization | Explain vanishing/exploding gradients quantitatively, derive Xavier/He init, and say what BatchNorm and LayerNorm actually do |
| [09](./09-practical-training-and-debugging.md) | Practical training and debugging | Build a correct training pipeline and systematically debug a model that won't learn |
| [10](./10-convolutional-networks.md) | Convolutional networks | Compute conv output shapes and parameter counts by hand, and explain the LeNet → AlexNet → VGG → ResNet progression as a sequence of specific fixes |
| [11](./11-sequence-models.md) | Sequence models | Explain RNN weight sharing, why LSTM gates preserve gradient, and exactly where seq2seq hits its bottleneck |
| [12](./12-attention-and-transformers.md) | Attention and Transformers | Implement scaled dot-product and multi-head attention from scratch and explain every component of a Transformer block |
| [13](./13-transfer-learning-and-embeddings.md) | Transfer learning and embeddings | Fine-tune a pretrained model correctly and reason about embeddings as a reusable representation |
| [14](./14-modern-landscape.md) | The modern landscape | Place self-supervised learning, generative models, scaling laws, and LLMs on the map you've built |
| [15](./15-reference.md) | Reference | Look up any term, formula, or source without rereading a chapter |
| [capstone](./exercises/14-capstone.md) | Build and ablate a ResNet | Assemble the whole book into one working CIFAR-10 model, then measure what each ingredient actually contributed |

The [exercise companion](./exercises/00-HOW-TO-USE.md) runs alongside, one chapter per teaching module, with a written questionnaire and a PyTorch coding task each. Solutions live in a [separate folder](./exercises/solutions/) so you can genuinely test yourself before peeking. [`SETUP.md`](./SETUP.md) gets you running in Colab or locally in about five minutes.

## If you only have thirty minutes

Read [Module 05 on backpropagation](./05-backpropagation-and-autodiff.md), because it is the one idea whose absence makes everything else feel like superstition. Then read the first half of [Module 04](./04-loss-functions-and-the-probabilistic-view.md) to see where the loss function comes from, and the ResNet section of [Module 10](./10-convolutional-networks.md) for the single most instructive architectural fix in the field's history. Those three sittings will not make you a practitioner, but they will make the rest of the literature legible.

## The running example

A thread runs through every module: a classifier for 28×28 grayscale images of handwritten digits — MNIST, 60,000 training images and 10,000 test images, ten classes. It is deliberately a solved problem. That is the point: because the answer is known, every change you make is interpretable, and we can keep returning to the same concrete object as the theory gets deeper. In Module 01 it is a single matrix multiply that gets about 92% accuracy. By Module 05 you will have hand-derived the gradients that train it. By Module 10 it becomes a convolutional network at 99%, and the same reasoning carries to CIFAR-10. From Module 11 onward the thread shifts to text — IMDB sentiment and small sequence tasks — because sequences are where recurrence and attention earn their existence. When a general principle appears, it lands on that concrete object first.

## What to trust, and how much

Not all sources deserve equal weight, and part of becoming competent in this field is developing that discrimination. This book cites in three tiers, and labels them where it matters.

**Primary sources** — the original papers and official specifications — are the strongest. When this book claims ResNet works because of identity shortcuts, the citation is He et al. 2015 itself, not a summary of it. Papers are the ground truth for what was actually claimed and measured, and they are usually more readable than their reputation suggests.

**Canonical textbooks and university course material** — *Deep Learning* by Goodfellow, Bengio and Courville; *Dive into Deep Learning*; the Stanford CS231n and CS224n notes — are the strongest source for synthesis, standard notation, and the derivations nobody re-derives in papers. They occasionally lag the frontier by a few years, which matters for Modules 12 and 14 and almost nowhere else.

**Official library documentation** — the PyTorch docs — is authoritative for behavior, and it is the only reliable source for what a function actually does today. API details drift between versions, so when this book and the docs disagree, the docs win.

**Practitioner writing** — engineering blogs, maintainer writeups — appears here only to illustrate or motivate, never as the sole support for a claim, and it is flagged as such. Where the field genuinely disagrees, and it does on several points in Modules 08 and 14, you will be told that it disagrees rather than handed a false consensus.

Every module ends with a **Sources** section listing exactly what it drew from, with links you can open.

## A note on how to read this

Type the code rather than copying it. Do the exercises before reading the solutions; the failure is where the learning is. When a derivation appears, work through it with a pen — you will not absorb backpropagation by reading it any more than you would absorb swimming by reading it. And when something does not click, the fastest fix is almost always to go back one module rather than forward one, because in this subject confusion is nearly always caused by a missing prerequisite rather than a difficult present idea.

Start with [Module 01](./01-what-is-a-neural-network.md).
