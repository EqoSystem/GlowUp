# Makes the five tap sounds (short, quiet, mono 44.1 kHz WAV files). Usage: make_sounds.py <output folder>
import math, os, random, struct, sys, wave
OUT = sys.argv[1]; RATE = 44100
os.makedirs(OUT, exist_ok=True)
def write(name, seconds, sample):
    n = int(RATE * seconds); data = []
    for i in range(n):
        t = i / RATE
        v = sample(t, t / seconds)
        fade = min(1.0, i / 60.0) * min(1.0, (n - i) / 200.0)        # no click at either end
        data.append(max(-1.0, min(1.0, v * fade)))
    peak = max(abs(v) for v in data) or 1.0
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(v / peak * 0.82 * 32767)) for v in data))
    print(name, f"{seconds * 1000:.0f} ms")
sin = lambda f, t: math.sin(2 * math.pi * f * t)
random.seed(7)
# 1. soft tap: a low, round knock
write("UiTap_1_SoftTap", 0.09, lambda t, k: (sin(330, t) + 0.35 * sin(660, t)) * math.exp(-k * 9))
# 2. pop: the pitch drops quickly
def pop(t, k):
    f = 620 * math.exp(-k * 2.6) + 150
    return math.sin(2 * math.pi * (620 / (2.6 / 0.085)) * (1 - math.exp(-t * 2.6 / 0.085)) + 2 * math.pi * 150 * t) * math.exp(-k * 5)
write("UiTap_2_Pop", 0.085, pop)
# 3. click: a tiny dry tick
noise = [random.uniform(-1, 1) for _ in range(RATE)]
write("UiTap_3_Click", 0.03, lambda t, k: (0.6 * sin(2100, t) + 0.4 * noise[int(t * RATE)]) * math.exp(-k * 11))
# 4. bubble: the pitch rises
def bubble(t, k):
    phase = 2 * math.pi * (280 * t + (820 / 2) * t * t / 0.12)
    return math.sin(phase) * math.exp(-k * 3.5) * (0.6 + 0.4 * math.sin(math.pi * k))
write("UiTap_4_Bubble", 0.12, bubble)
# 5. chime: a small bell
write("UiTap_5_Chime", 0.32, lambda t, k: (sin(1320, t) + 0.5 * sin(1980, t) + 0.25 * sin(2640, t)) * math.exp(-k * 6))
