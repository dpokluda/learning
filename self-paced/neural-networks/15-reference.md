# 15 — Reference: glossary, formulas, and sources

This module is not meant to be read straight through. It is the part of the book you come back to — a glossary when a term has gone fuzzy, a formula sheet when you need the exact form of an update rule, a symptom table when a model will not train, and a consolidated bibliography when you want the primary source.

Everything here is stated in the notation established in [Module 02](./02-mathematical-foundations.md) and used consistently throughout. Where a definition is contested or commonly misstated, the entry says so.

> **Prerequisite:** none — this is a lookup module. Each entry links back to the module that develops it properly.

## Notation

| symbol | meaning |
| --- | --- |
| $x$, $\mathbf{x}$, $X$ | scalar, vector (column), matrix |
| $\mathbf{x}^{(i)}$ | the $i$-th training example |
| $W^{(\ell)}, \mathbf{b}^{(\ell)}$ | weights and biases of layer $\ell$ |
| $\mathbf{z}^{(\ell)} = W^{(\ell)}\mathbf{a}^{(\ell-1)} + \mathbf{b}^{(\ell)}$ | pre-activation of layer $\ell$ |
| $\mathbf{a}^{(\ell)} = \phi(\mathbf{z}^{(\ell)})$ | activation of layer $\ell$; $\mathbf{a}^{(0)} = \mathbf{x}$ |
| $\phi$ | activation function |
| $\hat{\mathbf{y}}$ | model output |
| $\theta$ | all trainable parameters collectively |
| $\mathcal{L}(\hat{y}, y)$ | loss on one example |
| $J(\theta)$ | objective over the dataset (mean loss, plus regularizers) |
| $\eta$ | learning rate |
| $\lambda$ | regularization strength |
| $B$ | minibatch size |
| $\odot$ | elementwise (Hadamard) product |
| $\nabla_\theta J$ | gradient of $J$ with respect to $\theta$ |
| $\delta^{(\ell)} = \partial\mathcal{L}/\partial\mathbf{z}^{(\ell)}$ | error signal at layer $\ell$ |
| $\log$ | natural logarithm, always |

Two conventions worth restating because they cause more confusion than anything else. **Logits** always means the raw pre-softmax scores; they are unbounded and are what PyTorch's loss functions expect. And the mathematical convention $W\mathbf{x}$ with $W$ of shape (out, in) corresponds in PyTorch to `x @ W.T + b` with `x` of shape (batch, in) — the transpose is a layout choice, not a different operation. See [Module 02](./02-mathematical-foundations.md).

## Formula sheet

**Activations** ([Module 03](./03-feedforward-networks-and-activations.md))

$$\sigma(z) = \frac{1}{1+e^{-z}}, \quad \sigma'(z) = \sigma(z)(1-\sigma(z)) \le \tfrac14$$
$$\tanh(z) = 2\sigma(2z) - 1, \quad \tanh'(z) = 1 - \tanh^2(z) \le 1$$
$$\text{ReLU}(z) = \max(0, z), \quad \text{ReLU}'(z) = \mathbb{1}[z>0]$$
$$\text{softmax}(\mathbf{z})_i = \frac{e^{z_i}}{\sum_j e^{z_j}} = \frac{e^{z_i - \max_k z_k}}{\sum_j e^{z_j - \max_k z_k}}$$

The second softmax form is the numerically stable one and is what any library actually computes; the shift cancels exactly.

**Losses** ([Module 04](./04-loss-functions-and-the-probabilistic-view.md))

$$\mathcal{L}_{\text{MSE}} = \tfrac{1}{2}\lVert\hat{\mathbf{y}} - \mathbf{y}\rVert^2 \qquad \text{(MLE under Gaussian noise)}$$
$$\mathcal{L}_{\text{BCE}} = -\big[y\log\hat{y} + (1-y)\log(1-\hat{y})\big] \qquad \text{(MLE under Bernoulli)}$$
$$\mathcal{L}_{\text{CE}} = -\log \text{softmax}(\mathbf{z})_{y} = -z_y + \log\!\sum_j e^{z_j} \qquad \text{(MLE under Categorical)}$$

