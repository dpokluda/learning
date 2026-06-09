from torchvision import datasets, transforms
from torch.utils.data import DataLoader
import torch
import torch.nn as nn
import torch.optim as optim

def create_model():
    return nn.Sequential(
      nn.Conv2d(1, 32, kernel_size=3, padding=1),
      nn.ReLU(),
      nn.MaxPool2d(2),
      nn.Conv2d(32, 64, kernel_size=3, padding=1),
      nn.ReLU(),
      nn.MaxPool2d(2),
      nn.Flatten(),
      nn.Linear(64 * 7 * 7, 128),
      nn.ReLU(),
      nn.Linear(128, 10)
    )

def create_criterion():
    return nn.CrossEntropyLoss()

def create_optimizer(model):
    return optim.Adam(model.parameters(), lr=0.001)

def train(model, criterion, optimizer):
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

    # training loop
    running_loss = 0.0
    num_epochs = 5
    first = True
    print("Starting training...")
    model.train() # no-op now
    for epoch in range(num_epochs):
      for i, (images, labels) in enumerate(train_loader):
        logits = model(images)
        loss = criterion(logits, labels)

        if i == 0:
            running_loss = loss.item()
        else:
            running_loss = running_loss * 0.9 + loss.item() * 0.1
        
        if first:
          print(f"Initial loss: {running_loss:.3f}")
          first = False

        optimizer.zero_grad()
        loss.backward()
        optimizer.step()

      print(f"Epoch {epoch + 1}/{num_epochs}: loss: {running_loss:.3f}")

    return running_loss

def evaluate(model, test_loader):
    model.eval()
    correct = 0
    total = 0
    print("Starting evaluation...")
    with torch.no_grad():
        for images, labels in test_loader:
            logits = model(images)
            _, predicted = torch.max(logits, 1)
            total += labels.size(0)
            correct += (predicted == labels).sum().item()

    return 100 * correct / total

if __name__ == "__main__":
  # create the model, criterion, and optimizer
  model = create_model()
  criterion = create_criterion()
  optimizer = create_optimizer(model)

  # train the model
  running_loss = train(model, criterion, optimizer)
  print(f"Final loss: {running_loss:.3f}")

  # evaluate the model
  test_set = datasets.MNIST(
      root="./data",
      train=False,
      download=True,
      transform=transforms.ToTensor(),
  )
  test_loader = DataLoader(test_set, batch_size=64, shuffle=False)
  accuracy = evaluate(model, test_loader)
  print(f"Evaluation accuracy: {accuracy:.2f}%")