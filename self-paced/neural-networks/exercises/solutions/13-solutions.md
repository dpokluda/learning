# Solutions — Set 13

Worked answers for [Exercise Set 13](../13-exercises.md).

## Part A — Answers

**1. Two causes of degrading transferability.**

The first is **specificity**: as you go deeper, features become specialized to the pretraining task. Early layers compute edges and colour opponency, which any vision task needs. Late layers compute something close to templates for the specific classes the model was trained to separate — for ImageNet, that includes distinguishing 120 breeds of dog. Those distinctions are irrelevant to a different task, so the representation is worse for it.

The second is **co-adaptation**: adjacent layers in a trained network are jointly optimized, and their weights encode assumptions about each other that are not visible in either layer alone. Cutting the network at layer $k$ and attaching a fresh classifier severs a set of relationships that were doing real work. Yosinski et al. demonstrated this cleanly by transferring the first $k$ layers back to *the same task they were trained on* — where specificity cannot be the explanation, since the task is identical — and still observing a performance drop when those layers were frozen. The drop was largest in the middle of the network, exactly where you would expect the co-adaptation to be most intricate.

The distinction matters because they have different remedies. Co-adaptation damage is repaired by **fine-tuning**: unfreeze the transferred layers and the network re-establishes the broken relationships, and Yosinski found this recovers the loss entirely. Specificity is not repaired by longer training in the same way — the features are optimized for the wrong distinctions, and while fine-tuning can move them, you are effectively retraining rather than transferring. This is why the practical advice is to cut earlier when the target task is distant: you are trading away specificity you cannot use, and the co-adaptation you break will be restored by fine-tuning anyway.

**2. 300 examples.**

Argue for linear probing. Fine-tuning ResNet-18 means optimizing 11.2 million parameters against 300 examples, roughly 37,000 parameters per example. The model can memorize the training set completely — this is the [Set 07](./07-solutions.md) random-label result — and it will, because nothing in the optimization prefers a solution that generalizes. Worse, the large steps required to move that many parameters in a few hundred updates will damage the pretrained features before the randomly-initialized head has produced a useful gradient direction, which is catastrophic forgetting: you end up *below* the probe.

Linear probing trains 5,130 parameters (for ten classes off a 512-dimensional feature), about 17 per example. The features are fixed and were learned from 1.2 million images, so there is nothing to overfit except the final decision boundary. It also runs in seconds, which means you can afford proper cross-validation on 300 examples — itself worth more than the marginal accuracy fine-tuning might offer.

What would change my mind is **domain distance**. If the target images are unlike ImageNet photographs — medical scans, radar returns, microscopy, spectrograms rendered as images — the frozen features may simply be wrong for the domain, and a probe on wrong features has a low ceiling regardless of how well-regularized it is. The tell is a probe that plateaus at disappointing accuracy while its *training* accuracy is also mediocre: that is underfitting, not overfitting, and it means the representation lacks the information you need. The response is a middle path rather than full fine-tuning — unfreeze only the last block or two, use discriminative learning rates, add strong augmentation, and consider cutting at an earlier layer where the features are more generic. Parameter-efficient methods like LoRA are also designed exactly for this regime.

**3. What `requires_grad = False` misses.**

It stops gradients from being computed for the tensors registered as **parameters**. BatchNorm's `running_mean` and `running_var` are registered as **buffers**, not parameters, and they are not updated by gradient descent at all — they are updated by a forward pass in `train()` mode, as an exponential moving average of the batch statistics. `requires_grad` is irrelevant to them.

So a "frozen" backbone left in `train()` mode will have its normalization statistics progressively overwritten by your target data's statistics while its weights stay fixed. The result is a representation that drifts in a way you did not intend and cannot see in the parameter count, and it commonly presents as validation accuracy that degrades over epochs for no visible reason.

Two fixes. The blunt one is to call `.eval()` on the frozen module and keep it there, re-calling it after any `model.train()` that would flip it back:

```python
model.train()
backbone.eval()          # order matters: model.train() would have flipped it
```

The surgical one is to set `track_running_stats = False` or to freeze the BatchNorm modules explicitly:

```python
for m in backbone.modules():
    if isinstance(m, nn.BatchNorm2d):
        m.eval()
        for p in m.parameters():
            p.requires_grad = False
```

The same class of problem is why the [Module 13](../../13-transfer-learning-and-embeddings.md) checklist insists on passing only trainable parameters to the optimizer — AdamW decays parameters regardless of whether they have gradients, so a frozen backbone handed to AdamW slowly shrinks toward zero.

**4. LoRA's hypothesis.**