The right-hand form of cross-entropy is the one to implement: it never materializes the probabilities and is stable for any logit magnitude. Its gradient is the single most useful fact in the book:

$$\frac{\partial \mathcal{L}_{\text{CE}}}{\partial \mathbf{z}} = \text{softmax}(\mathbf{z}) - \mathbf{y}_{\text{onehot}}$$

Predicted minus actual — no activation derivative, so no saturation, which is precisely why cross-entropy trains classifiers and MSE does not.

**Backpropagation** ([Module 05](./05-backpropagation-and-autodiff.md))

$$
\begin{aligned}
\delta^{(L)} &= \nabla_{\mathbf{a}^{(L)}}\mathcal{L} \odot \phi'(\mathbf{z}^{(L)}) \\
\delta^{(\ell)} &= \left(W^{(\ell+1)\top}\delta^{(\ell+1)}\right) \odot \phi'(\mathbf{z}^{(\ell)}) \\
\frac{\partial\mathcal{L}}{\partial W^{(\ell)}} &= \delta^{(\ell)}\,\mathbf{a}^{(\ell-1)\top} \\
\frac{\partial\mathcal{L}}{\partial\mathbf{b}^{(\ell)}} &= \delta^{(\ell)}
\end{aligned}
$$

**Optimizers** ([Module 06](./06-optimization.md)). All use $g_t = \nabla_\theta J(\theta_{t-1})$.

$$\text{SGD:}\quad \theta_t = \theta_{t-1} - \eta\, g_t$$
$$\text{Momentum (PyTorch):}\quad v_t = \mu v_{t-1} + g_t, \qquad \theta_t = \theta_{t-1} - \eta\, v_t$$
$$\text{RMSProp:}\quad s_t = \rho s_{t-1} + (1-\rho)g_t^2, \qquad \theta_t = \theta_{t-1} - \frac{\eta}{\sqrt{s_t}+\epsilon}g_t$$
$$\text{Adam:}\quad
\begin{aligned}
m_t &= \beta_1 m_{t-1} + (1-\beta_1)g_t, &\hat{m}_t &= m_t/(1-\beta_1^t) \\
v_t &= \beta_2 v_{t-1} + (1-\beta_2)g_t^2, &\hat{v}_t &= v_t/(1-\beta_2^t) \\
\theta_t &= \theta_{t-1} - \eta\,\hat{m}_t/(\sqrt{\hat{v}_t}+\epsilon)
\end{aligned}$$

Note PyTorch's momentum convention keeps $\eta$ *outside* the velocity, which matters when a schedule changes $\eta$ mid-run. Defaults: $\mu = 0.9$; $\beta_1 = 0.9$, $\beta_2 = 0.999$, $\epsilon = 10^{-8}$.

**Regularization** ([Module 07](./07-generalization-and-regularization.md))

$$J_{\text{reg}}(\theta) = J(\theta) + \frac{\lambda}{2}\lVert\theta\rVert_2^2 \quad\Longrightarrow\quad \text{gradient gains } \lambda\theta$$

L2 penalty and weight decay coincide for plain SGD but *not* for Adam, where the penalty is divided by $\sqrt{\hat v_t}$ along with everything else. Use `AdamW`, which decouples them. Dropout multiplies activations by a Bernoulli($1-p$) mask at training time and scales by $1/(1-p)$ so that expectations match at test time — `model.eval()` is what turns it off.

**Initialization** ([Module 08](./08-initialization-and-normalization.md))

$$\text{Xavier/Glorot:}\quad \operatorname{Var}(W_{ij}) = \frac{2}{n_{\text{in}} + n_{\text{out}}} \qquad \text{(symmetric activations: tanh)}$$
$$\text{He/Kaiming:}\quad \operatorname{Var}(W_{ij}) = \frac{2}{n_{\text{in}}} \qquad \text{(ReLU; the 2 compensates for the killed half)}$$

