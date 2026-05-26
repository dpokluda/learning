import sys
import torch
import torchvision
import matplotlib
import numpy

print(f"Python      : {sys.version.split()[0]}")
print(f"torch       : {torch.__version__}")
print(f"torchvision : {torchvision.__version__}")
print(f"numpy       : {numpy.__version__}")
print(f"matplotlib  : {matplotlib.__version__}")
print(f"CUDA avail. : {torch.cuda.is_available()}")

# Tiny tensor op to confirm autograd works
x = torch.tensor([2.0], requires_grad=True)
y = x ** 3
y.backward()
assert x.grad.item() == 12.0, "autograd sanity check failed"
print("autograd    : OK")