The hypothesis is about the **update**, not the model: that when a pretrained weight matrix $W_0$ is adapted to a new task, the change $\Delta W = W - W_0$ has low intrinsic rank, even though $W_0$ itself is full-rank. Adapting a model is a small, structured modification rather than an arbitrary one, so $\Delta W$ can be written as $BA$ with $B \in \mathbb{R}^{d\times r}$, $A \in \mathbb{R}^{r\times k}$, and $r \ll \min(d,k)$. Train $A$ and $B$; leave $W_0$ frozen.

$B$ is initialized to **zero** so that $BA = 0$ at the start, which makes the adapted model *exactly* the pretrained model. If both factors were random, the adapter would inject noise into a carefully trained network before the first gradient step, degrading it for no reason and requiring recovery. Zeroing one factor gives a guaranteed-identity starting point while still allowing gradients to flow — the gradient with respect to $B$ is nonzero because $A$ is not zero, so training moves off the identity immediately. Zeroing *both* would be a dead fixed point, exactly the symmetry problem of [Set 08](./08-solutions.md). This is the same trick as zero-initializing a residual block's final normalization scale, for the same reason.

Merging works because the adaptation is **linear and additive**. $W_0\mathbf{x} + BA\mathbf{x} = (W_0 + BA)\mathbf{x}$, so once training is finished you compute $W = W_0 + \frac{\alpha}{r}BA$ once, store it as an ordinary weight matrix, and discard $A$ and $B$. The deployed model is architecturally identical to the original — same layers, same shapes, same latency. That distinguishes LoRA from adapter methods that insert extra modules into the forward pass and therefore pay a permanent inference cost. It also means you can keep one base model in memory and swap merged or unmerged adapters per request, which is how multi-tenant LLM serving works in practice.

**5. One vector per word type.**

A static embedding table maps each vocabulary entry to a fixed vector, so every occurrence of "bank" gets the same representation. But "bank" in *river bank* and in *bank account* are different words that happen to share a spelling, and no single point in the space can be correct for both. The learned vector ends up at a frequency-weighted compromise between the senses — near neither, and carrying misleading similarity relations to both "river" and "money."

This is not fixable by more dimensions or more data, because the problem is the *type* of the function. A static embedding is a lookup $\text{word} \to \mathbb{R}^d$, and a lookup cannot depend on context by definition. You would need $\text{word} \times \text{context} \to \mathbb{R}^d$.

Self-attention provides exactly that. Because every position's output is a weighted combination of all positions' values, with weights computed from the content of the sequence, the representation of token $i$ is a function of the entire input. Feed "I sat on the river bank" and "I opened a bank account" through a Transformer encoder and the two occurrences of "bank" produce different vectors — the first pulled toward the river context, the second toward the financial one. Contextual embeddings are the direct architectural consequence of the mechanism in [Module 12](../../12-attention-and-transformers.md), and they are why BERT-style models displaced word2vec and GloVe for essentially every downstream task. The cost is that you can no longer precompute a table; you must run the model.

**6. Distillation's soft targets.**

A hard label is a one-hot vector: this image is a 7, and every other class is equally and absolutely wrong. The teacher's distribution says it is a 7 with probability 0.9, a 1 with probability 0.07, a 9 with probability 0.02, and everything else near zero. That ranking of the *wrong* answers is information — it encodes which classes the teacher has learned to consider similar, a piece of the teacher's learned structure that the one-hot label destroys entirely. Hinton called it the "dark knowledge" in the model. Each training example carries far more signal, which is why distillation is more sample-efficient than training on labels.

Temperature is needed because that information lives in very small numbers. At $T=1$ a well-trained teacher is confident, so the informative probabilities are $10^{-2}$ or smaller and contribute almost nothing to the cross-entropy gradient. Dividing the logits by $T$ before the softmax flattens the distribution, amplifying the small probabilities into a usable range; $T$ between 3 and 5 is typical. Because the gradient of the soft-target loss scales as $1/T^2$, the convention is to multiply that loss term by $T^2$ so it stays commensurate with the hard-label term when both are used.

What the result implies is the interesting part. The student architecture, trained on hard labels alone, reaches some accuracy; trained on the teacher's outputs, it reaches meaningfully higher accuracy — DistilBERT retains about 97% of BERT's GLUE performance at 40% of the size. The student's *capacity* did not change between those two runs. So the gap was never a representational limit: the student could always express a better function, and gradient descent on the labels simply failed to find it. Distillation is an **optimization** aid, not a capacity aid — the teacher's soft targets provide a smoother, more informative loss surface that guides the student to a solution it could reach but not discover. That is the same distinction that ran through [Set 08](./08-solutions.md) (the degradation problem) and [Set 11](./11-solutions.md) (the forget-gate bias), and by this point in the book it should be the first hypothesis you reach for when a model underperforms.

