# Solutions — Set 12

Worked answers for [Exercise Set 12](../12-exercises.md).

## Part A — Answers

**1. Attention as a soft dictionary lookup.**

An ordinary dictionary lookup takes a query, compares it against a set of keys, finds the one that matches exactly, and returns the associated value. Attention does the same thing with two changes: the comparison is a similarity score rather than an equality test, and the return value is a weighted average over *all* values rather than a single one.

Concretely, every position emits three vectors. The **key** advertises what information that position holds — "I am a plural noun," "I am the subject of the sentence." The **query** advertises what the current position is looking for — "I need to know the number of my subject." The **value** is what actually gets retrieved if the position is attended to, which need not be the same as what the key advertised. The score $\mathbf{q}\cdot\mathbf{k}$ measures how well the advertisement matches the request, softmax turns the scores into a probability distribution, and the output is $\sum_j \alpha_j \mathbf{v}_j$.

What makes it *soft* is the softmax: instead of selecting one entry, it produces a distribution over all of them and blends. That is what makes it differentiable, and differentiability is the whole reason it can be learned. A hard lookup — pick the maximum — has zero gradient almost everywhere and cannot be trained by gradient descent. Softness is not an approximation of the thing we wanted; it is the thing that makes the mechanism trainable at all, and the same argument recurs everywhere in this field.

Reading it this way also explains why the three projections are learned. The network is not given keys and queries; it *invents* a communication protocol during training, deciding for itself what each position should advertise and ask for.

**2. Why $\sqrt{d_k}$.**

Let $\mathbf{q}$ and $\mathbf{k}$ have i.i.d. components with mean 0 and variance 1. Then

$$\mathbf{q}\cdot\mathbf{k} = \sum_{i=1}^{d_k} q_i k_i$$

Each term has mean $\mathbb{E}[q_i]\mathbb{E}[k_i] = 0$ and variance $\mathbb{E}[q_i^2 k_i^2] = \mathbb{E}[q_i^2]\mathbb{E}[k_i^2] = 1$. The terms are independent, so variances add:

$$\mathrm{Var}(\mathbf{q}\cdot\mathbf{k}) = d_k$$

The standard deviation is therefore $\sqrt{d_k}$, and dividing by $\sqrt{d_k}$ restores unit variance regardless of dimension. Vaswani et al. give exactly this argument in a footnote.

The problem it prevents is in the **backward** pass, and being precise about that is the point of the question. Softmax is not scale-invariant: multiply all its inputs by a constant and the output distribution sharpens. At $d_k = 256$ the scores have standard deviation 16, so the largest of fifty scores is typically many standard deviations above the rest and the softmax output is essentially one-hot. That is not obviously bad in the forward pass — a confident attention distribution might even seem desirable. But the Jacobian of the softmax is $\mathrm{diag}(\mathbf{p}) - \mathbf{p}\mathbf{p}^\top$, which vanishes as $\mathbf{p}$ approaches a one-hot vector. **A saturated softmax passes no gradient**, so the attention pattern is frozen at whatever the random initialization happened to produce and can never be learned. This is the sigmoid saturation problem from [Module 03](../../03-feedforward-networks-and-activations.md) wearing a different hat, and the measurements below show it costs three orders of magnitude of gradient.

**3. Why $W^Q$ and $W^K$ stay separate.**

The observation is correct: the attention score is $\mathbf{x}_i^\top (W^Q)^\top W^K \mathbf{x}_j$, which depends only on the product $M = (W^Q)^\top W^K$, a $d_{\text{model}} \times d_{\text{model}}$ matrix. In principle a single $M$ would express exactly the same set of score functions.

Three reasons not to.

**Parameters and computation.** With $d_{\text{model}} = 512$ and $d_k = 64$, the factored form costs $2 \times 512 \times 64 = 65{,}536$ parameters; a full $M$ costs $512^2 = 262{,}144$, four times more. The factorization is a deliberate **low-rank constraint** — $M$ is restricted to rank at most $d_k$ — and that constraint is a regularizer as well as a saving. More importantly, computing scores as $QK^\top$ costs $O(T d_{\text{model}} d_k + T^2 d_k)$, while forming $\mathbf{x}^\top M \mathbf{x}$ for all pairs costs $O(T^2 d_{\text{model}})$, which is eight times more per pair at these dimensions.

**Multi-head structure.** Each head needs its own low-dimensional subspace. The factored form gives each head its own $W^Q_h$ and $W^K_h$ projecting into a $d_k$-dimensional space; a single monolithic $M$ per head would multiply the parameter count by $d_{\text{model}}/d_k$ and lose the explicit subspace interpretation.