**Normalization** ([Module 08](./08-initialization-and-normalization.md)). Both compute $\hat{x} = (x-\mu)/\sqrt{\sigma^2+\epsilon}$ then $y = \gamma\hat{x}+\beta$; they differ only in which axes $\mu,\sigma$ are taken over. BatchNorm averages over the batch (so it depends on batch size and needs running statistics at inference); LayerNorm averages over the features of each example independently (so it does not).

**Convolution output shape** ([Module 10](./10-convolutional-networks.md))

$$H_{\text{out}} = \left\lfloor\frac{H_{\text{in}} + 2P - D(K-1) - 1}{S}\right\rfloor + 1$$

with padding $P$, dilation $D$, kernel $K$, stride $S$. For the common $D=1$ case, "same" padding is $P = (K-1)/2$ with odd $K$. A `Conv2d(C_in, C_out, K)` layer has $C_{\text{out}}(C_{\text{in}}K^2 + 1)$ parameters.

**Recurrence** ([Module 11](./11-sequence-models.md))

$$\text{RNN:}\quad \mathbf{h}_t = \tanh(W_{hh}\mathbf{h}_{t-1} + W_{xh}\mathbf{x}_t + \mathbf{b})$$
$$\text{LSTM:}\quad \mathbf{c}_t = \mathbf{f}_t\odot\mathbf{c}_{t-1} + \mathbf{i}_t\odot\mathbf{g}_t, \qquad \mathbf{h}_t = \mathbf{o}_t\odot\tanh(\mathbf{c}_t)$$
$$\text{GRU:}\quad \mathbf{h}_t = \mathbf{z}_t\odot\mathbf{h}_{t-1} + (1-\mathbf{z}_t)\odot\tilde{\mathbf{h}}_t$$

PyTorch gate order is $[\mathbf{i},\mathbf{f},\mathbf{g},\mathbf{o}]$ for LSTM and $[\mathbf{r},\mathbf{z},\mathbf{n}]$ for GRU; in both, the retention gate occupies slice `[H:2H]` and should be bias-initialized to +1.

**Attention** ([Module 12](./12-attention-and-transformers.md))

$$\text{Attention}(Q,K,V) = \operatorname{softmax}\!\left(\frac{QK^\top}{\sqrt{d_k}}\right)V$$

The $\sqrt{d_k}$ is because $\operatorname{Var}(\mathbf{q}\cdot\mathbf{k}) = d_k$ for unit-variance components; without it the softmax saturates at large $d_k$. Multi-head splits $d_{\text{model}}$ into $h$ heads of size $d_{\text{model}}/h$, so the cost equals single-head. A pre-norm block is $x \leftarrow x + \text{Sublayer}(\text{LayerNorm}(x))$.

## Glossary

**Activation function** — the elementwise nonlinearity $\phi$ applied after each linear layer. Without one, a stack of linear layers collapses to a single linear layer. [M03](./03-feedforward-networks-and-activations.md)

**Attention** — a differentiable weighted lookup: score a query against keys, softmax the scores, return the weighted average of values. [M12](./12-attention-and-transformers.md)

**Autograd / automatic differentiation** — building a graph of primitive operations during the forward pass and applying the chain rule backward through it. Reverse-mode computes all gradients of one scalar output in one backward pass at roughly the cost of the forward pass. Neither symbolic nor numerical differentiation. [M05](./05-backpropagation-and-autodiff.md)

**Backpropagation** — reverse-mode automatic differentiation applied to a neural network. Not an optimization algorithm; it computes gradients, and the optimizer uses them. [M05](./05-backpropagation-and-autodiff.md)

**Batch normalization** — normalizing each feature over the batch dimension, then rescaling by learned $\gamma,\beta$. Its original "internal covariate shift" justification is likely wrong; the loss-landscape-smoothing account has better evidence. [M08](./08-initialization-and-normalization.md)

**BPTT (backpropagation through time)** — ordinary backprop on an unrolled recurrent graph. *Truncated* BPTT limits the backward horizon by detaching the hidden state between chunks. [M11](./11-sequence-models.md)

