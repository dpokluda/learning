# Setup

You need very little to work through this book. Every exercise is sized to finish in minutes on free hardware, and nothing requires a GPU — though a few of the later ones (ResNet on CIFAR-10, the Transformer in Module 12) are noticeably more pleasant with one. There are two supported paths: Google Colab, which requires no installation at all, and a local virtual environment, which is better if you want your work to persist and your datasets to stay cached.

## Path A — Google Colab (zero installation)

Go to [colab.research.google.com](https://colab.research.google.com) and create a new notebook. PyTorch, torchvision, NumPy, and matplotlib are preinstalled, so the first cell you need is just a sanity check:

```python
import torch, torchvision
print("torch", torch.__version__)
print("torchvision", torchvision.__version__)
print("cuda available:", torch.cuda.is_available())
```

If `cuda available` prints `False` and you want a GPU, open **Runtime → Change runtime type → Hardware accelerator → T4 GPU** and rerun. The free tier is more than enough for everything here.

Two Colab-specific habits are worth forming early. First, Colab's filesystem is ephemeral — when the runtime disconnects, downloaded datasets and saved checkpoints vanish. For short exercises that is fine, since MNIST re-downloads in a few seconds. For the capstone, mount your Drive so checkpoints survive:

```python
from google.colab import drive
drive.mount('/content/drive')
DATA_DIR = '/content/drive/MyDrive/nn-course/data'
```

Second, the two extra packages this book uses occasionally are not preinstalled on every Colab image. Install them in the first cell when a module calls for them:

```python
!pip install -q datasets torchinfo
```

`datasets` is HuggingFace's dataset library, used from Module 11 onward for IMDB and other text corpora; `torchinfo` prints layer-by-layer shape and parameter summaries, which is genuinely useful when debugging architectures in Modules 09 and 10.

## Path B — Local installation

Python 3.10 or newer is required. Create an isolated environment so nothing here collides with your other work:

```bash
python3 -m venv .venv
source .venv/bin/activate          # Windows: .venv\Scripts\activate
python -m pip install --upgrade pip
```

Then install the packages. This is the complete list the book uses:

```bash
pip install torch torchvision numpy matplotlib tqdm datasets torchinfo scikit-learn
```

On Linux with an NVIDIA GPU you may want the CUDA build instead of the default wheel; the [official selector](https://pytorch.org/get-started/locally/) generates the exact command for your CUDA version. On an Apple Silicon Mac the default wheel already includes Metal (MPS) acceleration and no extra step is needed. On Windows or Linux without a GPU, the default wheel is CPU-only and correct.

Verify the installation:

```bash
python -c "import torch, torchvision, numpy, matplotlib; print(torch.__version__, torchvision.__version__)"
```

Each package earns its place. **torch** and **torchvision** are the core of every exercise. **numpy** appears mainly in Modules 02 and 05, where implementing things without autograd is the entire point. **matplotlib** draws the loss curves you will spend Modules 07 and 09 learning to read. **tqdm** gives training loops a progress bar, which matters more than it sounds when you are trying to tell a slow epoch from a hung one. **datasets** supplies text corpora from Module 11 onward. **torchinfo** summarizes model shapes and parameter counts. **scikit-learn** is used only for a handful of utilities — train/test splits, confusion matrices, t-SNE for the embedding visualizations in Module 13.

## Choosing a device

Write device-agnostic code from the first exercise and you will never have to retrofit it. The idiom used throughout this book is:

```python
def get_device():
    if torch.cuda.is_available():
        return torch.device("cuda")
    if torch.backends.mps.is_available():     # Apple Silicon
        return torch.device("mps")
    return torch.device("cpu")

device = get_device()
model = model.to(device)
# and inside the training loop:
#   images, labels = images.to(device), labels.to(device)
```

The rule that catches everyone once: the model and the data must be on the *same* device, or PyTorch raises a `RuntimeError` about expected device mismatch. Every reference solution in this book uses the helper above.

## Where the data goes

By default the exercises download to `./data`, which torchvision creates on first use. MNIST and FashionMNIST are about 10 MB each, CIFAR-10 is about 170 MB, and IMDB is about 80 MB. If you are working locally and want a single shared cache across all exercises, set an environment variable once and point `root=` at it:

```bash
export NN_COURSE_DATA=~/datasets
```

```python
import os
DATA_DIR = os.environ.get("NN_COURSE_DATA", "./data")
```

Add `data/` to your `.gitignore` if you keep your work in version control. Downloaded datasets should never be committed.

## Reproducibility

Neural network training is stochastic in several places at once — weight initialization, data shuffling, dropout masks — so two runs of identical code give slightly different numbers. That is normal and expected, and the accuracy figures quoted throughout this book should be read as "around this value," typically within a few tenths of a percent. When you want a run to be repeatable, seed everything:

```python
import random, numpy as np, torch

def set_seed(seed=0):
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.cuda.manual_seed_all(seed)
```

Be aware that seeding does not buy you bit-exact reproducibility across different hardware or PyTorch versions, and that some GPU kernels are nondeterministic by default; `torch.use_deterministic_algorithms(True)` forces determinism at some cost in speed. For learning purposes, seeding as above is enough — it makes your own experiments comparable to each other, which is the property you actually need when you are changing one thing at a time in Module 09.

## A note on versions

This book was written and verified against PyTorch 2.x. The API has been stable for years in the areas we touch — `nn`, `optim`, `utils.data`, `torchvision.datasets` — so older 2.x versions will work fine. Two things did change recently enough to mention: `torchvision.models` now takes a `weights=` argument rather than the deprecated `pretrained=True` (Module 13 uses the modern form), and `torchtext` is no longer maintained, which is why this book uses HuggingFace `datasets` for text instead. If a snippet ever disagrees with the [official documentation](https://pytorch.org/docs/stable/index.html), trust the documentation.

Once `import torch` works, go to [Module 01](./01-what-is-a-neural-network.md).