**Cross-attention breaks the symmetry entirely.** In an encoder–decoder, queries come from the decoder and keys from the encoder — different sequences, potentially different modalities and different dimensionalities. There is no single "the input" for $M$ to act on, so the factored form is not an optimization there but a necessity.

**4. Multi-head cost and benefit.**

Single-head attention with $d_{\text{model}} = 512$ uses $W^Q, W^K, W^V \in \mathbb{R}^{512\times512}$. Multi-head with $h = 8$ uses per-head projections of size $512 \times 64$ where $64 = 512/8$; across eight heads that is $8 \times 512 \times 64 = 512 \times 512$ per projection type — identical. The concatenated head outputs total $8 \times 64 = 512$ dimensions, matching the output projection's input width. So the parameter count and the FLOP count are the same; the heads partition an existing budget rather than adding to it.

What they buy is the ability to attend to **several things at once**. A single softmax produces one probability distribution per query, so a single head must choose: attend to the syntactic subject, or to the coreferent pronoun, or to the nearby modifier. Averaging over all three gives a blurred result that is none of them. Eight heads compute eight independent distributions in parallel and concatenate the results, so the position can retrieve syntactic agreement information *and* coreference information *and* local context simultaneously, and the output projection learns how to combine them.

Put differently, the constraint being relaxed is not capacity but the **single-distribution bottleneck of the softmax**. This is why interpretability work finds heads that specialize — a head tracking previous-token identity, a head tracking matching brackets, an "induction head" completing repeated patterns. The specialization is emergent, not designed, and it is what you would expect if the heads are competing for a limited number of retrieval slots.

**5. Permutation equivariance and position.**

Self-attention computes each output as a weighted sum over all positions, where the weights depend only on the *content* of the vectors involved. Permute the input sequence and every score is computed between the same pairs of vectors, so the outputs are the same set, permuted identically. Formally, $\text{Attn}(P\mathbf{X}) = P\,\text{Attn}(\mathbf{X})$ for any permutation matrix $P$: the operation is permutation-equivariant.

That is fatal for language, because "the dog bit the man" and "the man bit the dog" are the same multiset of tokens. Without position information, a Transformer is a set-processing model and cannot represent word order at all. Recurrence encoded order implicitly, in the sequential structure of the computation; deleting the recurrence deletes the order along with it, so it must be added back explicitly.

**Sinusoidal** encodings add fixed $\sin/\cos$ waves at geometrically spaced frequencies. They are parameter-free, defined for any position however large, and have the property verified below that $P_i \cdot P_{i+k}$ depends only on $k$ — so relative position is linearly recoverable from absolute encodings. Vaswani et al. hoped this would extrapolate to longer sequences than trained on; in practice it does so only weakly, because the model still has to learn to *use* the encoding and it only ever practised on the range it saw.

**Learned** encodings make position an ordinary embedding table. Simpler, marginally better in-distribution, and used by BERT and GPT-2. They cannot extrapolate *at all* — position 2049 has no row in a table of 2048 — which is a hard architectural limit rather than a degradation.

**Rotary (RoPE)** encodings rotate the query and key vectors by an angle proportional to position, so the dot product between positions $i$ and $j$ depends on their *difference* by construction rather than by learned approximation. Relative position is baked into the algebra, nothing is added to the residual stream, and extrapolation is substantially better — with interpolation tricks, RoPE models are routinely extended to context lengths far beyond training. This is why essentially every recent large language model uses it.

**6. Pre-norm versus post-norm.**

Post-norm, the original: $\mathbf{x} \leftarrow \text{LayerNorm}(\mathbf{x} + \text{Sublayer}(\mathbf{x}))$. Pre-norm, used now: $\mathbf{x} \leftarrow \mathbf{x} + \text{Sublayer}(\text{LayerNorm}(\mathbf{x}))$.

The difference is whether the normalization sits **inside** the residual branch or **on** the residual path. In pre-norm the residual stream from input to output is a clean sum of sublayer contributions with nothing applied to it, so the identity path of [Module 08](../../08-initialization-and-normalization.md) is intact and the gradient reaches every layer undiminished. In post-norm every residual addition is immediately followed by a LayerNorm, which rescales the sum and breaks the clean path — the gradient must pass through $L$ normalization Jacobians on its way back.

What pre-norm buys is **trainability without warmup**. Post-norm Transformers are notoriously sensitive at the start of training and essentially require a learning-rate warmup schedule; Xiong et al. showed the expected gradient at initialization in a post-norm network scales badly with depth, while pre-norm is well-behaved, and that pre-norm trains stably without warmup. As depth grew past the original six layers this stopped being a convenience and became a requirement, which is why GPT-2 onward, and effectively all modern large models, are pre-norm.

