# 02 — Just enough mathematics

There is a tax to be paid before deep learning becomes legible, and it is smaller than most people fear. You need three things: enough linear algebra to keep track of shapes and know what a matrix multiplication is doing geometrically, enough calculus to understand what a gradient is and how the chain rule composes, and enough probability to see why the loss functions of Module 04 take the forms they do. You do not need measure theory, you do not need to prove anything, and you do not need to remember how to invert a matrix by hand.

This module is deliberately applied. Every concept is introduced in the context of the running MNIST classifier, and every piece of notation established here is used unchanged for the remaining thirteen modules. If you already know this material, skim — but do read the section on the transpose convention, because the gap between how textbooks write a linear layer and how PyTorch stores one is responsible for more confusion than any other single thing in this subject.

> **Prerequisite:** [Module 01](./01-what-is-a-neural-network.md) — you should be comfortable with the idea of a network as a parameterized function $f(\mathbf{x};\theta)$.

## Vectors, and what a dot product means

A vector is an ordered list of numbers, and in this book it is written in bold lowercase: $\mathbf{x} \in \mathbb{R}^{784}$ means $\mathbf{x}$ is a list of 784 real numbers. For the MNIST image that is the pixel intensities, read row by row into one long list. Nothing more mystical than an array.

Two operations matter. Addition and scalar multiplication work elementwise and behave exactly as you would guess. The dot product is the one that carries meaning:

$$\mathbf{w} \cdot \mathbf{x} = \sum_{i=1}^{d} w_i x_i$$

Multiply corresponding entries, add them all up, get a single number. The reason this operation is everywhere is its geometric identity, $\mathbf{w}\cdot\mathbf{x} = \|\mathbf{w}\|\,\|\mathbf{x}\|\cos\vartheta$, where $\|\cdot\|$ is Euclidean length and $\vartheta$ is the angle between the two vectors. Read that as a statement about *agreement*: the dot product is large and positive when the vectors point in similar directions, near zero when they are perpendicular, and negative when they oppose. It is a similarity score.

This is what a single row of the MNIST weight matrix is doing. Row 3 of $W$ is a 784-dimensional vector — an image, in effect — and dotting it with an input asks "how much does this image look like my template for a 3?" Every score a neural network ever produces, at every layer, is fundamentally that question asked with learned templates. Hold onto this, because in Module 12 the *entire mechanism of attention* turns out to be dot products used as similarity scores, and it will feel inevitable rather than arbitrary if you have this reading in place.

The Euclidean norm $\|\mathbf{x}\| = \sqrt{\sum_i x_i^2} = \sqrt{\mathbf{x}\cdot\mathbf{x}}$ measures length, and its square $\|\mathbf{x}\|^2$ shows up constantly because it avoids the square root and differentiates cleanly. When Module 07 penalizes large weights, the penalty is $\|\theta\|^2$; when Module 04 measures regression error, it is $\|\hat{\mathbf{y}} - \mathbf{y}\|^2$. Same object.

## Matrices, shapes, and the transpose convention

A matrix is a rectangular grid, written in capitals: $W \in \mathbb{R}^{m \times n}$ has $m$ rows and $n$ columns. Matrix–vector multiplication $\mathbf{z} = W\mathbf{x}$ requires $\mathbf{x} \in \mathbb{R}^n$ and produces $\mathbf{z} \in \mathbb{R}^m$, where

$$z_i = \sum_{j=1}^{n} W_{ij}x_j$$

which is exactly "$z_i$ is the dot product of row $i$ of $W$ with $\mathbf{x}$." So a matrix–vector product is $m$ dot products stacked, or equivalently, $m$ similarity questions asked at once. A linear layer with 128 outputs asks 128 learned questions about its input in a single operation, and the reason this is fast is that hardware is very good at exactly this.

The discipline that saves you the most debugging time is tracking shapes. Write them down. $W$ is $(m \times n)$ and $\mathbf{x}$ is $(n)$, the inner dimensions match, the result is $(m)$. When PyTorch raises `mat1 and mat2 shapes cannot be multiplied`, this is the arithmetic it is complaining about, and the fix is always to work out what shape you actually have versus what shape the operation wants.

Now the convention issue. In mathematical writing — this book included, and *Deep Learning* and d2l and every paper — a single example is a column vector and a linear layer is

$$\mathbf{z} = W\mathbf{x} + \mathbf{b}, \qquad W \in \mathbb{R}^{\text{out} \times \text{in}}$$

In PyTorch, data is *batch-first* and examples are **rows**. A batch of 64 MNIST images is a tensor of shape `(64, 784)`, not `(784, 64)`. To multiply a batch of row-vectors by the same weight matrix, you need

