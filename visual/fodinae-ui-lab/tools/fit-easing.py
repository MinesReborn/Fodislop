#!/usr/bin/env python3
"""
Какая именованная кривая Unity ближе всего к нашей cubic-bezier.

ЗАЧЕМ. В UI Toolkit функции cubic-bezier() нет вовсе — доступны 23
именованные кривые. Витрина утверждает, что сигнатурная кривая макета
cubic-bezier(0.2, 0.75, 0.2, 1) ложится на ease-out-circ, но метода не
приводит. Это единственное непроверенное утверждение в системе, и оно
дорогое: кривая фирменная (планета и рейл).

МЕТОДИЧЕСКАЯ ТОНКОСТЬ, РАДИ КОТОРОЙ ВСЁ И ЗАТЕВАЛОСЬ.
CSS-кривая задана ПАРАМЕТРИЧЕСКИ: точка на ней — это (x(u), y(u)), где u
пробегает [0,1]. Именованные кривые Unity заданы как функция f(t). Сравнивать
y(u) с f(u) нельзя: у бегунка u и времени t разный ход, и совпадение по u
ничего не говорит о совпадении по времени.

Поэтому для каждого t решаем x(u) = t бисекцией и берём y(u). Прямое сравнение
y(t) — классическая ошибка, и она СИСТЕМАТИЧЕСКИ выбирает не ту кривую:
скрипт печатает и её ответ тоже, чтобы разница была видна, а не заявлена.

Определения кривых — канонические (Penner/easings.net), им следует и Unity.
Ease/EaseIn/EaseOut/EaseInOut в Unity отображаются на Quad-семейство: в
UnityEngine.UIElementsModule.dll присутствуют InQuad/OutQuad/InOutQuad и
отсутствуют другие кандидаты. На вывод это не влияет — победитель не из них.
"""
import math

BEZIER = (0.2, 0.75, 0.2, 1.0)   # --ease-signature
SAMPLES = 101


def bezier_axis(u: float, a: float, b: float) -> float:
    """Кубическая Безье по одной оси; P0=0, P3=1."""
    v = 1.0 - u
    return 3 * v * v * u * a + 3 * v * u * u * b + u * u * u


def bezier_y_at_time(t: float, p: tuple[float, float, float, float]) -> float:
    """y в момент времени t: сперва решаем x(u)=t, потом берём y(u).

    Бисекция, а не Ньютон: x(u) монотонна при 0<=x1,x2<=1, а бисекция не
    зависит от начального приближения и не расходится. 60 шагов дают
    точность ~1e-18 — заведомо мельче, чем всё, что мы сравниваем.
    """
    x1, y1, x2, y2 = p
    lo, hi = 0.0, 1.0
    for _ in range(60):
        mid = (lo + hi) / 2
        if bezier_axis(mid, x1, x2) < t:
            lo = mid
        else:
            hi = mid
    return bezier_axis((lo + hi) / 2, y1, y2)


C1 = 1.70158
C2 = C1 * 1.525
C3 = C1 + 1
C4 = 2 * math.pi / 3
C5 = 2 * math.pi / 4.5


def out_bounce(t: float) -> float:
    n, d = 7.5625, 2.75
    if t < 1 / d:
        return n * t * t
    if t < 2 / d:
        t -= 1.5 / d
        return n * t * t + 0.75
    if t < 2.5 / d:
        t -= 2.25 / d
        return n * t * t + 0.9375
    t -= 2.625 / d
    return n * t * t + 0.984375