The cost is a small, consistent quality gap in favour of post-norm when post-norm can be trained at all, which is why some careful implementations use hybrid schemes. But for anything deep, pre-norm is the default, and the usual accompaniment is a final LayerNorm after the last block — because otherwise the residual stream reaching the output head has never been normalized.

## Part B — Reference solutions

### Scaled dot-product attention

```python
import torch, torch.nn.functional as F, math
torch.set_default_dtype(torch.float64); torch.manual_seed(0)

def sdpa(q, k, v, causal=False):
    d_k = q.size(-1)
    scores = q @ k.transpose(-2, -1) / math.sqrt(d_k)
    if causal:
        T = q.size(-2)
        mask = torch.triu(torch.ones(T, T, dtype=torch.bool), diagonal=1)
        scores = scores.masked_fill(mask, float("-inf"))
    return scores.softmax(-1) @ v

q, k, v = (torch.randn(2, 4, 6, 8) for _ in range(3))
print((sdpa(q, k, v)       - F.scaled_dot_product_attention(q, k, v)).abs().max())
print((sdpa(q, k, v, True) - F.scaled_dot_product_attention(q, k, v, is_causal=True)).abs().max())
```
```
unmasked: 6.7e-16
causal:   4.4e-16
```

Four lines, and the causal mask is one more. Everything in this module is built out of this function; the rest is projections, reshaping and residual connections.

The mask is worth a moment. Setting scores to $-\infty$ before the softmax makes those weights exactly zero afterwards, because $e^{-\infty} = 0$ — masking *before* the normalization rather than zeroing after it, which matters because zeroing after would leave the remaining weights not summing to 1. And note what the mask buys beyond correctness: with it, a single forward pass over a length-$T$ sequence produces the training signal for all $T$ next-token predictions at once, because position $i$'s output legitimately depends only on positions $\le i$. Without it you would need $T$ separate forward passes. Causal masking is not a restriction on the Transformer, it is the thing that makes decoder training parallel.

In production, call `F.scaled_dot_product_attention` rather than your own: PyTorch dispatches it to FlashAttention when the inputs qualify, which computes the same result without ever materializing the $T \times T$ score matrix — turning attention's memory cost from quadratic to linear in sequence length.

### The variance claim and the saturation table

Empirical variance of $\mathbf{q}\cdot\mathbf{k}$ over 20,000 independent pairs:

| $d_k$ | $\mathrm{Var}(\mathbf{q}\cdot\mathbf{k})$ | after dividing by $\sqrt{d_k}$ |
| --- | --- | --- |
| 4 | 3.98 | 0.99 |
| 16 | 16.07 | 1.00 |
| 64 | 64.25 | 1.00 |
| 256 | 253.06 | 0.99 |

The variance tracks $d_k$ to within sampling error, and the scaling flattens it to 1 at every dimension. Theory confirmed to two digits.

Now the consequence. One query against 50 keys, reporting the largest attention weight, the entropy of the distribution (maximum possible $\ln 50 = 3.912$), and the norm of the gradient flowing back through the softmax:

| $d_k$ | | max weight | entropy | gradient norm |
| --- | --- | --- | --- | --- |
| 16 | unscaled | 0.607 | 1.584 | $2.6\times10^{-1}$ |
| 16 | scaled | 0.095 | 3.524 | $8.7\times10^{-2}$ |
| 64 | unscaled | 0.682 | 0.872 | $2.6\times10^{-1}$ |
| 64 | scaled | 0.119 | 3.372 | $1.1\times10^{-1}$ |
| 256 | unscaled | **0.9999** | **0.001** | $1.2\times10^{-4}$ |
| 256 | scaled | 0.112 | 3.476 | $1.0\times10^{-1}$ |

The last two rows are the argument. At $d_k = 256$ without scaling, one key receives 99.99% of the attention and the entropy is 0.001 against a maximum of 3.912 — the distribution is one-hot to four decimal places, at initialization, before any learning. The gradient norm is $1.2\times10^{-4}$, **roughly a thousand times smaller** than the scaled version's $1.0\times10^{-1}$.

Understand what that means. The attention pattern at initialization is determined entirely by random projections, and it is frozen there, because no gradient reaches the parameters that would change it. The model is not attending badly; it is attending arbitrarily and permanently. Meanwhile the scaled rows show entropy near the 3.912 maximum at every dimension — a nearly uniform distribution that is maximally *plastic*, ready to be shaped by training.

