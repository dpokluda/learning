from torchvision import datasets, transforms
from torch.utils.data import DataLoader
import torch
import torch.nn as nn
import torch.optim as optim

def train(model, criterion, optimizer, train_loader, test_loader, num_epochs):
  train_losses = []
  test_accuracies = []

  # training loop
  running_loss = 0.0
  for epoch in range(num_epochs):
    model.train()
    for i, (images, labels) in enumerate(train_loader):
      flattened_images = images.view(-1, 28 * 28)
      logits = model(flattened_images)
      loss = criterion(logits, labels)

      if i == 0:
          running_loss = loss.item()
      else:
          running_loss = running_loss * 0.9 + loss.item() * 0.1
      
      optimizer.zero_grad()
      loss.backward()
      optimizer.step()

    accuracy = evaluate(model, test_loader)
    train_losses.append(running_loss)
    test_accuracies.append(accuracy)
    print(f"epoch {epoch+1:2d} | loss {running_loss:.3f} | test acc {accuracy:.2f}%")

  return train_losses, test_accuracies

def evaluate(model, test_loader):
    model.eval()
    correct = 0
    total = 0
    with torch.no_grad():
        for images, labels in test_loader:
            flattened_images = images.view(-1, 28 * 28)
            logits = model(flattened_images)
            _, predicted = torch.max(logits, 1)
            total += labels.size(0)
            correct += (predicted == labels).sum().item()

    return 100 * correct / total

def run_experiment(name, model, optimizer, train_loader, test_loader, num_epochs):
    print(f"=== {name} ===")
    criterion = nn.CrossEntropyLoss()
    train_losses, test_accuracies = train(model, criterion, optimizer, train_loader, test_loader, num_epochs)
    return train_losses, test_accuracies

if __name__ == "__main__":
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

  # evaluate the model
  test_set = datasets.MNIST(
      root="./data",
      train=False,
      download=True,
      transform=transforms.ToTensor(),
  )
  test_loader = DataLoader(test_set, batch_size=64, shuffle=False)
  
  # experiments
  # torch.manual_seed(0)
  # model = nn.Sequential(
  #   nn.Linear(28 * 28, 128),
  #   nn.ReLU(),
  #   nn.Linear(128, 10)
  # )
  # optimizer = optim.SGD(model.parameters(), lr=0.01) # SGD: wight =  weight - lr * gradient
  # sgd_losses, sgd_accs = run_experiment("baseline 15ep", model, optimizer, train_loader, test_loader, num_epochs=15)

  # torch.manual_seed(0)
  # model = nn.Sequential(
  #   nn.Linear(28 * 28, 128),
  #   nn.ReLU(),
  #   nn.Linear(128, 10)
  # )
  # optimizer = optim.Adam(model.parameters(), lr=0.001) # Adam - adaptive + momentum: keeps per-parameter EMAs of the gradient (m, "momentum") and of the squared gradient (v, "variance"); update = lr * m / (sqrt(v) + eps), so each weight gets its own adaptive step size
  # adam_losses, adam_accs = run_experiment("adam 15ep", model, optimizer, train_loader, test_loader, num_epochs=15)

  # torch.manual_seed(0)
  # model = nn.Sequential(
  #   nn.Linear(28 * 28, 128), # first hidden layer
  #   nn.ReLU(),
  #   nn.Linear(128, 128), # second hidden layer
  #   nn.ReLU(),
  #   nn.Linear(128, 10)
  # )
  # optimizer = optim.Adam(model.parameters(), lr=0.001) # Adam - adaptive + momentum: keeps per-parameter EMAs of the gradient (m, "momentum") and of the squared gradient (v, "variance"); update = lr * m / (sqrt(v) + eps), so each weight gets its own adaptive step size
  # adam_losses, adam_accs = run_experiment("2 hidden layers with adam 15ep", model, optimizer, train_loader, test_loader, num_epochs=15)

  torch.manual_seed(0)
  model = nn.Sequential(
    nn.Linear(28 * 28, 128),
    nn.ReLU(),
    nn.Dropout(0.2), # dropout: randomly zeroes some of the elements of the input tensor with probability p using samples from a Bernoulli distribution. During training, this has the effect of preventing units from co-adapting too much. During evaluation, this layer does nothing.
    nn.Linear(128, 128),
    nn.ReLU(),
    nn.Dropout(0.2),
    nn.Linear(128, 10)
  )
  optimizer = optim.Adam(model.parameters(), lr=0.001) # Adam - adaptive + momentum: keeps per-parameter EMAs of the gradient (m, "momentum") and of the squared gradient (v, "variance"); update = lr * m / (sqrt(v) + eps), so each weight gets its own adaptive step size
  adam_losses, adam_accs = run_experiment("2 hidden layers with dropout 0.2 with adam 15ep", model, optimizer, train_loader, test_loader, num_epochs=15)