**Capacity** — informally, how complex a function class a model can represent. Classical measures (VC dimension, Rademacher complexity) badly mispredict deep network generalization. [M07](./07-generalization-and-regularization.md)

**Cross-entropy** — $-\sum_i y_i \log \hat{y}_i$; the negative log-likelihood of a categorical model, and the standard classification loss. [M04](./04-loss-functions-and-the-probabilistic-view.md)

**Convolution** — a translation-equivariant linear operation applying the same small kernel at every spatial position; parameter sharing plus locality. (Deep-learning "convolution" is technically cross-correlation; the distinction is immaterial when the kernel is learned.) [M10](./10-convolutional-networks.md)

**Distillation** — training a small student model to match a large teacher's outputs. [M13](./13-transfer-learning-and-embeddings.md)

**Double descent** — the phenomenon where test error decreases, increases to a peak at the interpolation threshold, then decreases again as capacity grows further, contradicting the classical U-curve. [M07](./07-generalization-and-regularization.md)

**Dropout** — randomly zeroing activations during training, which prevents co-adaptation and approximates an ensemble. Active only in `model.train()`. [M07](./07-generalization-and-regularization.md)

**Early stopping** — halting when validation loss stops improving and restoring the best checkpoint; the cheapest effective regularizer. [M07](./07-generalization-and-regularization.md)

**Embedding** — a learned dense vector representation of a discrete item, in which geometric relationships encode semantic ones. [M13](./13-transfer-learning-and-embeddings.md)

**Epoch** — one full pass over the training set. Steps, not epochs, is usually the more meaningful unit.

**Fine-tuning** — continuing training of a pretrained model on a new task, usually at a much lower learning rate. Contrast *linear probing*, which trains only a new head on frozen features. [M13](./13-transfer-learning-and-embeddings.md)

**Gradient clipping** — rescaling the gradient vector when its norm exceeds a threshold. Fixes exploding gradients; does nothing for vanishing ones. [M11](./11-sequence-models.md)

**Gradient descent** — iteratively stepping parameters opposite the gradient. *Stochastic* gradient descent estimates the gradient from a minibatch; the noise is both a computational necessity and a mild regularizer. [M06](./06-optimization.md)

**Initialization** — the choice of starting parameter values. Determines whether signal and gradient magnitudes are preserved through depth; a bad choice makes a well-designed architecture untrainable. [M08](./08-initialization-and-normalization.md)

**Layer normalization** — normalizing over the feature dimension of each example independently. Batch-size- and sequence-length-independent, which is why Transformers use it. [M08](./08-initialization-and-normalization.md)

**Learning rate** — the step size $\eta$. The single most important hyperparameter; if you tune one thing, tune this. [M06](./06-optimization.md)

**Logits** — raw pre-softmax scores. Unbounded, and what PyTorch loss functions expect. [M04](./04-loss-functions-and-the-probabilistic-view.md)

**LoRA (low-rank adaptation)** — freezing pretrained weights and training a low-rank update $BA$ alongside them, cutting trainable parameters by orders of magnitude. [M13](./13-transfer-learning-and-embeddings.md)

**Maximum likelihood estimation (MLE)** — choosing parameters that maximize the probability of the observed data. Nearly every standard loss is a negative log-likelihood under some noise model. [M04](./04-loss-functions-and-the-probabilistic-view.md)

**Momentum** — accumulating an exponentially-weighted average of past gradients, which damps oscillation across steep directions and accelerates along consistent ones. [M06](./06-optimization.md)

**Overfitting** — low training error with high test error; the model has fit sample-specific noise. Diagnosed by the gap between the two curves, not by either alone. [M07](./07-generalization-and-regularization.md)

**Perceptron** — the 1958 single-layer linear threshold classifier, provably unable to represent XOR. [M01](./01-what-is-a-neural-network.md)

**Positional encoding** — position information added to token representations, necessary because self-attention is permutation-equivariant. [M12](./12-attention-and-transformers.md)

**Pooling** — downsampling a feature map by summarizing local regions (max or average), giving small translation invariance and reducing resolution. Largely replaced by strided convolution in modern designs. [M10](./10-convolutional-networks.md)