Notice also that unscaled attention is fine at $d_k = 16$ and catastrophic at $d_k = 256$. That is the danger: the bug is invisible on small toy models and appears only when you scale up, which is exactly when it is most expensive to diagnose. A one-character division is the difference.

### Multi-head attention from scratch

```python
B, T, E, H = 2, 5, 8, 4
mha = nn.MultiheadAttention(E, H, batch_first=True)
x = torch.randn(B, T, E)
ref, _ = mha(x, x, x)

# in_proj_weight is (3E, E), stacked as [W_q ; W_k ; W_v]
qkv = x @ mha.in_proj_weight.T + mha.in_proj_bias
q, k, v = qkv.chunk(3, dim=-1)

d = E // H
split = lambda t: t.view(B, T, H, d).transpose(1, 2)      # (B, H, T, d)
q, k, v = split(q), split(k), split(v)

att = (q @ k.transpose(-2, -1) / math.sqrt(d)).softmax(-1) @ v
out = att.transpose(1, 2).reshape(B, T, E) @ mha.out_proj.weight.T + mha.out_proj.bias

print((out - ref).abs().max())        # 1.1e-16
```

Machine epsilon, so the implementation is exactly PyTorch's.

The reshaping is the only real content. `view(B, T, H, d)` splits the $E$-dimensional vector into $H$ contiguous chunks of size $d$ — head $i$ owns dimensions $[id, (i{+}1)d)$ — and `transpose(1, 2)` moves the head axis next to the batch axis so that the subsequent matmuls treat $(B, H)$ as independent batch dimensions and compute all heads in parallel. Undoing it afterwards with `transpose(1,2).reshape(B, T, E)` concatenates the heads back. There is no loop over heads anywhere, which is why multi-head attention costs the same as single-head in wall-clock time as well as in FLOPs.

The trap is `in_proj_weight`. PyTorch fuses the three projections into one $(3E, E)$ matrix so it can compute $Q$, $K$ and $V$ with a single GEMM, and the stacking order is $[W^Q; W^K; W^V]$. If you assume a different order you get a plausible-looking model that silently computes the wrong thing.

### Positional encoding properties

```python
def positional_encoding(T, D):
    pos = torch.arange(T).unsqueeze(1).float()
    div = torch.exp(torch.arange(0, D, 2).float() * (-math.log(10000.0) / D))
    P = torch.zeros(T, D)
    P[:, 0::2] = torch.sin(pos * div)
    P[:, 1::2] = torch.cos(pos * div)
    return P
```
```
norms (all equal):  5.657, 5.657, 5.657, 5.657        (= sqrt(32) for D=64)

dot products:
  k    P[20]·P[20+k]    P[120]·P[120+k]
  0        32.000            32.000
  1        30.917            30.917
  2        28.304            28.304
  5        23.504            23.504
 10        21.052            21.052
 50        15.674            15.674
```

Both properties hold exactly. Every position vector has norm $\sqrt{D/2}$, because each of the $D/2$ frequency pairs contributes $\sin^2 + \cos^2 = 1$ — so no position is intrinsically "louder" than another, and adding the encoding to the token embedding perturbs every position by the same magnitude.

The second column is the important one. The dot product between positions 20 and 25 is *identical* to the dot product between positions 120 and 125, to three decimal places. **The encoding is translation-invariant in the dot product**, which is precisely the operation attention performs. That is what lets a model learn relative-position behaviour — "attend three tokens back" — from an encoding that only ever expresses absolute position. The mechanism is the angle-addition formula: $P_{i+k}$ is a fixed linear (rotation) function of $P_i$ depending only on $k$, so their inner product cannot depend on $i$.

Note also that the dot product decays with $k$ — 32.0, 30.9, 28.3, 23.5, 21.1, 15.7 — giving a built-in locality prior. Nearby positions have more similar encodings, so before learning anything the model has a mild bias toward attending locally, which is a reasonable prior for language.

### The character-level GPT

Decoder-only, 4 pre-norm blocks, 4 heads, $d = 128$, context 128 characters, weight tying, dropout 0.1, AdamW with one-cycle to $10^{-3}$, 2,000 steps on tiny Shakespeare. Vocabulary is 65 characters; 818,113 parameters.

