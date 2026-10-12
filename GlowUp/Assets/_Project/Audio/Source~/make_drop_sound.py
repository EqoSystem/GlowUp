# Makes the sound of the DROP when a scan is let go into the remove area: Delete_Drop_Made.wav (mono, 44.1 kHz, 0.4 s).
# Nothing is recorded or copied: the sound is worked out from numbers, so it belongs to the project.
#
#   python make_drop_sound.py <output folder>          (only Python itself is needed)
#
# What it is made of: a short low knock (the drop), a burst of bright splinters (ice breaking), and a glassy ring that
# dies away. Change SEED for other splinters; the numbers marked "level" make a part louder or quieter.
import math, os, random, struct, sys, wave

OUT = sys.argv[1] if len(sys.argv) > 1 else "."
RATE = 44100
SECONDS = 0.4
SEED = 5
N = int(RATE * SECONDS)
TWO_PI = 2 * math.pi
random.seed(SEED)
sound = [0.0] * N

# ---- the knock: a low tone whose pitch falls quickly ----
turns = 0.0
for i in range(N):
    x = i / RATE
    pitch = 70 + 170 * math.exp(-x / 0.03)
    turns += pitch / RATE
    sound[i] += 0.9 * math.exp(-x / 0.06) * math.sin(TWO_PI * turns)             # level

# ---- the splinters: about 30 tiny bright ticks in the first quarter of a second ----
for _ in range(30):
    start = random.uniform(0.0, 0.22) ** 1.4
    pitch = random.uniform(3000, 8000)
    ring = random.uniform(0.002, 0.006)
    level = random.uniform(0.2, 0.6) * (1 - start / 0.3)
    first = int(start * RATE)
    for i in range(first, min(N, first + int(ring * 6 * RATE))):
        x = (i - first) / RATE
        sound[i] += level * math.exp(-x / ring) * (0.7 * math.sin(TWO_PI * pitch * x) + 0.3 * random.uniform(-1, 1))

# ---- the ring of glass ----
for pitch, level, fade in ((2093.0, 0.22, 0.10), (3136.0, 0.14, 0.07), (4699.0, 0.08, 0.05)):
    for i in range(N):
        x = i / RATE
        sound[i] += level * min(1.0, x / 0.002) * math.exp(-x / fade) * math.sin(TWO_PI * pitch * x)

for i in range(N):
    sound[i] *= min(1.0, i / (0.002 * RATE)) * min(1.0, (N - i) / (0.08 * RATE))
peak = max(abs(v) for v in sound) or 1.0
sound = [v / peak * 0.85 for v in sound]

os.makedirs(OUT, exist_ok=True)
path = os.path.join(OUT, "Delete_Drop_Made.wav")
with wave.open(path, "wb") as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
    w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, v)) * 32767)) for v in sound))
print("written:", path, "length %.2f s" % SECONDS)
