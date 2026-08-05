# Solutions — Set 11

Worked answers for [Exercise Set 11](../11-exercises.md).

## Part A — Answers

**1. Unrolling and BPTT.**

An RNN applies one function repeatedly: $\mathbf{h}_t = \tanh(W_{xh}\mathbf{x}_t + W_{hh}\mathbf{h}_{t-1} + \mathbf{b})$. Draw $T$ copies of that computation side by side, feeding each one's output into the next, and you have a $T$-layer feedforward network — one "layer" per timestep — in which every layer uses the *same* $W_{xh}$, $W_{hh}$ and $\mathbf{b}$. That is the unrolled graph, and it is a completely ordinary feedforward network as far as autograd is concerned.

Backpropagation through time is therefore ordinary backpropagation on that graph, with one twist that matters: because the weights are **shared** across all $T$ layers, the gradient with respect to $W_{hh}$ is the *sum* of the contributions from every timestep:

$$\frac{\partial \mathcal{L}}{\partial W_{hh}} = \sum_{t=1}^{T} \frac{\partial \mathcal{L}}{\partial \mathbf{h}_t}\,\frac{\partial \mathbf{h}_t}{\partial W_{hh}}\bigg|_{\text{direct}}$$

Nothing about this is special to recurrence — it is the multivariate chain rule applied to a parameter that appears in many places, exactly as in [Module 05](../../05-backpropagation-and-autodiff.md). What *is* special is the consequence: a single weight matrix receives gradient signal from $T$ different depths simultaneously, so it must be simultaneously well-conditioned for one-step and $T$-step propagation. That tension is the source of everything difficult about training RNNs.

Two practical consequences follow. Memory scales with $T$, since every intermediate hidden state must be retained for the backward pass, which is why long sequences are chunked into segments and truncated BPTT is used. And the graph must be built before it is traversed, so a sequence cannot be processed with less than $T$ sequential steps — the point [Module 12](../../12-attention-and-transformers.md) attacks.

**2. The product of Jacobians.**

Differentiate a loss at time $T$ with respect to an early hidden state $\mathbf{h}_k$. The only path from $\mathbf{h}_k$ to $\mathcal{L}_T$ runs through $\mathbf{h}_{k+1}, \mathbf{h}_{k+2}, \dots, \mathbf{h}_T$, so the chain rule gives

$$\frac{\partial \mathcal{L}_T}{\partial \mathbf{h}_k} = \frac{\partial \mathcal{L}_T}{\partial \mathbf{h}_T}\prod_{t=k+1}^{T}\frac{\partial \mathbf{h}_t}{\partial \mathbf{h}_{t-1}}$$

Each factor is $\partial \mathbf{h}_t/\partial \mathbf{h}_{t-1} = D_t W_{hh}$, where $D_t = \mathrm{diag}(1 - \tanh^2(\mathbf{z}_t))$ is the diagonal of activation derivatives. So the product is

$$\prod_{t=k+1}^{T} D_t W_{hh}$$

— **the same matrix multiplied $T-k$ times**, modulated by diagonal factors.

Repeated multiplication by a fixed matrix is governed by its spectral radius $\rho(W_{hh})$, the largest absolute eigenvalue: $\lVert W^n \rVert$ grows or decays like $\rho^n$. If $\rho < 1$ the product vanishes exponentially; if $\rho > 1$ it explodes exponentially. There is no stable middle, because exponentials do not have one — $\rho$ would have to be exactly 1, and even then the diagonal factors intervene.

The $D_t$ terms break the symmetry. Since $1 - \tanh^2(z) \le 1$ always, with equality only at $z = 0$, they can only shrink the product further. **Vanishing is the default and exploding is the exception**, requiring $W_{hh}$ large enough to overcome the saturation. That asymmetry shows up in the measurements below.

**3. Why clipping is asymmetric.**

Gradient clipping rescales the gradient when its norm exceeds a threshold: $g \leftarrow g \cdot \tau / \lVert g \rVert$ if $\lVert g \rVert > \tau$. It preserves the *direction* and caps the *magnitude*.

That works for explosion because an exploded gradient still points somewhere useful. The product of Jacobians has blown up in scale, but the relative contributions of different parameters — the direction in parameter space — is still informative. Rescaling recovers a usable step. Pascanu et al. justify it exactly this way: you are stepping over a cliff in the loss surface, and clipping keeps the step size sane while following the same descent direction.

Vanishing cannot be repaired the same way, because **the information is gone, not merely rescaled**. When a gradient decays to $10^{-16}$, the long-range contribution has been driven below the noise floor of the short-range contributions and below float precision. Multiplying by $10^{16}$ to "unclip" it would amplify the numerical noise along with any signal, and worse, the vanished component is a *fraction* of a gradient that also contains healthy short-range terms — you cannot selectively rescale the part that died without knowing which part that was. Direction is destroyed, not just magnitude.

