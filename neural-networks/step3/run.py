from torchvision import datasets, transforms
from torch.utils.data import DataLoader
import torch.nn as nn
import torch.optim as optim

# load dataset
train_set = datasets.MNIST(
    root="./data",
    train=True,
    download=True,
    transform=transforms.ToTensor(),
)

# use DataLoader to create batches of data
batch_size = 64
train_loader = DataLoader(train_set, batch_size=batch_size, shuffle=True)

# create a simple linear model
model = nn.Linear(28 * 28, 10)
criterion = nn.CrossEntropyLoss()
optimizer = optim.SGD(model.parameters(), lr=0.01)

# training loop
running_loss = 0.0
print("Starting training...")
model.train() # no-op now
for i, (images, labels) in enumerate(train_loader):
    flattened_images = images.view(-1, 28 * 28)
    logits = model(flattened_images)
    loss = criterion(logits, labels)

    if i == 0:
        running_loss = loss.item()
    else:
        running_loss = running_loss * 0.9 + loss.item() * 0.1
        
    if i % 100 == 0:
        print(f"  step {i}  loss={loss.item():.3f}  ema={running_loss:.3f}")

    optimizer.zero_grad()
    loss.backward()
    optimizer.step()

# print the final loss
print(f"Final loss: {running_loss:.3f}")