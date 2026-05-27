# MNIST Neural Network — Learning Project Plan

> A reference document for resuming this project across sessions. Share this with any AI assistant at the start of a new conversation to get them up to speed.

## About this document

This is the master plan for my first hands-on neural network project. It exists so I can pause for days or weeks and pick up where I left off — and so an AI assistant joining a new conversation can immediately understand the context, my background, my goals, and where I am in the journey.

**How to use it:** At the start of a new chat, paste or attach this document and say something like *"I'm working on this learning project. I'm on Step X. Help me with [specific question]."* Update the "Progress log" section at the bottom as you go.

---

## Who I am (the learner)

- **Background:** Principal engineer with primary expertise in C#, web services, and distributed systems.
- **ML experience:** Just completed Andrej Karpathy's *Neural Networks: Zero to Hero* video series. I understand (at a conceptual and small-code level): backpropagation, gradients, a basic MLP, character-level language models (makemore), and the building blocks of a small transformer (nanoGPT).
- **What I am NOT:** A data scientist, an ML researcher, or someone who has shipped production ML systems. I have not built a real training loop in PyTorch on real data before this project.
- **Learning style:** I'm a seasoned engineer, so I prefer being told the "why" alongside the "how." I'm comfortable with code, debugging, and reading docs. I don't need hand-holding on Python syntax or general software engineering. I DO want help with ML-specific intuitions, idioms, and pitfalls.
- **Motivation:** Mostly for fun and to deepen my understanding of how modern AI/LLMs actually work under the hood.

---

## What we're building

A handwritten digit classifier trained on the **MNIST dataset** (70,000 small grayscale images of digits 0–9). The model takes a 28×28 pixel image as input and outputs which digit it represents.

This is the canonical "hello world" of neural networks. It's deliberately a solved problem so I can focus on learning the workflow, not on chasing state-of-the-art results.

---

## Why this project (and not something else)

We considered several first projects, including a "neural calculator" that learns arithmetic from examples. We chose MNIST because:

1. **Fast feedback loop.** The dataset is small (~60k training examples), it downloads in seconds, and trains in under a minute on a CPU. Tight iteration loops are the single biggest factor in learning speed.
2. **It exercises every concept from Karpathy's series in a new domain** — forward pass, loss, backprop, gradient descent — while introducing real-world idioms (DataLoader, batches, train/test splits, accuracy metrics) that Karpathy's micrograd/makemore skipped over.
3. **It teaches the engineering discipline ML projects need:** separating train/eval, tracking loss curves, recognizing overfitting, train/eval mode distinction. These habits transfer to every future ML project.
4. **Standard enough to find help easily.** When stuck, the answer is one search away because everyone has done this.
5. **Real enough to feel like ML.** Unlike toy regression problems or the Iris dataset, MNIST involves real images and a real (if classic) classification task.

The "neural calculator" idea was rejected for the first project because addition turns out to be surprisingly hard for transformers to generalize — a great lesson, but a frustrating first experience.

---

## The mental model for ML projects

Almost every supervised learning project follows the same skeleton. Internalizing this shape makes every future project easier:

1. **Get the data and look at it.** Actually look. Plot examples. Know what you're working with.
2. **Build the dumbest possible end-to-end pipeline.** Data → model → loss → training loop → evaluation. Make it work *badly*, but make it work *all the way through*.
3. **Establish a baseline.** The simplest model possible. You need a number to beat.
4. **Iterate.** Change one thing at a time, measure, keep what helps, discard what doesn't.
5. **Stop when you've learned what you wanted to learn** — not when the model is "perfect." It never is.

This is the loop. Once you've internalized it, you can attack any supervised learning problem.

---

## Step-by-step plan

Each step is intended as roughly one 30–90 minute session. Don't rush; the goal is understanding, not completion.

### Step 1 — Environment setup
- Install Python 3.x if not already present.
- Install PyTorch and dependencies: `pip install torch torchvision matplotlib`
- Verify with: `python -c "import torch; print(torch.__version__)"`
- (Optional) If you have a GPU: `python -c "import torch; print(torch.cuda.is_available())"`. CPU is fine for MNIST.

**Goal:** You can run Python and import torch without errors.

### Step 2 — Load the data and look at it
- Use `torchvision.datasets.MNIST` to download the dataset.
- Plot 25 random images with their labels using matplotlib. Confirm the labels match what you see.
- Print the shape of a single batch (should be `[batch_size, 1, 28, 28]`).
- Print min/max pixel values (should be ~0 to ~1 after `ToTensor()`).
- Note the dataset sizes: 60,000 training and 10,000 test images.

**Goal:** You can describe the data out loud without looking it up: *"60k training and 10k test 28×28 grayscale images of handwritten digits, pixel values normalized to roughly 0–1, with integer labels 0–9."*

### Step 3 — Build the dumbest end-to-end pipeline
- Implement a single linear layer model (logistic regression): `nn.Linear(784, 10)`.
- Write the full training loop: get batch → flatten → forward → loss → backward → optimizer step.
- Use `nn.CrossEntropyLoss` and `torch.optim.SGD(lr=0.01)`.
- Run for one epoch. Print loss every 100 batches. **Don't worry about accuracy yet.**

**Goal:** Loss goes down over the course of training. That's the only goal.

### Step 4 — Add evaluation
- Write an `evaluate()` function that runs the model on the test set with `model.eval()` and `torch.no_grad()`, and returns accuracy.
- Run it. Expect roughly **90–92%** accuracy with the simple linear model.

**Goal:** You can answer "how good is my model?" with a single number.

