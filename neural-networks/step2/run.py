from torchvision import datasets, transforms
import matplotlib.pyplot as plt
import torch

train_set = datasets.MNIST(
    root="./data",
    train=True,
    download=True,
    transform=transforms.ToTensor(),
)

# Pick 25 random indices from the training set
random_indices = torch.randperm(len(train_set))[:25]

# Create a 5x5 grid of subplots
fig, axes = plt.subplots(5, 5, figsize=(10, 10))
# Loop through the random indices and plot the corresponding images
for ax, idx in zip(axes.flatten(), random_indices):
  image, label = train_set[idx]
  ax.imshow(image.squeeze(), cmap="gray")
  ax.set_title(f"Label: {label}")
  ax.axis("off")

plt.tight_layout()
plt.show()

# create tenzor for our batch of images from random_indices
batch_images = torch.stack([train_set[idx][0] for idx in random_indices])
print(batch_images.shape)  # should be [25, 1, 28, 28]

# check the min and max pixel values in the batch
print(f"min={batch_images.min().item()}, max={batch_images.max().item()}")