That is why exploding gradients get a one-line fix and vanishing gradients required an architectural change. The LSTM is the answer to the half of the problem clipping cannot touch.

**4. The LSTM equations.**

$$
\begin{aligned}
\mathbf{f}_t &= \sigma(W_f[\mathbf{h}_{t-1}, \mathbf{x}_t] + \mathbf{b}_f) &&\text{forget gate}\\
\mathbf{i}_t &= \sigma(W_i[\mathbf{h}_{t-1}, \mathbf{x}_t] + \mathbf{b}_i) &&\text{input gate}\\
\tilde{\mathbf{c}}_t &= \tanh(W_c[\mathbf{h}_{t-1}, \mathbf{x}_t] + \mathbf{b}_c) &&\text{candidate}\\
\mathbf{o}_t &= \sigma(W_o[\mathbf{h}_{t-1}, \mathbf{x}_t] + \mathbf{b}_o) &&\text{output gate}\\
\mathbf{c}_t &= \mathbf{f}_t \odot \mathbf{c}_{t-1} + \mathbf{i}_t \odot \tilde{\mathbf{c}}_t &&\text{cell update}\\
\mathbf{h}_t &= \mathbf{o}_t \odot \tanh(\mathbf{c}_t) &&\text{output}
\end{aligned}
$$

The **forget gate** decides, per coordinate, how much of the previous cell state survives. Remove it (fix $\mathbf{f} = 1$) and the cell state can only ever accumulate; it never releases anything, saturates, and the model cannot represent "that context is over." Remove it the other way (fix $\mathbf{f}=0$) and you have destroyed the memory entirely.

The **input gate** decides how much of the new candidate is written. Remove it and every timestep writes at full strength, so irrelevant tokens overwrite important stored context — the model loses its ability to *ignore* input, which on the task below is most of the job.

The **candidate** is what would be written; it is not a gate but the content. Remove it and there is nothing to store.

The **output gate** decides how much of the cell state is exposed as $\mathbf{h}_t$. Remove it and the model must reveal its entire memory at every step, losing the ability to hold something privately for later use while emitting something else now.

The cell state solves vanishing gradients because **its update is additive and its gradient path is multiplication by $\mathbf{f}_t$ rather than by $W_{hh}$**: $\partial \mathbf{c}_t/\partial\mathbf{c}_{t-1} = \mathbf{f}_t$, a diagonal of learned numbers in $(0,1)$, so if the network learns $\mathbf{f} \approx 1$ the gradient flows across arbitrarily many steps essentially undiminished. It is the same trick as a residual connection ([Set 08](./08-solutions.md)), discovered eighteen years earlier.

**5. GRU's merge.**

The GRU merges the forget and input gates into a single **update gate** $\mathbf{z}_t$, enforcing $\text{keep} + \text{write} = 1$: $\mathbf{h}_t = (1-\mathbf{z}_t)\odot\tilde{\mathbf{h}}_t + \mathbf{z}_t \odot \mathbf{h}_{t-1}$. Where the LSTM can independently choose to forget everything *and* write nothing, the GRU cannot — writing implies forgetting a proportional amount. It also eliminates the separate cell state, so $\mathbf{h}_t$ serves both as memory and as output, which removes the output gate too. Three gate-like quantities become two ($\mathbf{r}$, $\mathbf{z}$).

The gain is roughly 25% fewer parameters and correspondingly faster training and less memory, plus one fewer thing to tune. Empirically the two perform comparably on most tasks; Chung et al.'s systematic comparison found no consistent winner, and the honest summary is that the choice usually matters less than the learning rate.

What you might lose is the independent control. On tasks requiring a value to be stored and *held unchanged* through a long stretch of relevant-looking input, the LSTM can set $\mathbf{f}\approx1, \mathbf{i}\approx0$ while the GRU must trade one against the other. And the missing output gate means the GRU cannot hide part of its state. These are real but narrow differences, and in practice both were superseded by attention for exactly the tasks where they would have mattered most.

**6. The seq2seq bottleneck.**

In the original encoder–decoder design, the encoder consumes the entire input sequence and produces a single fixed-size vector — its final hidden state — which is the decoder's only view of the input. Every word of a 40-word sentence must be represented in, say, 512 numbers, and the decoder must reconstruct the whole meaning from them.

It is structural rather than a capacity problem for three reasons. First, the input length is unbounded while the vector is fixed, so for any hidden size there is a sequence long enough to exceed it — you cannot fix an $O(1)$ budget for $O(T)$ information by increasing the constant. Bahdanau et al. showed exactly this empirically: fixed-vector seq2seq degrades sharply as source sentences get longer, while attention-based models do not. Second, the representation is built sequentially, so early tokens must survive the entire encoder recurrence to reach the decoder, which is the vanishing-gradient problem reappearing as an information-flow problem. Third, and most fundamentally, **the encoder must decide what to keep before it knows what will be asked**. Translating a sentence, the decoder may need the subject when it generates the verb and a modifier twenty words later; the encoder cannot know which detail matters at which moment, so it must compress everything equally.