### Step 5 — Upgrade to an MLP
- Replace the single linear layer with: `Linear(784, 128) → ReLU → Linear(128, 10)`.
- Retrain for a few epochs.
- Expect **~97%** accuracy.

**Goal:** Feel firsthand that depth + nonlinearity helps.

### Step 6 — Experiment (the real learning step)
Change **one thing at a time** and observe. Suggested experiments:
- Train for more epochs. Does accuracy plateau? When?
- Try hidden layer sizes: 32, 256, 512. Bigger isn't always better.
- Add a second hidden layer.
- Swap SGD for Adam. Try different learning rates: 0.001, 0.01, 0.1, 1.0. What happens at the high end?
- Add `nn.Dropout(0.5)` between layers. Does it help, hurt, or do nothing?
- Plot training loss AND test accuracy together over epochs. Can you spot overfitting (training keeps improving while test stalls or drops)?

**Goal:** Build real intuition about what each knob actually does. Take notes — this is the most valuable step.

### Step 7 (optional stretch) — Convolutional network
- Replace the MLP with a small CNN: two conv layers + pooling + a small linear head.
- Expect **~99%** accuracy.
- This is a bigger conceptual jump — understand what convolutions and pooling do before coding.

**Goal:** See why CNNs dominated computer vision for a decade.

---

## Glossary (quick reference)

- **Epoch** — One full pass through the training dataset.
- **Batch** — A small group of examples (e.g. 64) processed together in one forward/backward pass.
- **Loss / Cost** — A single number measuring how wrong the model's predictions are. We minimize it.
- **Logits** — The raw, unnormalized outputs of the final layer (before softmax). `CrossEntropyLoss` expects logits, not probabilities.
- **Softmax** — Function that turns logits into a probability distribution over classes.
- **Cross-entropy loss** — Standard loss for classification. In PyTorch, `nn.CrossEntropyLoss` combines softmax + negative log likelihood internally.
- **ReLU** — `max(0, x)`. The default nonlinearity.
- **Optimizer** — Algorithm that updates weights using gradients. SGD is basic; Adam is the common default.
- **Learning rate** — How large a step the optimizer takes. Too high → diverges. Too low → trains forever.
- **Overfitting** — Model memorizes training data; test performance suffers. Detect by gap between train and test accuracy.
- **Dropout** — Training-time regularization that randomly zeros activations. Reduces overfitting.
- **Train/eval mode** — `model.train()` enables dropout/batchnorm-train behavior; `model.eval()` disables them. **Always switch correctly.**
- **`torch.no_grad()`** — Context manager that disables gradient tracking. Use during evaluation for speed/memory.
- **DataLoader** — PyTorch utility that yields shuffled, batched data from a Dataset.

---

## Common pitfalls (read before debugging)

These will save hours:

- **Loss is stuck at ~2.3.** That's `ln(10)` — the model is outputting a uniform distribution over 10 classes. Usually means: learning rate too low, gradients not flowing, or you forgot `optimizer.zero_grad()`.
- **Loss goes to NaN.** Usually learning rate too high. Try dividing by 10.
- **Test accuracy way lower than train accuracy.** Overfitting. Try fewer epochs, smaller model, or add dropout.
- **Forgot `optimizer.zero_grad()`.** Gradients accumulate across batches. Symptom: loss behaves erratically.
- **Forgot `model.eval()` during evaluation.** Dropout is still active during eval → noisy, inconsistent accuracy.
- **Wrong tensor shape.** PyTorch errors almost always mean shape mismatch. When confused, print `.shape` on everything.
- **Applied softmax before `CrossEntropyLoss`.** It expects raw logits. Doing softmax first will train, but poorly.
- **Didn't shuffle training data.** Set `shuffle=True` on the training DataLoader. Don't shuffle the test loader.

---

## Working principles

A few principles to internalize:

1. **Start with the absolute minimum that runs end-to-end, then add complexity only when motivated by a specific question.** Don't add dropout because tutorials do. Add it because you noticed overfitting and want to see if it helps.
2. **Change one thing at a time.** Otherwise you can't tell what helped.
3. **Always have a baseline.** "97% accuracy" is meaningless until you know the simple linear model gets 92%.
4. **Look at your data and your mistakes.** Visualize misclassified examples — they often reveal what the model is missing.
5. **Type code yourself, don't copy-paste tutorials.** The friction is the point.

---

## Reference materials

- **PyTorch official MNIST example:** https://github.com/pytorch/examples/tree/main/mnist — a known-good reference. Try to solve problems yourself first; consult after.
- **PyTorch docs:** https://pytorch.org/docs/stable/ — especially `nn`, `optim`, `utils.data`.
- **Karpathy's series (for re-grounding fundamentals):** https://karpathy.ai/zero-to-hero.html

---

## Progress log

Update this section as you go. It's the single most useful thing for resuming work.

| Date | Step completed | Notes / observations / things to come back to |
|------|---------------|------------------------------------------------|
| 5/26/2026 | Step 1   | Created simple install.ps1 script              |
| 5/26/2026 | Step 2   | Created run script                             |
| 5/27/2026 | Step 3   | Starting loss=2.310, after 1 epoch loss=0.653  |
|      | Step 4        |                                                |
|      | Step 5        |                                                |
|      | Step 6        |                                                |
|      | Step 7        |                                                |

---

## Next session prompt (template)

When resuming with an AI assistant, try something like:

> I'm working on the project described in the attached document. I've completed through Step X. My last loss was Y and test accuracy was Z. Right now I'm trying to [specific thing] and [specific question / problem]. Here's my current code: [paste code].

Specific beats general. The more concrete you are, the more useful the help.