**Receptive field** — the region of the input that influences one unit's activation. Grows with depth, kernel size, stride and dilation. [M10](./10-convolutional-networks.md)

**Residual connection** — $y = x + F(x)$, giving an identity path so gradients reach early layers undiminished and depth costs little. In essentially every deep architecture since 2015. [M10](./10-convolutional-networks.md)

**Self-attention** — attention where queries, keys and values are all derived from the same sequence. [M12](./12-attention-and-transformers.md)

**Self-supervised learning** — creating supervision from the data's own structure (predict the next token, reconstruct masked pixels), removing the labelling bottleneck. [M14](./14-modern-landscape.md)

**Softmax** — mapping a vector of reals to a probability distribution via normalized exponentials. Shift-invariant, which is what makes the stable implementation possible. [M04](./04-loss-functions-and-the-probabilistic-view.md)

**Teacher forcing** — feeding a decoder the ground-truth previous token during training rather than its own prediction. Stabilizes and parallelizes training at the cost of exposure bias. [M11](./11-sequence-models.md)

**Transfer learning** — reusing representations learned on one task for another. [M13](./13-transfer-learning-and-embeddings.md)

**Universal approximation** — the theorem that a one-hidden-layer network can approximate any continuous function on a compact set to any accuracy. An existence result about representability only: it says nothing about the width required, nor whether gradient descent can find the weights. [M03](./03-feedforward-networks-and-activations.md)

**Vanishing / exploding gradients** — the exponential decay or growth of gradients through repeated multiplication by Jacobians, in depth or in time. Exploding is easy to fix (clip); vanishing needs architecture (gates, skips) or normalization. [M08](./08-initialization-and-normalization.md), [M11](./11-sequence-models.md)

**Weight decay** — shrinking weights toward zero at each step. Equivalent to an L2 penalty for SGD but not for Adam; `AdamW` implements the decoupled version. [M07](./07-generalization-and-regularization.md)

## Debugging quick reference

The full treatment is in [Module 09](./09-practical-training-and-debugging.md); this is the lookup version.

| symptom | most likely causes |
| --- | --- |
| loss is `nan` | learning rate too high; `log(0)` from manual softmax; division by near-zero; missing gradient clipping in an RNN |
| loss does not move at all | forgot `optimizer.zero_grad()` or `loss.backward()`; parameters not passed to the optimizer; learning rate ~0; all-zero initialization |
| initial loss is not $\ln(\text{classes})$ | labels misaligned; final layer badly initialized; applying softmax before `cross_entropy` (double softmax) |
| training loss falls, validation loss rises | overfitting — add regularization, get more data, stop earlier |
| both losses plateau high | underfitting — bigger model, higher learning rate, train longer, check for a bug first |
| cannot overfit a single batch | there is a bug; stop tuning and find it |
| validation better than training | dropout/BatchNorm active during training only (usually fine); or a leaky split |
| accuracy good, probabilities untrustworthy | miscalibration — check loss, not just accuracy; consider temperature scaling |
| works in training, fails at inference | forgot `model.eval()`; different preprocessing; BatchNorm running statistics |
| results differ run to run beyond noise | unseeded RNG; nondeterministic kernels; data-order effects |

The single highest-value habit in this table: **overfit one batch first**. A model that cannot drive the loss on eight examples to near zero has a bug, and no amount of hyperparameter tuning will fix it.

## The order to read the primary sources

If you read nothing else, read these seven papers in this order. Each is short, each changed the field, and together they are the spine of the whole book.

Rumelhart, Hinton and Williams (1986) on backpropagation is where the whole enterprise becomes possible. Krizhevsky, Sutskever and Hinton (2012) on AlexNet is where it becomes practical. Srivastava et al. (2014) on dropout and Ioffe and Szegedy (2015) on batch normalization are the two techniques that made deep networks routinely trainable. He et al. (2015) on ResNet solves depth. Kingma and Ba (2014) on Adam is the optimizer everyone actually uses. And Vaswani et al. (2017) on the Transformer is the architecture everything since is built from.