Attention removes the constraint entirely rather than relaxing it: keep *all* the encoder states, and let the decoder look up what it needs at each step. That the fix is a lookup rather than a bigger vector is the whole insight, and it is [Module 12](../../12-attention-and-transformers.md).

## Part B — Reference solutions

### The cells, verified

```python
import torch, torch.nn as nn
torch.set_default_dtype(torch.float64); torch.manual_seed(0)
I, H, B = 5, 7, 3
x, h, c = torch.randn(B, I), torch.randn(B, H), torch.randn(B, H)

# --- RNN ---
r = nn.RNNCell(I, H)
mine = torch.tanh(x @ r.weight_ih.T + r.bias_ih + h @ r.weight_hh.T + r.bias_hh)

# --- LSTM: gates packed [i, f, g, o] ---
l = nn.LSTMCell(I, H)
z = x @ l.weight_ih.T + l.bias_ih + h @ l.weight_hh.T + l.bias_hh
i, f, g, o = z.split(H, dim=1)
i, f, g, o = torch.sigmoid(i), torch.sigmoid(f), torch.tanh(g), torch.sigmoid(o)
c_new = f * c + i * g
h_new = o * torch.tanh(c_new)

# --- GRU: gates packed [r, z, n]; note where r is applied ---
gr = nn.GRUCell(I, H)
zi = x @ gr.weight_ih.T + gr.bias_ih
zh = h @ gr.weight_hh.T + gr.bias_hh
ri, zi_, ni = zi.split(H, 1)
rh_, zh_, nh = zh.split(H, 1)
rr = torch.sigmoid(ri + rh_)
zz = torch.sigmoid(zi_ + zh_)
n  = torch.tanh(ni + rr * nh)              # r multiplies the *hidden* term only
h_gru = (1 - zz) * n + zz * h
```
```
RNNCell  max diff: 1.11e-16
LSTMCell max diff: h 2.78e-17   c 1.11e-16
GRUCell  max diff: 2.22e-16
manual unroll vs nn.RNN: 2.22e-16
```

Three implementation details are worth more than the equations, because they are what actually costs you time.

**PyTorch carries two bias vectors**, `bias_ih` and `bias_hh`, and adds both. Mathematically this is redundant — one bias would do — and it exists for CuDNN compatibility. The consequence bites when you want to *set* a bias: filling the forget-gate slice of both vectors with 1.0 gives an effective forget bias of **2.0**, not 1.0. If your forget-bias experiment behaves oddly, this is why.

**Gate ordering is a convention you must look up.** PyTorch packs the LSTM's four gates as $[i, f, g, o]$ in a single $4H \times \cdot$ weight matrix, so the forget gate is rows $[H : 2H]$. The GRU packs $[r, z, n]$, so the retention-related gate is *also* rows $[H:2H]$ — a coincidence that is convenient and completely undocumented in the shape of the tensor.

