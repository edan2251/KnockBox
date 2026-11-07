"""KNOCKBOX 효과음 합성기 (05-presentation.md 7절).

파이썬 표준 라이브러리만 쓴다. 효과음마다 고정 시드라 다시 실행해도 같은 파일이 나온다.

    python Tools/sfx_synth.py                  # Assets/_Project/Audio/Sfx 에 전부 생성
    python Tools/sfx_synth.py --out DIR jump   # DIR 에 jump.wav 만 생성

새 효과음은 SOUNDS 표에 함수를 더한다. 소리를 바꾸면 이 파일과 WAV를 함께 커밋한다.
"""
import argparse
import math
import os
import random
import struct
import wave

RATE = 44100
DEFAULT_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "_Project", "Audio", "Sfx")


def envelope(t, attack, decay_time):
    """빠르게 올라가고 지수로 줄어드는 포락선."""
    if t < attack:
        return t / attack
    return math.exp(-(t - attack) / decay_time)


def lowpass(samples, amount):
    """한 극 저역 통과. amount 가 0에 가까울수록 더 먹먹하다."""
    out, y = [], 0.0
    for x in samples:
        y += amount * (x - y)
        out.append(y)
    return out


def sweep(duration, f0, f1, rng, noise=0.0, attack=0.004, decay=0.05, shape="sine"):
    """f0 → f1 로 미끄러지는 음 + 소음."""
    n = int(duration * RATE)
    phase, out = 0.0, []
    for i in range(n):
        t = i / RATE
        k = i / max(n - 1, 1)
        freq = f0 * (f1 / f0) ** k
        phase += 2.0 * math.pi * freq / RATE
        if shape == "square":
            tone = 1.0 if math.sin(phase) >= 0 else -1.0
            tone *= 0.5
        else:
            tone = math.sin(phase)
        value = tone * (1.0 - noise) + (rng.random() * 2.0 - 1.0) * noise
        out.append(value * envelope(t, attack, decay))
    return out


def jump(rng):
    body = sweep(0.14, 330.0, 760.0, rng, noise=0.08, attack=0.003, decay=0.045)
    return [s * 0.55 for s in body]


def land(rng):
    thump = sweep(0.16, 140.0, 55.0, rng, noise=0.0, attack=0.002, decay=0.05)
    n = len(thump)
    dust = lowpass([(rng.random() * 2.0 - 1.0) * envelope(i / RATE, 0.001, 0.03) for i in range(n)], 0.18)
    return [0.75 * a + 0.5 * b for a, b in zip(thump, dust)]


def dash(rng):
    n = int(0.24 * RATE)
    raw = [(rng.random() * 2.0 - 1.0) for _ in range(n)]
    # 바람 소리: 소음을 점점 밝게(저역 통과 계수를 키우며) 걸러 '휙' 하고 지나가게 한다.
    out, y = [], 0.0
    for i, x in enumerate(raw):
        k = i / (n - 1)
        amount = 0.04 + 0.35 * math.sin(math.pi * k)
        y += amount * (x - y)
        out.append(y * math.sin(math.pi * k) ** 0.7)
    return [s * 1.4 for s in out]


def noise_burst(rng, duration, decay, amount):
    n = int(duration * RATE)
    return lowpass([(rng.random() * 2.0 - 1.0) * envelope(i / RATE, 0.001, decay) for i in range(n)], amount)


def bells(rng, duration, partials, decay):
    """비조화 배음의 합(금속성). partials 는 (주파수, 세기) 목록."""
    n = int(duration * RATE)
    phases = [rng.random() * 2.0 * math.pi for _ in partials]
    out = []
    for i in range(n):
        t = i / RATE
        value = sum(a * math.sin(phases[j] + 2.0 * math.pi * f * t) for j, (f, a) in enumerate(partials))
        out.append(value * envelope(t, 0.001, decay))
    return out


def mix(*layers):
    n = max(len(layer) for _, layer in layers)
    out = [0.0] * n
    for gain, layer in layers:
        for i, x in enumerate(layer):
            out[i] += gain * x
    return out


def hit_weak(rng):
    thump = sweep(0.12, 240.0, 90.0, rng, attack=0.001, decay=0.03)
    snap = noise_burst(rng, 0.12, 0.012, 0.55)
    return mix((0.8, thump), (0.7, snap))


def hit_strong(rng):
    thump = sweep(0.24, 170.0, 42.0, rng, attack=0.001, decay=0.06)
    crunch = noise_burst(rng, 0.24, 0.035, 0.3)
    snap = noise_burst(rng, 0.05, 0.006, 0.8)
    return mix((0.9, thump), (0.6, crunch), (0.5, snap))


def clash(rng):
    ring = bells(rng, 0.38, [(1250.0, 1.0), (1873.0, 0.7), (2631.0, 0.55), (3417.0, 0.35)], 0.09)
    click = noise_burst(rng, 0.05, 0.004, 0.9)
    return mix((0.7, ring), (0.6, click))


def parry(rng):
    ting = bells(rng, 0.42, [(1975.0, 1.0), (3950.0, 0.35), (5925.0, 0.12)], 0.13)
    click = noise_burst(rng, 0.03, 0.003, 0.9)
    return mix((0.8, ting), (0.3, click))


def guard(rng):
    block = sweep(0.12, 320.0, 170.0, rng, noise=0.15, attack=0.001, decay=0.025, shape="square")
    thud = noise_burst(rng, 0.12, 0.02, 0.2)
    return mix((0.6, block), (0.7, thud))