```python
class Block(nn.Module):
    def __init__(self):
        super().__init__()
        self.ln1 = nn.LayerNorm(D)
        self.attn = nn.MultiheadAttention(D, HEADS, batch_first=True, dropout=0.1)
        self.ln2 = nn.LayerNorm(D)
        self.ff = nn.Sequential(nn.Linear(D, 4*D), nn.GELU(),
                                nn.Linear(4*D, D), nn.Dropout(0.1))
    def forward(self, x, mask):                       # pre-norm
        h = self.ln1(x)
        a, _ = self.attn(h, h, h, attn_mask=mask, need_weights=False)
        x = x + a
        return x + self.ff(self.ln2(x))

class GPT(nn.Module):
    def __init__(self):
        super().__init__()
        self.tok = nn.Embedding(V, D); self.pos = nn.Embedding(BLOCK, D)
        self.blocks = nn.ModuleList([Block() for _ in range(LAYERS)])
        self.lnf = nn.LayerNorm(D); self.head = nn.Linear(D, V)
        self.head.weight = self.tok.weight            # weight tying
        self.apply(self._init)                        # <-- see below

    @staticmethod
    def _init(mod):
        if isinstance(mod, (nn.Linear, nn.Embedding)):
            nn.init.normal_(mod.weight, 0.0, 0.02)
            if isinstance(mod, nn.Linear) and mod.bias is not None:
                nn.init.zeros_(mod.bias)

    def forward(self, idx, targets=None):
        T = idx.size(1)
        mask = torch.triu(torch.full((T, T), float("-inf"), device=idx.device), 1)
        x = self.tok(idx) + self.pos(torch.arange(T, device=idx.device))
        for b in self.blocks:
            x = b(x, mask)
        logits = self.head(self.lnf(x))
        loss = None if targets is None else F.cross_entropy(logits.view(-1, V), targets.view(-1))
        return logits, loss
```

**The failed sanity check, and what it was worth.** Written the obvious way — without the `_init` call — the model reports an initial loss of **81.87** against a predicted $\ln 65 = 4.174$. That is the [Module 09](../../09-practical-training-and-debugging.md) check failing by a factor of twenty, and it is the single most useful fifteen seconds in this exercise.

The cause is weight tying interacting with the default embedding initialization. `nn.Embedding` initializes from $\mathcal{N}(0,1)$, and tying makes that same tensor the output projection. So the logits are dot products of unit-scale 128-dimensional vectors, with standard deviation around $\sqrt{128} \approx 11$ — enormously over-confident predictions, hence a huge cross-entropy. It is the $\sqrt{d_k}$ problem again, in a different place.

The fix is GPT-2's initialization: every `Linear` and `Embedding` weight drawn from $\mathcal{N}(0, 0.02^2)$.

| | initial loss | val loss @ 500 | @ 1000 | @ 2000 |
| --- | --- | --- | --- | --- |
| default init | 81.87 | 2.5442 | 2.1281 | 1.9202 |
| $\sigma = 0.02$ init | **4.1601** | 2.1459 | 1.7552 | **1.5842** |

The initial loss goes to 4.1601 against the predicted 4.1744 — agreement to within sampling noise, which is what a passing sanity check looks like. And the final validation loss improves by 0.34 nats, from 1.9202 to 1.5842, purely from a three-line initialization change with everything else identical. In perplexity terms that is $e^{1.92} = 6.8$ against $e^{1.58} = 4.9$, a 28% reduction. The model wasted a large part of its 2,000-step budget climbing back down from a terrible starting point, and never fully recovered.

The samples show the difference too. Both runs produce recognizable Shakespearean *form* — character names in capitals followed by colons, line breaks, dialogue structure — learned purely from characters with no notion of a word. The better-initialized model produces more real words and more nearly-grammatical clauses:

```
MENENIUS:
Now, when I see thee house at thought of the will sorrow
And with of the vierce proclaims.

DUCHESS OF YORK:
O fieuld him, come, sir, my lord desire.

KING RICHARD III:
There would servic his not children a see: now the
more of world comes that I was; ...
```

818,113 parameters, 53 seconds on a laptop GPU, no pretrained anything. It is a real, if very small, GPT: the architecture is the same one that scales to hundreds of billions of parameters, and what separates this from a frontier model is data, compute and engineering — not a different idea. That is worth sitting with, because it is the honest summary of where the field is.

Two further details in the code deserve naming. **Weight tying** shares the token embedding with the output projection, which saves $V \times D$ parameters and reflects a real symmetry: the vector representing a token as input should relate to the vector scoring it as output. **Gradient clipping at 1.0** and the one-cycle schedule are both from [Module 06](../../06-optimization.md) and [Module 09](../../09-practical-training-and-debugging.md), and neither is optional at this scale — remove the clipping and a single unlucky batch can spike the loss badly enough to lose a hundred steps of progress.

---

Back to [Set 12](../12-exercises.md) · Next solutions: [Set 13](./13-solutions.md)