**The GRU applies $\mathbf{r}$ after the hidden matmul.** The candidate is $\tanh(W_{in}x + b_{in} + \mathbf{r}\odot(W_{hn}h + b_{hn}))$, not $\tanh(W_{in}x + W_{hn}(\mathbf{r}\odot h))$. Both appear in the literature; PyTorch implements the former, and getting it wrong produces a cell that trains but does not match. Read the [`nn.GRU` docs](https://pytorch.org/docs/stable/generated/torch.nn.GRU.html) formula rather than a blog post.

### Gradient decay, measured

Backpropagating from the output at time $T$ to the input at time 1, and reporting the ratio against the gradient at time $T$:

| $T$ | RNN | LSTM (forget bias 1) |
| --- | --- | --- |
| 10 | $2.4\times10^{-3}$ | 0.46 |
| 30 | $8.5\times10^{-9}$ | 0.73 |
| 60 | $8.6\times10^{-17}$ | 0.36 |

This is the entire argument for the LSTM in one table. The RNN's long-range gradient decays by roughly a factor of $10^{-8}$ per thirty steps and reaches $10^{-16}$ at $T=60$ — which is float64 machine epsilon, meaning the first timestep is **numerically indistinguishable from having no influence at all**. The LSTM's stays within a factor of three of the short-range gradient at every length tested, with no trend in $T$.

The spectral-radius prediction, checked directly by rescaling random matrices and propagating a unit vector fifty times:

| spectral radius $\rho$ | $\lVert W^{50}\mathbf{h}\rVert$ |
| --- | --- |
| 0.9 | $6.0\times10^{-4}$ |
| 1.0 | $1.2\times10^{-1}$ |
| 1.1 | $1.4\times10^{1}$ |

Five orders of magnitude from a 22% change in $\rho$. ([Module 11](../../11-sequence-models.md) reports a similar table with different constants — $5.9\times10^{-3}$, $2.2$, $2.1\times10^{2}$ — because it uses $5\times5$ matrices and the Frobenius norm of $W^{50}$ rather than $7\times7$ and the norm of $W^{50}\mathbf{h}$. The *ratios between rows* are what the theory predicts, and those agree; the absolute values depend on the matrix and the norm.)

### The long-dependency task, and the surprise

The task: a length-$T$ sequence over a vocabulary of 8, where position 0 carries the signal (token 1 or 2) and positions $1..T{-}1$ are noise from tokens 3–7. Predict the signal from the hidden state at time $T$. Embedding of size 16, hidden size 32, Adam at $3\times10^{-3}$, gradient clipping at 1.0, batch 64. Accuracy on 2,000 fresh sequences; 50% is chance.

$T = 50$:

| steps | RNN | LSTM (default) | **LSTM (forget bias 1)** | GRU (default) |
| --- | --- | --- | --- | --- |
| 250 | 48.9% | 50.7% | **100.0%** | 50.0% |
| 500 | 48.9% | 47.5% | **100.0%** | 50.3% |
| 1000 | 45.6% | 51.5% | **100.0%** | 50.9% |
| 2000 | 62.1% | 51.0% | **100.0%** | 51.2% |
| 4000 | **100.0%** | 48.1% | **100.0%** | 48.5% |

Read the first two data columns and the textbook is wrong. The plain RNN — the architecture that supposedly cannot learn long dependencies — solves the task perfectly at 4,000 steps, while the LSTM and GRU sit at chance for the entire budget. If you ran this experiment expecting to confirm what you had read, you would conclude your code was broken.

It is not broken. **The default forget-gate bias is zero**, so at initialization $\sigma(0) = 0.5$ and the cell state is multiplied by roughly one half at every timestep. Over 50 steps that is $0.5^{50} \approx 10^{-15}$. The LSTM's celebrated gradient highway is *closed at initialization*, and the network must first learn to open it — but the gradient signal that would teach it to do so has itself decayed by $10^{-15}$. It is stuck in a self-reinforcing dead zone.

The fix is one line: initialize the forget-gate bias to 1, so $\sigma(1) = 0.73$ and, more importantly, the gate starts in a regime where it can be pushed toward 1 by a gradient that actually arrives.

```python
for name, p in lstm.named_parameters():
    if "bias" in name:
        with torch.no_grad():
            p[H:2*H].fill_(1.0)          # slice [H:2H] is the forget gate
```

That takes the LSTM from chance at 4,000 steps to **100% at 250 steps** — a speedup of at least sixteen-fold, and possibly unbounded, since the default never solves it at all. The same fix applied to the GRU's update gate (also slice `[H:2H]`) takes it to 100% at 250, 1000 and 4000 steps.

Three things to take away.

**This is a known result that is under-taught.** Jozefowicz, Zaremba and Sutskever's ["An Empirical Exploration of Recurrent Network Architectures"](https://proceedings.mlr.press/v37/jozefowicz15.html) recommends forget-bias 1 explicitly, and Gers et al. noted it in 2000. It is in most serious implementations and in almost no tutorials, and PyTorch does not do it by default. If you train an LSTM on anything with long-range structure and it will not learn, check this first.

**The RNN result is a genuine caveat on the vanishing-gradient story.** The gradient measurement above was taken *at initialization*; it says the gradient is $10^{-16}$ for a random $W_{hh}$, not that it stays there. Training can drift $W_{hh}$ toward spectral radius near 1, at which point the long path opens. Adam helps enormously, because it normalizes by the gradient's own running magnitude ([Set 06](./06-solutions.md)) — a consistently tiny but consistently *signed* gradient still produces a full-sized Adam step where SGD would produce a $10^{-16}$-sized one. So the accurate statement is not "RNNs cannot learn long dependencies" but "RNNs are enormously less sample-efficient at it and depend on escaping a bad initialization." A 16× difference on a toy task is an untrainable model on a real one.

**Architectural claims are claims about optimization, not just about representation.** All three architectures can *represent* the solution — the RNN eventually finds it. What differs is how easily gradient descent gets there from the initialization you happen to use, and that makes initialization part of the architecture rather than a detail beneath it. The same lesson appeared in [Set 08](./08-solutions.md) with He initialization and zero-init residual blocks, and it will appear again in [Set 12](.././12-exercises.md) with the $1/\sqrt{d_k}$ scaling factor.

---

Back to [Set 11](../11-exercises.md) · Next solutions: [Set 12](./12-solutions.md)