CURVES = {
    "linear":          lambda t: t,
    "ease-in-sine":    lambda t: 1 - math.cos(t * math.pi / 2),
    "ease-out-sine":   lambda t: math.sin(t * math.pi / 2),
    "ease-in-out-sine": lambda t: -(math.cos(math.pi * t) - 1) / 2,
    "ease-in":         lambda t: t * t,                       # InQuad
    "ease-out":        lambda t: 1 - (1 - t) ** 2,            # OutQuad
    "ease-in-out":     lambda t: 2 * t * t if t < .5 else 1 - (-2 * t + 2) ** 2 / 2,
    "ease":            lambda t: 2 * t * t if t < .5 else 1 - (-2 * t + 2) ** 2 / 2,
    "ease-in-cubic":   lambda t: t ** 3,
    "ease-out-cubic":  lambda t: 1 - (1 - t) ** 3,
    "ease-in-out-cubic": lambda t: 4 * t ** 3 if t < .5 else 1 - (-2 * t + 2) ** 3 / 2,
    "ease-in-circ":    lambda t: 1 - math.sqrt(max(0.0, 1 - t * t)),
    "ease-out-circ":   lambda t: math.sqrt(max(0.0, 1 - (t - 1) ** 2)),
    "ease-in-out-circ": lambda t: (1 - math.sqrt(max(0.0, 1 - (2 * t) ** 2))) / 2
                        if t < .5 else (math.sqrt(max(0.0, 1 - (-2 * t + 2) ** 2)) + 1) / 2,
    "ease-in-back":    lambda t: C3 * t ** 3 - C1 * t * t,
    "ease-out-back":   lambda t: 1 + C3 * (t - 1) ** 3 + C1 * (t - 1) ** 2,
    "ease-in-out-back": lambda t: ((2 * t) ** 2 * ((C2 + 1) * 2 * t - C2)) / 2 if t < .5
                        else ((2 * t - 2) ** 2 * ((C2 + 1) * (t * 2 - 2) + C2) + 2) / 2,
    "ease-in-elastic": lambda t: 0.0 if t == 0 else 1.0 if t == 1
                       else -(2 ** (10 * t - 10)) * math.sin((t * 10 - 10.75) * C4),
    "ease-out-elastic": lambda t: 0.0 if t == 0 else 1.0 if t == 1
                        else 2 ** (-10 * t) * math.sin((t * 10 - 0.75) * C4) + 1,
    "ease-in-out-elastic": lambda t: 0.0 if t == 0 else 1.0 if t == 1
                           else -(2 ** (20 * t - 10) * math.sin((20 * t - 11.125) * C5)) / 2
                           if t < .5 else (2 ** (-20 * t + 10) * math.sin((20 * t - 11.125) * C5)) / 2 + 1,
    "ease-in-bounce":  lambda t: 1 - out_bounce(1 - t),
    "ease-out-bounce": out_bounce,
    "ease-in-out-bounce": lambda t: (1 - out_bounce(1 - 2 * t)) / 2 if t < .5
                          else (1 + out_bounce(2 * t - 1)) / 2,
}


def score(target: list[float], f) -> tuple[float, float]:
    diffs = [abs(f(i / (SAMPLES - 1)) - target[i]) for i in range(SAMPLES)]
    rms = math.sqrt(sum(d * d for d in diffs) / SAMPLES)
    return max(diffs), rms


def main() -> None:
    ts = [i / (SAMPLES - 1) for i in range(SAMPLES)]
    correct = [bezier_y_at_time(t, BEZIER) for t in ts]
    naive = [bezier_axis(t, BEZIER[1], BEZIER[3]) for t in ts]   # ошибочный способ

    print(f"кривая: cubic-bezier{BEZIER}   точек: {SAMPLES}\n")
    for label, target in (("ПРАВИЛЬНО (решаем x(u)=t)", correct),
                          ("ОШИБОЧНО (сравниваем y(t) напрямую)", naive)):
        ranked = sorted(((score(target, f), n) for n, f in CURVES.items()),
                        key=lambda kv: kv[0][1])
        print(label)
        print(f"  {'кривая':<22} {'max':>9} {'rms':>9}")
        for (mx, rms), name in ranked[:5]:
            print(f"  {name:<22} {mx:>9.4f} {rms:>9.4f}")
        print(f"  -> {ranked[0][1]}\n")

    best = min(((score(correct, f), n) for n, f in CURVES.items()), key=lambda kv: kv[0][1])
    circ = score(correct, CURVES["ease-out-circ"])
    print(f"утверждение витрины: ease-out-circ   max={circ[0]:.4f} rms={circ[1]:.4f}")
    print(f"фактический победитель: {best[1]}   max={best[0][0]:.4f} rms={best[0][1]:.4f}")
    print("ВЕРНО" if best[1] == "ease-out-circ" else "УТВЕРЖДЕНИЕ НЕВЕРНО")


if __name__ == "__main__":
    main()