## Part B — Reference solutions

### Layer-wise transferability

```python
import torch, torch.nn as nn, torch.nn.functional as F, torchvision as tv
from torchvision import transforms as T
from torch.utils.data import DataLoader, Subset

tf = T.Compose([T.Resize(128), T.ToTensor(),
                T.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225])])
m = tv.models.resnet18(weights=tv.models.ResNet18_Weights.IMAGENET1K_V1).to(device).eval()

stages = {
    "layer1": nn.Sequential(m.conv1, m.bn1, m.relu, m.maxpool, m.layer1),
    "layer2": nn.Sequential(m.conv1, m.bn1, m.relu, m.maxpool, m.layer1, m.layer2),
    "layer3": nn.Sequential(m.conv1, m.bn1, m.relu, m.maxpool, m.layer1, m.layer2, m.layer3),
    "layer4": nn.Sequential(m.conv1, m.bn1, m.relu, m.maxpool, m.layer1, m.layer2, m.layer3, m.layer4),
}

@torch.no_grad()
def features(net, loader):
    X, Y = [], []
    for x, y in loader:
        f = net(x.to(device))
        X.append(F.adaptive_avg_pool2d(f, 1).flatten(1).cpu()); Y.append(y)
    return torch.cat(X), torch.cat(Y)
```

| representation | dimension | accuracy |
| --- | --- | --- |
| raw pixels | 49,152 | 26.80% |
| `layer1` | 64 | 54.00% |
| `layer2` | 128 | 69.25% |
| `layer3` | 256 | 79.30% |
| `layer4` | 512 | 79.55% |

The curve rises steeply and then flattens, and both halves are informative.

The rise from 26.8% to 79.3% comes entirely from **representation quality**, because the classifier is the same single linear layer in every row and the backbone has never seen a CIFAR-10 image. What changes is how the data is presented to that classifier: raw pixels are not linearly separable by class, and four residual stages of ImageNet-trained processing make them nearly so. Note also that the best representation is 96 times *smaller* than the pixel one — 512 numbers against 49,152 — so this is compression toward the task-relevant subspace, not enrichment.

The flattening from `layer3` to `layer4`, a gain of 0.25 points, is Yosinski's specificity effect appearing directly. `layer4` sits immediately below ImageNet's classification head, and its features are shaped to separate a thousand fine-grained categories. CIFAR-10 wants ten coarse ones. The additional specialization is aimed at distinctions we do not need, and it stops paying.

The practical reading: **cut earlier when your task is further from the pretraining task**. Here CIFAR-10 is natural images, so the penalty is a plateau rather than a decline; on a genuinely distant domain you would see `layer4` do measurably *worse* than `layer3`, and cutting at `layer2` or `layer3` would be the right call.

### The three-way comparison

| approach | trainable parameters | accuracy | time |
| --- | --- | --- | --- |
| from scratch | 11,181,642 | 46.60% | 17.1 s |
| linear probe | **5,130** | 74.50% | 5.1 s |
| fine-tune | 11,181,642 | **89.40%** | 13.6 s |

Training **5,130 parameters beats training 11.2 million by 28 points**, in a third of the time. That is the headline, and the reason is not subtle: the probe is not learning from 5,000 images, it is learning from 5,000 images *plus* the 1.2 million ImageNet images that shaped the features it consumes.

Fine-tuning adds another 15 points by letting the features themselves move. CIFAR-10 images are $32\times32$ upsampled to 128 — blurry, low-detail, differently framed from ImageNet photographs — and the frozen features are slightly mismatched to that domain. Unfreezing fixes the mismatch.

The learning-rate difference between the rows is essential rather than incidental. Scratch and probe use $10^{-3}$; fine-tuning uses $10^{-4}$. Fine-tuning at $10^{-3}$ typically lands *below* the linear probe, because the first few hundred large steps — driven by gradients from a randomly-initialized head that is producing nonsense — destroy the pretrained representation before anything useful is learned. That failure has a name, catastrophic forgetting, and two standard preventions: a lower learning rate, and a warmup epoch with the backbone frozen so the head is sensible before the backbone starts moving.

The size of the fine-tuning gain is itself a diagnostic. A small gain over the probe means the pretraining domain already matched yours. A large gain, as here, means it did not — and suggests that a model pretrained on something closer to your data would do better still.

### LoRA from scratch

