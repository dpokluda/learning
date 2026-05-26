"""Quick tqdm demo — shows the three most common usage patterns."""
import time
from tqdm import tqdm, trange


# 1) Wrap any iterable. This is the 90% case.
print("\n[1] Wrapping a range:")
for i in tqdm(range(50), desc="processing"):
    time.sleep(0.02)

# 2) trange = shortcut for tqdm(range(...))
print("\n[2] trange shortcut:")
for i in trange(30, desc="epochs"):
    time.sleep(0.03)

# 3) Manual control — useful when you don't have a fixed-length iterable,
#    e.g. streaming data, or when you want to update with custom info.
print("\n[3] Manual updates with postfix (simulated training loop):")
n_batches = 40
loss = 2.30
with tqdm(total=n_batches, desc="train") as bar:
    for batch in range(n_batches):
        time.sleep(0.03)
        loss *= 0.97  # pretend the model is learning
        bar.update(1)
        bar.set_postfix(loss=f"{loss:.4f}")

# 4) Nested bars — outer = epochs, inner = batches.
print("\n[4] Nested bars (epochs x batches):")
for epoch in trange(3, desc="epoch"):
    for batch in tqdm(range(20), desc=f"  batch", leave=False):
        time.sleep(0.01)

print("\nDone.")