$$Z = XW^\top + \mathbf{b}, \qquad X \in \mathbb{R}^{B \times \text{in}},\; W \in \mathbb{R}^{\text{out} \times \text{in}},\; Z \in \mathbb{R}^{B \times \text{out}}$$

Note carefully what is and is not different. The *stored weight matrix is the same shape in both conventions* — `nn.Linear(784, 128).weight` really is `(128, 784)`, output-by-input, matching the math. What differs is only that the batch dimension comes first, which forces the transpose in the code. PyTorch's `nn.Linear` documents its operation as $y = xA^\top + b$ for exactly this reason.[^m2-linear] So when you read $W\mathbf{x}$ in a paper and see `x @ W.T` in code, they are the same equation seen from two seating positions, and the moment that stops surprising you, a whole category of confusion disappears.

Verify it rather than trusting it:

```python
import torch, torch.nn as nn

layer = nn.Linear(784, 128)
print(layer.weight.shape)        # torch.Size([128, 784])  -> (out, in), matches the math
print(layer.bias.shape)          # torch.Size([128])

x = torch.randn(64, 784)         # batch of 64 examples, examples are ROWS
z = layer(x)
print(z.shape)                   # torch.Size([64, 128])

manual = x @ layer.weight.T + layer.bias
print(torch.allclose(z, manual))  # True
```

That last line is the whole point: `nn.Linear` is literally `x @ W.T + b`, and you can now read either notation without translation effort.

One more mechanism to name, because it silently appears in that snippet. Adding a `(128,)` bias to a `(64, 128)` matrix is not a shape-legal operation in strict linear algebra, but NumPy and PyTorch **broadcast** it: the smaller tensor is conceptually replicated along the missing dimension. The rule is that dimensions are aligned from the right and must either match or be 1. Broadcasting is enormously convenient and is also a reliable source of silent bugs — a `(64, 1)` tensor and a `(1, 64)` tensor broadcast against each other into a `(64, 64)` tensor, which is almost never what you meant and which will not raise an error. Module 09 returns to this as a debugging pattern.

Matrix–matrix multiplication generalizes the same rule: $(m \times k)$ times $(k \times n)$ gives $(m \times n)$, inner dimensions must agree. It is associative but **not commutative** — $AB \neq BA$ in general — and the transpose reverses order, $(AB)^\top = B^\top A^\top$, an identity you will need in the backpropagation derivation of Module 05.

## Derivatives and gradients

Training is minimization, and minimization needs derivatives. For a scalar function $f: \mathbb{R} \to \mathbb{R}$, the derivative $f'(x)$ is the instantaneous rate of change — the slope of the tangent line — and it tells you the local linear approximation $f(x + \epsilon) \approx f(x) + \epsilon f'(x)$ for small $\epsilon$. That approximation *is* the algorithm: if you want $f$ to decrease and $f'(x) > 0$, take a small step in the negative direction.

Neural network losses are functions of thousands to billions of parameters, so the object of interest is the **partial derivative** $\partial f/\partial x_i$ — the rate of change when you wiggle coordinate $i$ and hold everything else fixed — and the **gradient**, which collects them all:

$$\nabla_{\mathbf{x}} f = \left[\frac{\partial f}{\partial x_1}, \frac{\partial f}{\partial x_2}, \dots, \frac{\partial f}{\partial x_d}\right]$$

The gradient of a scalar function is a vector with one entry per input, and it has a property worth stating precisely because it justifies everything in Module 06: **the gradient points in the direction of steepest ascent**, and its negation points in the direction of steepest descent. To decrease a loss, move against its gradient. That is the entire optimization strategy of deep learning, refined but never replaced.

A worked example, small enough to check by hand. Let $f(x_1, x_2) = x_1^2 + 3x_1x_2$. Then $\partial f/\partial x_1 = 2x_1 + 3x_2$ and $\partial f/\partial x_2 = 3x_1$, so at the point $(1, 2)$ the gradient is $[2 + 6,\; 3] = [8, 3]$. Increasing $x_1$ raises $f$ about 8 units per unit of movement, increasing $x_2$ raises it about 3, and the fastest way down from here is the direction $[-8, -3]$. PyTorch agrees:

```python
import torch
x = torch.tensor([1.0, 2.0], requires_grad=True)
f = x[0]**2 + 3*x[0]*x[1]
f.backward()
print(x.grad)          # tensor([8., 3.])
```

