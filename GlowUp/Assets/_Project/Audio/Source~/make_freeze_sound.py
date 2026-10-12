# Makes the app's OWN "freezing" sound for removing a scan: Delete_Freeze_Made.wav (mono, 44.1 kHz, about 1.2 seconds).
# Nothing is recorded or copied from anywhere: the sound is worked out from numbers, so it belongs to the project.
#
#   python make_freeze_sound.py <output folder>          (only Python itself is needed, no extra packages)
#
# What it is made of, so it can be changed:
#   1. CRACKLE  many tiny, bright "ticks" (ice forming). They come thick and fast at the start and thin out.
#   2. SHIMMER  a few high, glassy tones that ring together and fade (frost).
#   3. BREATH   a soft hiss of air that swells and dies away (cold).
#   4. SWEEP    a quiet tone that climbs, which gives the feeling of something freezing up.
#   5. TINK     one small glass bell near the end (frozen).
# Change SEED for another pattern of ticks; change the numbers marked "level" to make a part louder or quieter.
import math, os, random, struct, sys, wave

OUT = sys.argv[1] if len(sys.argv) > 1 else "."
RATE = 44100
SECONDS = 1.2
SEED = 12
N = int(RATE * SECONDS)
TWO_PI = 2 * math.pi
random.seed(SEED)
sound = [0.0] * N


def add(start_seconds, length_seconds, sample):
    """Adds sample(t) (t = seconds since this part began) into the sound."""
    first = int(start_seconds * RATE)
    for i in range(max(0, first), min(N, first + int(length_seconds * RATE))):
        sound[i] += sample((i - first) / RATE)


# ---- 1. crackle: about 90 ticks, each a very short ring at a high pitch with a grain of noise ----
ticks = 0
t = 0.0
while t < 0.95:
    # thick at the start (every 4 ms or so), thinning out towards the end
    gap = random.expovariate(1.0) * (0.004 + 0.030 * (t / 0.95) ** 1.6)
    t += max(0.0015, gap)
    pitch = random.uniform(2600, 7200)
    ring = random.uniform(0.002, 0.007)                 # how long one tick rings (seconds)
    level = random.uniform(0.15, 0.85) * (1.0 - 0.55 * t / 0.95)
    if random.random() < 0.12: level *= 1.7             # now and then a bigger crack
    phase = random.uniform(0, TWO_PI)
    grain = random.uniform(0.15, 0.45)
    add(t, ring * 6, lambda x, p=pitch, r=ring, l=level, ph=phase, g=grain:
        l * math.exp(-x / r) * ((1 - g) * math.sin(TWO_PI * p * x + ph) + g * random.uniform(-1, 1)))
    ticks += 1

# ---- 2. shimmer: glassy tones that are not in tune with each other, like a small sheet of ice ringing ----
BASE = 1760.0
for ratio, level, fade in ((1.0, 0.20, 0.42), (1.47, 0.15, 0.36), (2.09, 0.13, 0.30), (2.56, 0.10, 0.26), (3.14, 0.08, 0.22), (4.02, 0.05, 0.18)):
    wobble = random.uniform(7.0, 11.0)
    add(0.03, SECONDS - 0.03, lambda x, f=BASE * ratio, l=level, d=fade, w=wobble:
        l * min(1.0, x / 0.05) * math.exp(-x / d) * (1 + 0.25 * math.sin(TWO_PI * w * x)) * math.sin(TWO_PI * f * x))

# ---- 3. breath: a hiss (noise with its low part taken out) that swells and dies away ----
last = 0.0
for i in range(N):
    x = i / RATE
    white = random.uniform(-1, 1)
    hiss = white - last                                  # the difference of neighbours keeps only the bright part
    last = white
    swell = min(1.0, x / 0.18) * math.exp(-max(0.0, x - 0.18) / 0.30)
    sound[i] += 0.085 * swell * hiss                     # level

# ---- 4. sweep: a quiet tone climbing from 900 to 3400 Hz ----
def sweep(x, seconds=0.62):
    k = min(1.0, x / seconds)
    turns = 900 * x + (3400 - 900) * x * k / 2           # the pitch rises steadily
    return 0.10 * math.sin(math.pi * k) ** 1.5 * math.sin(TWO_PI * turns)
add(0.02, 0.62, sweep)

# ---- 5. tink: one small glass bell ----
for pitch, level, fade in ((3520.0, 0.34, 0.16), (5274.0, 0.20, 0.11), (7040.0, 0.12, 0.08)):
    add(0.64, SECONDS - 0.64, lambda x, f=pitch, l=level, d=fade: l * min(1.0, x / 0.002) * math.exp(-x / d) * math.sin(TWO_PI * f * x))

# ---- no click at either end, a soft fade at the end, and a loudness like the tap sounds ----
for i in range(N):
    start = min(1.0, i / (0.004 * RATE))
    end = min(1.0, (N - i) / (0.28 * RATE))
    sound[i] *= start * end
peak = max(abs(v) for v in sound) or 1.0
sound = [v / peak * 0.85 for v in sound]

os.makedirs(OUT, exist_ok=True)
path = os.path.join(OUT, "Delete_Freeze_Made.wav")
with wave.open(path, "wb") as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
    w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, v)) * 32767)) for v in sound))

# ---- a few numbers about the result (the program cannot hear, so it says what it can measure) ----
tenths = []
for a in range(0, N, RATE // 10):
    part = sound[a:a + RATE // 10]
    tenths.append(math.sqrt(sum(v * v for v in part) / max(1, len(part))))
loudest = max(tenths)
crossings = sum(1 for i in range(1, N) if (sound[i - 1] < 0) != (sound[i] < 0))
print("written:", path)
print("length %.2f s, %d ticks, loudest point %.2f" % (SECONDS, ticks, max(abs(v) for v in sound)))
print("loudness in each tenth of a second (100 = the loudest tenth):", " ".join("%3d" % round(100 * v / loudest) for v in tenths))
print("brightness: about %d Hz on average (from how often the wave crosses zero)" % round(crossings / 2 / SECONDS))