def wall_splat(rng):
    boom = sweep(0.4, 120.0, 38.0, rng, attack=0.001, decay=0.1)
    rubble = noise_burst(rng, 0.4, 0.09, 0.12)
    crack = noise_burst(rng, 0.06, 0.008, 0.85)
    return mix((1.0, boom), (0.7, rubble), (0.55, crack))


def grab(rng):
    grip = noise_burst(rng, 0.1, 0.008, 0.7)
    thud = sweep(0.1, 210.0, 120.0, rng, attack=0.001, decay=0.03)
    return mix((0.6, grip), (0.7, thud))


def throw(rng):
    n = int(0.3 * RATE)
    raw = [(rng.random() * 2.0 - 1.0) for _ in range(n)]
    out, y = [], 0.0
    for i, x in enumerate(raw):
        k = i / (n - 1)
        amount = 0.03 + 0.5 * k * k  # 점점 밝아지며 휙 지나간다
        y += amount * (x - y)
        out.append(y * math.sin(math.pi * k) ** 0.6)
    swoosh = [v * 1.6 for v in out]
    tone = sweep(0.3, 180.0, 420.0, rng, attack=0.02, decay=0.12)
    return mix((1.0, swoosh), (0.25, tone))


def spike(rng):
    sting = bells(rng, 0.3, [(2310.0, 1.0), (3170.0, 0.7), (4630.0, 0.4)], 0.05)
    crunch = noise_burst(rng, 0.25, 0.04, 0.5)
    ouch = sweep(0.25, 520.0, 260.0, rng, noise=0.1, attack=0.002, decay=0.06, shape="square")
    return mix((0.6, sting), (0.5, crunch), (0.35, ouch))


def boundary(rng):
    """경계 튕김(CR-002): 짧은 전기 소리. 빠르게 흔들리는 사각파 윙 + 지지직 잡음 + 높은 따끔함."""
    n = int(0.28 * RATE)
    buzz = []
    phase = 0.0
    for i in range(n):
        t = i / RATE
        f = 150.0 + 90.0 * math.sin(2.0 * math.pi * 31.0 * t)
        phase += 2.0 * math.pi * f / RATE
        square = 1.0 if math.sin(phase) >= 0.0 else -1.0
        gate = 1.0 if rng.random() > 0.18 else 0.2
        buzz.append(square * gate * envelope(t, 0.002, 0.07))
    crackle = noise_burst(rng, 0.22, 0.05, 0.9)
    sting = bells(rng, 0.12, [(2900.0, 1.0), (4100.0, 0.6)], 0.025)
    return mix((0.55, lowpass(buzz, 0.35)), (0.5, crackle), (0.35, sting))


def snap(rng):
    """줄·경첩이 끊어짐(CP15): 짧고 높은 '툭' — 날카로운 잡음 터짐 + 빠르게 떨어지는 짧은 음."""
    crack = noise_burst(rng, 0.09, 0.012, 0.95)
    twang = sweep(0.16, 900.0, 320.0, rng, attack=0.001, decay=0.04, shape="square")
    return mix((0.8, crack), (0.35, twang))


def pad(rng):
    n = int(0.32 * RATE)
    phase, out = 0.0, []
    for i in range(n):
        t = i / RATE
        k = i / (n - 1)
        freq = 180.0 * (3.4 ** k) * (1.0 + 0.06 * math.sin(2.0 * math.pi * 18.0 * t))  # 보잉: 올라가며 떨림
        phase += 2.0 * math.pi * freq / RATE
        out.append(math.sin(phase) * envelope(t, 0.004, 0.12))
    return out


def countdown(rng):
    return sweep(0.14, 880.0, 880.0, rng, attack=0.003, decay=0.06, shape="square")


def ko(rng):
    boom = sweep(0.7, 110.0, 30.0, rng, attack=0.001, decay=0.2)
    rubble = noise_burst(rng, 0.7, 0.16, 0.15)
    fall = sweep(0.7, 660.0, 110.0, rng, noise=0.05, attack=0.01, decay=0.25, shape="square")
    return mix((1.0, boom), (0.6, rubble), (0.3, fall))


SOUNDS = {
    "jump": (jump, 101),
    "land": (land, 102),
    "dash": (dash, 103),
    "hitWeak": (hit_weak, 104),
    "hitStrong": (hit_strong, 105),
    "clash": (clash, 106),
    "parry": (parry, 107),
    "guard": (guard, 108),
    "wallSplat": (wall_splat, 109),
    "grab": (grab, 110),
    "throw": (throw, 111),
    "spike": (spike, 112),
    "pad": (pad, 113),
    "countdown": (countdown, 114),
    "ko": (ko, 115),
    "boundary": (boundary, 116),
    "snap": (snap, 117),
}


def normalize(samples, peak=0.85):
    top = max(abs(s) for s in samples) or 1.0
    return [s * peak / top for s in samples]


def write_wav(path, samples):
    frames = b"".join(struct.pack("<h", int(round(max(-1.0, min(1.0, s)) * 32767))) for s in samples)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(frames)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=DEFAULT_OUT)
    parser.add_argument("names", nargs="*", help="만들 효과음 이름(없으면 전부)")
    args = parser.parse_args()

    names = args.names or sorted(SOUNDS)
    os.makedirs(args.out, exist_ok=True)
    for name in names:
        if name not in SOUNDS:
            raise SystemExit(f"알 수 없는 효과음: {name} (가능: {', '.join(sorted(SOUNDS))})")
        func, seed = SOUNDS[name]
        samples = normalize(func(random.Random(seed)))
        path = os.path.join(args.out, name + ".wav")
        write_wav(path, samples)
        print(f"{path} ({len(samples) / RATE:.3f}s)")


if __name__ == "__main__":
    main()