Two more objects and the calculus is complete. When the output is a vector rather than a scalar, $\mathbf{f}: \mathbb{R}^n \to \mathbb{R}^m$, the derivative is the **Jacobian**, an $m \times n$ matrix with $J_{ij} = \partial f_i/\partial x_j$. Every layer of a network is such a function, so every layer has a Jacobian, and Module 05 is essentially the story of multiplying Jacobians together efficiently. And the matrix of second derivatives, the **Hessian** $H_{ij} = \partial^2 f/\partial x_i \partial x_j$, describes curvature — whether the loss surface is a narrow ravine or a gentle bowl. It is almost never computed explicitly in deep learning, since for a billion parameters it would have $10^{18}$ entries, but it is the right way to *think* about why plain gradient descent struggles in ill-conditioned landscapes, which is the motivation for momentum and Adam in Module 06.

## The chain rule, which is the whole game

A neural network is a composition of functions, and the chain rule is how you differentiate a composition. In one dimension, if $y = f(u)$ and $u = g(x)$, then

$$\frac{dy}{dx} = \frac{dy}{du}\cdot\frac{du}{dx}$$

Local rates of change multiply. If a 1% change in $x$ produces a 2% change in $u$, and a 1% change in $u$ produces a 3% change in $y$, then a 1% change in $x$ produces roughly a 6% change in $y$. This is intuitive and it is also, when iterated across fifty layers, the source of the vanishing and exploding gradient problems of Module 08 — multiply fifty numbers each slightly less than one and you get approximately zero.

The multivariable version is the one you need. If $y$ depends on several intermediates $u_1, \dots, u_k$, each of which depends on $x$, the contributions **sum over every path**:

$$\frac{\partial y}{\partial x} = \sum_{j=1}^{k} \frac{\partial y}{\partial u_j}\cdot\frac{\partial u_j}{\partial x}$$

Sum over paths, multiply along each path. That single sentence is backpropagation. Everything in Module 05 is bookkeeping to compute this sum efficiently on a graph with millions of nodes without recomputing shared subexpressions.

In vector form, if $\mathbf{y} = \mathbf{f}(\mathbf{u})$ and $\mathbf{u} = \mathbf{g}(\mathbf{x})$, the Jacobians compose by matrix multiplication, $J_{\mathbf{y}\mathbf{x}} = J_{\mathbf{y}\mathbf{u}}J_{\mathbf{u}\mathbf{x}}$. And here is the observation that makes deep learning computationally feasible at all. A loss is a *scalar*, so its Jacobian with respect to any intermediate is a single row. Multiplying a row vector by a matrix costs $O(mn)$; multiplying two matrices costs $O(mnk)$. By starting from the scalar loss and moving *backwards*, every step is a cheap vector–matrix product rather than an expensive matrix–matrix product. That asymmetry — cheap when outputs are few and inputs are many — is why reverse-mode automatic differentiation is the right algorithm for a function with one output and a billion inputs, and why the gradient of a network costs only a small constant multiple of the forward pass. Module 05 makes this precise.

A few derivatives are worth memorizing because they appear in nearly every derivation that follows:

| Function | Derivative | Where it shows up |
|---|---|---|
| $f(x) = x^2$ | $2x$ | MSE loss, L2 regularization |
| $f(x) = e^x$ | $e^x$ | softmax, exponential families |
| $f(x) = \ln x$ | $1/x$ | log-likelihood, cross-entropy |
| $\sigma(x) = \frac{1}{1+e^{-x}}$ | $\sigma(x)(1-\sigma(x))$ | sigmoid units, LSTM gates |
| $\tanh(x)$ | $1 - \tanh^2(x)$ | RNN hidden states |
| $\mathrm{ReLU}(x) = \max(0,x)$ | $1$ if $x>0$, else $0$ | essentially every modern network |
| $\mathbf{z} = W\mathbf{x}$ | $\partial \mathbf{z}/\partial\mathbf{x} = W$ | every linear layer |

The sigmoid derivative deserves a second look, because it explains a failure mode rather than just enabling a calculation. Since $\sigma$ outputs values in $(0,1)$, the product $\sigma(1-\sigma)$ is maximized at $\sigma = 0.5$ where it equals $0.25$, and it decays toward zero as the output saturates toward either extreme. So the *largest* gradient a sigmoid ever passes through is one quarter, and stacking ten of them multiplies the signal by at most $0.25^{10} \approx 10^{-6}$. That is the vanishing gradient problem in one line of arithmetic, and it is why the field abandoned sigmoid activations for hidden layers. Module 03 makes the case for ReLU on exactly these grounds.

## Probability, and where losses come from