## Consolidated bibliography

**Books**

- Ian Goodfellow, Yoshua Bengio and Aaron Courville, [*Deep Learning*](https://www.deeplearningbook.org/), MIT Press 2016. Free online. The rigorous reference; predates Transformers.
- Aston Zhang, Zachary Lipton, Mu Li and Alexander Smola, [*Dive into Deep Learning*](https://d2l.ai/), Cambridge 2023. Free online, runnable code in PyTorch/TensorFlow/JAX. The best companion to this book.
- Kevin Murphy, [*Probabilistic Machine Learning*](https://probml.github.io/pml-book/), MIT Press 2022/2023. Free online. The statistical foundations.
- Michael Nielsen, [*Neural Networks and Deep Learning*](http://neuralnetworksanddeeplearning.com/). Free online; the gentlest correct introduction to backpropagation anywhere.
- Simon Prince, [*Understanding Deep Learning*](https://udlbook.github.io/udlbook/), MIT Press 2023. Free online; modern and unusually well illustrated.

**Courses**

- [CS231n: Deep Learning for Computer Vision](https://cs231n.github.io/) (Stanford) — the notes on optimization, backprop and convolution are canonical.
- [CS224n: NLP with Deep Learning](https://web.stanford.edu/class/cs224n/) (Stanford) — sequence models, attention, Transformers.
- [Neural Networks: Zero to Hero](https://karpathy.ai/zero-to-hero.html) (Karpathy) — builds autograd, then a language model, then GPT, from nothing.
- [Practical Deep Learning for Coders](https://course.fast.ai/) (fast.ai) — top-down and practical.

**Papers by module**

*Foundations* — [Rosenblatt 1958, the perceptron](https://doi.org/10.1037/h0042519); [Rumelhart, Hinton & Williams 1986, backpropagation](https://www.nature.com/articles/323533a0); [Cybenko 1989](https://doi.org/10.1007/BF02551274) and [Hornik 1991](https://doi.org/10.1016/0893-6080%2891%2990009-T) on universal approximation.

*Optimization* — [Kingma & Ba 2014, Adam](https://arxiv.org/abs/1412.6980); [Loshchilov & Hutter 2017, AdamW and SGDR](https://arxiv.org/abs/1711.05101); [Reddi et al. 2018, on Adam's convergence](https://arxiv.org/abs/1904.09237); [Smith 2015, LR range test](https://arxiv.org/abs/1506.01186).

*Generalization* — [Srivastava et al. 2014, dropout](https://jmlr.org/papers/v15/srivastava14a.html); [Zhang et al. 2017, rethinking generalization](https://arxiv.org/abs/1611.03530); [Belkin et al. 2019, double descent](https://arxiv.org/abs/1812.11118).

*Initialization and normalization* — [Glorot & Bengio 2010, Xavier](https://proceedings.mlr.press/v9/glorot10a.html); [He et al. 2015, He init and PReLU](https://arxiv.org/abs/1502.01852); [Ioffe & Szegedy 2015, BatchNorm](https://arxiv.org/abs/1502.03167); [Santurkar et al. 2018, why BatchNorm works](https://arxiv.org/abs/1805.11604); [Ba et al. 2016, LayerNorm](https://arxiv.org/abs/1607.06450).

*Convolutional networks* — [LeCun et al. 1998, LeNet](http://yann.lecun.com/exdb/publis/pdf/lecun-98.pdf); [Krizhevsky et al. 2012, AlexNet](https://papers.nips.cc/paper/4824-imagenet-classification-with-deep-convolutional-neural-networks); [Simonyan & Zisserman 2014, VGG](https://arxiv.org/abs/1409.1556); [He et al. 2015, ResNet](https://arxiv.org/abs/1512.03385).

*Sequence models* — [Hochreiter & Schmidhuber 1997, LSTM](https://doi.org/10.1162/neco.1997.9.8.1735); [Gers et al. 2000, forget gate](https://doi.org/10.1162/089976600300015015); [Cho et al. 2014, GRU](https://arxiv.org/abs/1406.1078); [Sutskever et al. 2014, seq2seq](https://arxiv.org/abs/1409.3215); [Pascanu et al. 2013, exploding gradients](https://arxiv.org/abs/1211.5063).

*Attention and Transformers* — [Bahdanau et al. 2015, attention](https://arxiv.org/abs/1409.0473); [Vaswani et al. 2017, the Transformer](https://arxiv.org/abs/1706.03762); [Xiong et al. 2020, pre-norm](https://arxiv.org/abs/2002.04745); [Dao et al. 2022, FlashAttention](https://arxiv.org/abs/2205.14135).

*Transfer and modern* — [Yosinski et al. 2014, feature transferability](https://arxiv.org/abs/1411.1792); [Mikolov et al. 2013, word2vec](https://arxiv.org/abs/1301.3781); [Devlin et al. 2019, BERT](https://arxiv.org/abs/1810.04805); [Hu et al. 2021, LoRA](https://arxiv.org/abs/2106.09685); [Radford et al. 2021, CLIP](https://arxiv.org/abs/2103.00020); [Hoffmann et al. 2022, Chinchilla](https://arxiv.org/abs/2203.15556); [Ho et al. 2020, DDPM](https://arxiv.org/abs/2006.11239).

**Documentation**

The [PyTorch docs](https://pytorch.org/docs/stable/index.html) are precise about exact formulas and parameter layouts and are worth reading rather than guessing — particularly [`nn.LSTM`](https://pytorch.org/docs/stable/generated/torch.nn.LSTM.html), [`nn.Conv2d`](https://pytorch.org/docs/stable/generated/torch.nn.Conv2d.html), [`nn.MultiheadAttention`](https://pytorch.org/docs/stable/generated/torch.nn.MultiheadAttention.html) and [`torch.optim`](https://pytorch.org/docs/stable/optim.html), each of which states its equations explicitly. The [tutorials](https://pytorch.org/tutorials/) and the [autograd mechanics](https://pytorch.org/docs/stable/notes/autograd.html) note are the other two things worth reading start to finish. [torchvision models](https://pytorch.org/vision/stable/models.html) documents the pretrained weights and their required preprocessing transforms.

## Where the field disagrees

Five places this book took a position that is not universally shared, collected so you know which of your beliefs are provisional.

**Why BatchNorm works.** The original internal-covariate-shift explanation is still repeated everywhere and is probably wrong; Santurkar et al. showed you can inject covariate shift after BatchNorm and still get the benefit. The loss-smoothing account is better supported but not conclusive. [M08](./08-initialization-and-normalization.md)

**Adam versus SGD generalization.** A persistent claim is that SGD with momentum generalizes better than Adam on vision tasks. The evidence is real but confounded by tuning effort, and AdamW closes much of the gap. Treat it as task-dependent. [M06](./06-optimization.md)

**Whether classical generalization theory applies.** Networks that memorize random labels also generalize well on real ones, which no classical capacity bound explains. Several partial accounts exist; none is settled. [M07](./07-generalization-and-regularization.md)

**Attention as explanation.** Attention heatmaps are widely presented as interpretations of model behaviour, and there is a substantial literature arguing they should not be. Read both sides. [M12](./12-attention-and-transformers.md)

**Emergent abilities.** Whether capabilities appear discontinuously with scale or only appear to under discontinuous metrics is genuinely open. [M14](./14-modern-landscape.md)

## Before you move on

There is nothing to move on to — this is the end of the book. If you have read the modules and worked the exercises, you can derive backpropagation, implement an optimizer, diagnose a failing model, and read the architecture section of a modern paper without hand-waving. That is a real foundation, and it is the part that does not go stale.

Come back to this module when a symbol has gone fuzzy or a formula needs checking. Everything else — the specific architectures, the current best model, the hyperparameters that happen to work this year — will change. The parts that will not are the chain rule, the probabilistic view of loss functions, the exponential behaviour of repeated Jacobian products, and the discipline of measuring the claim instead of repeating it.
