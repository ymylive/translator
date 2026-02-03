"""Material Design easing functions for smooth animations"""
import math


def ease_out_cubic(t: float) -> float:
    """Material Design standard deceleration curve.

    Best for: elements entering the screen, hover effects, most UI transitions.
    """
    return 1 - pow(1 - t, 3)


def ease_in_cubic(t: float) -> float:
    """Acceleration curve.

    Best for: elements leaving the screen.
    """
    return t * t * t


def ease_in_out_cubic(t: float) -> float:
    """Smooth acceleration then deceleration.

    Best for: elements that move from one position to another on screen.
    """
    if t < 0.5:
        return 4 * t * t * t
    else:
        return 1 - pow(-2 * t + 2, 3) / 2


def ease_out_back(t: float) -> float:
    """Deceleration with slight overshoot/bounce back.

    Best for: playful UI elements, button press feedback, modal appearances.
    """
    c1 = 1.70158
    c3 = c1 + 1
    return 1 + c3 * pow(t - 1, 3) + c1 * pow(t - 1, 2)


def ease_out_elastic(t: float) -> float:
    """Elastic/spring effect.

    Best for: attention-grabbing animations, completion celebrations.
    """
    if t == 0:
        return 0
    if t == 1:
        return 1

    c4 = (2 * math.pi) / 3
    return pow(2, -10 * t) * math.sin((t * 10 - 0.75) * c4) + 1


def ease_out_quad(t: float) -> float:
    """Lighter deceleration curve.

    Best for: subtle transitions, color changes.
    """
    return 1 - (1 - t) * (1 - t)


def ease_out_quart(t: float) -> float:
    """Stronger deceleration curve.

    Best for: dramatic entrances, important UI changes.
    """
    return 1 - pow(1 - t, 4)


def linear(t: float) -> float:
    """Linear interpolation (no easing).

    Best for: progress bars, continuous animations.
    """
    return t