The final ingredient is probability, and its role is not decorative. Almost every loss function in deep learning is a negative log-likelihood in disguise, and once you see that, choosing a loss for a new problem stops being a matter of taste and becomes a matter of choosing which distribution your output represents.

A **random variable** takes values with specified probabilities. Discrete ones have a probability mass function with $\sum_i p(x_i) = 1$; continuous ones have a density with $\int p(x)\,dx = 1$, where the density at a point can exceed 1 but the integral cannot. Three distributions carry nearly all the weight in this book. The **Bernoulli** describes a single binary outcome with $P(x=1) = p$, and it is what a binary classifier's sigmoid output parameterizes. The **categorical** generalizes it to $K$ mutually exclusive outcomes with probabilities $p_1,\dots,p_K$ summing to 1, and it is what a softmax output parameterizes — for MNIST, $K=10$. The **Gaussian**, $\mathcal{N}(\mu, \sigma^2)$ with density proportional to $\exp(-(x-\mu)^2/2\sigma^2)$, is the default for continuous outputs and, as Module 04 shows, is exactly why squared error is the standard regression loss.

The **expectation** $\mathbb{E}[X] = \sum_i x_i p(x_i)$ is the probability-weighted average, and it is the object minimized in training: the true objective is the expected loss over the data distribution, which you cannot compute because you do not have the distribution, so you approximate it by the average over your training set. That substitution is called empirical risk minimization, and the gap between the two — expected loss versus training-set loss — is precisely what Module 07 calls generalization error. Naming it now makes that module much easier.

**Conditional probability** $P(A \mid B) = P(A, B)/P(B)$ expresses probability given knowledge, and it is the correct way to describe a classifier's output. Your MNIST network does not output "3." It outputs $P(y = k \mid \mathbf{x})$ for each $k$ — a full conditional distribution over the ten digits given this image — and you collapse it to a decision with `argmax` only at the very end. This distinction matters practically: the distribution carries calibration and uncertainty information that the argmax discards, and Modules 04 and 09 both make use of it.

**Independence** lets a joint probability factor into a product, $P(A,B) = P(A)P(B)$, and combined with the assumption that training examples are drawn independently it gives the likelihood of a whole dataset as $\prod_i P(y_i \mid \mathbf{x}_i; \theta)$. **Maximum likelihood estimation** says: choose the $\theta$ that makes the observed data most probable. Because products of thousands of numbers below 1 underflow to zero in floating point, and because sums differentiate more pleasantly than products, you maximize the logarithm instead, which turns the product into a sum. Since optimizers conventionally minimize, you negate:

$$\theta^* = \arg\max_\theta \prod_i P(y_i \mid \mathbf{x}_i;\theta) = \arg\min_\theta \left(-\sum_i \log P(y_i \mid \mathbf{x}_i;\theta)\right)$$

The right-hand expression is the negative log-likelihood, and it is the origin of nearly every loss function you will meet. Module 04 is largely the work of turning that crank for specific choices of distribution.

Two information-theoretic quantities complete the toolkit. The **entropy** $H(p) = -\sum_i p_i \log p_i$ measures the average surprise of a distribution — maximal when everything is equally likely, zero when one outcome is certain. The **cross-entropy** $H(p,q) = -\sum_i p_i \log q_i$ measures the average surprise of using distribution $q$ when reality is $p$, and the **KL divergence** $D_{\mathrm{KL}}(p\|q) = H(p,q) - H(p)$ is the excess cost of that mismatch. KL is always non-negative and is zero exactly when $p = q$, which makes it a natural measure of how wrong a predicted distribution is. Since the true label distribution $p$ is fixed, $H(p)$ is a constant, so minimizing cross-entropy and minimizing KL divergence are the same optimization problem. That equivalence is why "cross-entropy loss" and "minimizing the divergence from the true distribution" and "maximum likelihood" are three descriptions of one procedure.

Here is the arithmetic on a concrete MNIST example, worth doing by hand once. Suppose the true label is 3, so $p$ is one-hot: $p_3 = 1$ and everything else 0. Your model predicts $q_3 = 0.7$ with the remaining 0.3 spread over the other nine classes. Then

$$H(p,q) = -\sum_i p_i \log q_i = -1 \cdot \log 0.7 = 0.357$$

Every term but the true class vanishes because $p_i = 0$ there. So cross-entropy with a one-hot target reduces to the **negative log probability assigned to the correct class**, and nothing else. If the model had been 90% confident the loss would be $-\log 0.9 = 0.105$; at 10% confidence it would be $2.303$. Notice that last number: $-\log(0.1) = \log 10 = 2.303$ is exactly the loss of a model that outputs a uniform distribution over ten classes, which is why an untrained MNIST network starts at a loss of about 2.3 and why a loss stuck at 2.3 means the model has learned nothing at all. That diagnostic recurs throughout Module 09.