```python
class LoRALinear(nn.Module):
    def __init__(self, base: nn.Linear, r=8, alpha=16):
        super().__init__()
        self.base, self.r, self.scale = base, r, alpha / r
        for p in self.base.parameters():
            p.requires_grad = False
        self.A = nn.Parameter(torch.randn(r, base.in_features) * 0.01)
        self.B = nn.Parameter(torch.zeros(base.out_features, r))

    def forward(self, x):
        return self.base(x) + F.linear(F.linear(x, self.A), self.B) * self.scale

    def merged(self):
        m = nn.Linear(self.base.in_features, self.base.out_features)
        with torch.no_grad():
            m.weight.copy_(self.base.weight + self.scale * (self.B @ self.A))
            m.bias.copy_(self.base.bias)
        return m
```
```
at init, ||LoRA(x) - base(x)||             = 0.0
after training, merged vs unmerged max diff = 2.6e-06   (float32 round-off)
trainable: full = 262,656   LoRA r=8 = 8,192   ratio 32.1x
```

Both properties verified. The initialization difference is **exactly zero** — not small, zero — because $B = 0$ makes the entire adapter term vanish identically. And the merged layer reproduces the adapted layer to float32 round-off, confirming that deployment costs nothing: you ship a plain `nn.Linear`.

Trainable parameters against rank, for a $512\times512$ layer:

| $r$ | LoRA parameters | reduction |
| --- | --- | --- |
| 1 | 1,024 | 256.5× |
| 2 | 2,048 | 128.2× |
| 4 | 4,096 | 64.1× |
| 8 | 8,192 | 32.1× |
| 16 | 16,384 | 16.0× |
| 64 | 65,536 | 4.0× |

The count is $r(d_{\text{in}} + d_{\text{out}})$ — linear in $r$ — against $d_{\text{in}}d_{\text{out}}$ for the full matrix. Ranks of 8 to 16 are the common choice, and Hu et al.'s striking empirical claim is that even $r = 1$ or $2$ often matches full fine-tuning on their benchmarks, which is strong evidence for the low-rank hypothesis.

Two things the parameter table understates. The memory saving is larger than $32\times$ in practice, because the frozen $W_0$ needs no optimizer state — with Adam that is two additional tensors per trainable parameter, so the optimizer-state saving alone is another factor of three. And the `scale = alpha / r` factor exists so that changing $r$ does not require retuning the learning rate: since $BA$'s magnitude grows with rank, dividing by $r$ keeps the update's scale roughly constant, and the convention $\alpha = 2r$ then makes the effective multiplier 2 regardless.

Applying this to real models means wrapping the attention projections, usually $W^Q$ and $W^V$ — Hu et al. found adapting those two gives the best return per parameter, and adapting the feedforward layers adds little.

### Embeddings for retrieval

No classifier is trained here at all. Extract 512-dimensional penultimate features, L2-normalize, and ask whether an image's nearest neighbours in that space share its class.

```python
backbone = nn.Sequential(*list(m.children())[:-1]).to(device).eval()   # drop the fc head

E, Y = embed(loader)                      # (2000, 512)
E = F.normalize(E, dim=1)
S = E @ E.T                               # cosine similarity, since rows are unit norm
S.fill_diagonal_(-2)                      # never retrieve the query itself

for k in (1, 5, 10):
    nbrs = S.topk(k, dim=1).indices
    print(k, (Y[nbrs] == Y.unsqueeze(1)).float().mean().item() * 100)
```
```
top-1  nearest-neighbour same-class rate: 71.50%     (chance 10%)
top-5                                     65.74%
top-10                                    63.02%
top-5  on raw pixels:                     25.25%
```

An image's single nearest neighbour among 2,000 CIFAR-10 images shares its class **71.5% of the time**, against a 10% chance baseline — with no training whatsoever, from a network that has never seen this dataset. The same procedure on raw pixels gives 25.25%, and what pixel similarity actually retrieves is images with similar colour histograms and backgrounds: a brown horse on grass matches a brown deer on grass.

This is the clearest demonstration in the book of what "learning a representation" means. The network was trained to classify ImageNet, and in doing so it built a coordinate system in which **semantic similarity became geometric proximity**. That property was not the training objective; it fell out of it, and it is reusable for tasks the model was never trained on.

Two details worth noting. Accuracy decreases with $k$ — 71.5%, 65.7%, 63.0% — which is expected, because the further you go from a query the more you leave its immediate neighbourhood; the decay is gentle, indicating classes occupy reasonably coherent regions rather than thin filaments. And cosine similarity is the right metric here rather than Euclidean distance, because feature magnitude carries confidence or salience information that is unhelpful for semantic comparison; normalizing first discards it and compares direction only. That convention holds across essentially all embedding applications, from semantic search to retrieval-augmented generation.

Everything that makes vector databases useful is in these six lines: embed a corpus once, embed a query, take a dot product, sort. The engineering in a production system is about doing that fast over a billion vectors, not about the idea.

---

Back to [Set 13](../13-exercises.md) · Next: [Capstone](../14-capstone.md)