```python
import torch, torch.nn.functional as F
logits = torch.tensor([[0.1, 0.2, 0.1, 2.5, 0.0, 0.1, 0.1, 0.2, 0.1, 0.1]])
target = torch.tensor([3])
print(F.softmax(logits, dim=1)[0, 3].item())   # 0.5474  probability of the true class
print(F.cross_entropy(logits, target).item())  # 0.6026  = -log(0.5474)

uniform = torch.zeros(1, 10)
print(F.cross_entropy(uniform, target).item()) # 2.3026 = log(10), the "learned nothing" value
```

## Notation used throughout this book

Fixing conventions now prevents ambiguity later. Scalars are lowercase italic $x$, vectors bold lowercase $\mathbf{x}$, matrices capital $W$. Superscripts in parentheses index **layers** and subscripts index **components**, so $W^{(2)}_{ij}$ is the weight in layer 2 connecting input $j$ to output $i$. The symbol $\theta$ means all parameters collectively. Pre-activations are $\mathbf{z}^{(\ell)} = W^{(\ell)}\mathbf{a}^{(\ell-1)} + \mathbf{b}^{(\ell)}$ and post-activations are $\mathbf{a}^{(\ell)} = \phi(\mathbf{z}^{(\ell)})$, with $\mathbf{a}^{(0)} = \mathbf{x}$ by convention. A single example's loss is $\mathcal{L}$, the objective averaged over a dataset or batch is $J(\theta)$, the learning rate is $\eta$, and batch size is $B$. Predictions are $\hat{y}$ and targets are $y$.

Two conventions are worth stating explicitly because they are easy to trip on. The word "logits" always means the raw, unnormalized outputs of the final layer, *before* any softmax — PyTorch's `nn.CrossEntropyLoss` expects logits and applies the softmax internally, so applying softmax yourself first is a real and common bug. And $\log$ in this book always means the natural logarithm, base $e$, as it does in essentially all machine learning writing.

## Before you move on

What actually needs to be automatic by the end of this module is small. A dot product is a similarity score, and a matrix–vector product asks many similarity questions at once. Shapes must line up, and tracking them by hand is the cheapest debugging habit in the field. The gradient of a scalar function points uphill, so training walks downhill against it. The chain rule multiplies local rates along a path and sums over paths, which is all backpropagation is. And a loss function is a negative log-likelihood, which means choosing a loss is really choosing a distribution for your model's output.

If you can state why PyTorch's `nn.Linear` computes $xA^\top + b$ rather than $Ax + b$ and why that is not a different equation, work out $\nabla f$ for $f(x_1,x_2) = x_1^2 + 3x_1x_2$ at a point without hesitating, and explain why an untrained ten-class classifier starts at a loss near 2.3, you are ready. The last one in particular is the fastest sanity check in practice — if your loss does not start near $\log K$, something is wrong before training even begins. [Exercise Set 02](./exercises/02-exercises.md) drills the shape algebra and the gradient arithmetic until they stop costing you attention.

Next, [Module 03](./03-feedforward-networks-and-activations.md) assembles these pieces into a full feedforward network, makes the case for particular activation functions on grounds you can now evaluate, and examines what the universal approximation theorem does and does not promise.

## Sources

[^m2-linear]: PyTorch documentation, [`torch.nn.Linear`](https://pytorch.org/docs/stable/generated/torch.nn.Linear.html). The operation is documented as $y = xA^\top + b$ with `weight` of shape `(out_features, in_features)`; the code snippet in this module reproduces it exactly.

**Further reading.** Goodfellow, Bengio and Courville devote [Chapter 2](https://www.deeplearningbook.org/contents/linear_algebra.html) to linear algebra, [Chapter 3](https://www.deeplearningbook.org/contents/prob.html) to probability and information theory, and [Chapter 4](https://www.deeplearningbook.org/contents/numerical.html) to numerical computation — all three are pitched at exactly this level and are freely readable online. *Dive into Deep Learning* covers the same ground with runnable code in its [preliminaries chapter](https://d2l.ai/chapter_preliminaries/index.html), which is the better choice if you want to experiment as you read. For the deeper linear algebra intuition, Gilbert Strang's [MIT 18.06](https://ocw.mit.edu/courses/18-06-linear-algebra-spring-2010/) is the standard recommendation and is worth the time if you have it. The [CS231n optimization notes](https://cs231n.github.io/optimization-1/) connect gradients to loss landscapes with unusually good visualizations.
