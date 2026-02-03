"""Animation engine for Material Design style smooth UI transitions"""
from typing import Callable, Optional, Any, Dict, Tuple
from .easing import ease_out_cubic, ease_out_quad


# Pre-computed RGB cache to avoid repeated hex parsing
_rgb_cache: Dict[str, Tuple[int, int, int]] = {}


def _hex_to_rgb(hex_color: str) -> Tuple[int, int, int]:
    """Convert hex color to RGB tuple with caching."""
    if hex_color in _rgb_cache:
        return _rgb_cache[hex_color]
    clean = hex_color.lstrip('#')
    result = (int(clean[0:2], 16), int(clean[2:4], 16), int(clean[4:6], 16))
    _rgb_cache[hex_color] = result
    return result


def _rgb_to_hex(r: int, g: int, b: int) -> str:
    """Convert RGB tuple to hex color."""
    return f'#{int(r):02x}{int(g):02x}{int(b):02x}'


def _interpolate_color(start_rgb: Tuple[int, int, int],
                       end_rgb: Tuple[int, int, int],
                       t: float) -> str:
    """Interpolate between two colors."""
    r = start_rgb[0] + (end_rgb[0] - start_rgb[0]) * t
    g = start_rgb[1] + (end_rgb[1] - start_rgb[1]) * t
    b = start_rgb[2] + (end_rgb[2] - start_rgb[2]) * t
    return _rgb_to_hex(r, g, b)


class AnimationEngine:
    """Core animation engine managing all UI animations.

    Features:
    - Global animation enable/disable switch for performance mode
    - Prevents animation stacking (cancels previous animation on same property)
    - Supports custom easing functions
    - Handles color interpolation with caching
    - Provides completion callbacks
    """

    _active_animations: Dict[str, str] = {}  # widget_id:property -> animation_id
    _animation_counter = 0
    _animations_enabled = True  # Global animation switch

    @classmethod
    def set_enabled(cls, enabled: bool) -> None:
        """Enable or disable all animations globally."""
        cls._animations_enabled = enabled
        if not enabled:
            # Clear all active animations when disabled
            cls._active_animations.clear()

    @classmethod
    def is_enabled(cls) -> bool:
        """Check if animations are enabled."""
        return cls._animations_enabled

    @classmethod
    def _get_animation_key(cls, widget: Any, property_name: str) -> str:
        """Generate unique key for widget+property combination."""
        return f"{id(widget)}:{property_name}"

    @classmethod
    def _generate_animation_id(cls) -> str:
        """Generate unique animation ID."""
        cls._animation_counter += 1
        return f"anim_{cls._animation_counter}"

    @classmethod
    def cancel(cls, widget: Any, property_name: str) -> None:
        """Cancel any running animation on the specified widget property."""
        key = cls._get_animation_key(widget, property_name)
        if key in cls._active_animations:
            del cls._active_animations[key]

    @classmethod
    def animate(cls, widget: Any, property_name: str,
                start: float, end: float,
                duration: int = 300,
                easing: Callable[[float], float] = ease_out_cubic,
                on_complete: Optional[Callable] = None,
                setter: Optional[Callable[[float], None]] = None) -> None:
        """Animate a numeric property from start to end value.

        Args:
            widget: The widget to animate
            property_name: Name of the property (for tracking)
            start: Starting value
            end: Ending value
            duration: Animation duration in milliseconds
            easing: Easing function to use
            on_complete: Callback when animation completes
            setter: Custom setter function, defaults to widget.set()
        """
        # Skip animation if disabled - apply final value immediately
        if not cls._animations_enabled:
            try:
                if setter:
                    setter(end)
                elif hasattr(widget, 'set'):
                    widget.set(end)
            except Exception:
                pass
            if on_complete:
                on_complete()
            return

        # Cancel any existing animation on this property
        key = cls._get_animation_key(widget, property_name)
        animation_id = cls._generate_animation_id()
        cls._active_animations[key] = animation_id

        # Optimized frame rate: ~60fps for smooth animations
        steps = max(int(duration / 16), 1)
        delay = duration / steps

        def update_step(step: int):
            # Check if this animation was cancelled
            if cls._active_animations.get(key) != animation_id:
                return

            if step > steps:
                # Animation complete
                if key in cls._active_animations:
                    del cls._active_animations[key]
                if on_complete:
                    on_complete()
                return

            t = step / steps
            eased_t = easing(t)
            value = start + (end - start) * eased_t

            try:
                if setter:
                    setter(value)
                elif hasattr(widget, 'set'):
                    widget.set(value)
            except Exception:
                # Widget may have been destroyed
                if key in cls._active_animations:
                    del cls._active_animations[key]
                return

            widget.after(int(delay), lambda: update_step(step + 1))

        update_step(0)

    @classmethod
    def animate_color(cls, widget: Any, property_name: str,
                      start_color: str, end_color: str,
                      duration: int = 250,
                      easing: Callable[[float], float] = ease_out_quad,
                      on_complete: Optional[Callable] = None) -> None:
        """Animate a color property smoothly.

        Args:
            widget: The widget to animate
            property_name: Property name ('fg_color', 'border_color', etc.)
            start_color: Starting hex color
            end_color: Ending hex color
            duration: Animation duration in milliseconds
            easing: Easing function to use
            on_complete: Callback when animation completes
        """
        # Skip animation if disabled - apply final color immediately
        if not cls._animations_enabled:
            try:
                widget.configure(**{property_name: end_color})
            except Exception:
                pass
            if on_complete:
                on_complete()
            return

        key = cls._get_animation_key(widget, property_name)
        animation_id = cls._generate_animation_id()
        cls._active_animations[key] = animation_id

        try:
            start_rgb = _hex_to_rgb(start_color)
            end_rgb = _hex_to_rgb(end_color)
        except (ValueError, TypeError):
            return

        # Optimized frame rate: ~60fps for smooth animations
        steps = max(int(duration / 16), 1)
        delay = duration / steps

        def update_step(step: int):
            if cls._active_animations.get(key) != animation_id:
                return

            if step > steps:
                if key in cls._active_animations:
                    del cls._active_animations[key]
                if on_complete:
                    on_complete()
                return

            t = step / steps
            eased_t = easing(t)
            color = _interpolate_color(start_rgb, end_rgb, eased_t)

            try:
                widget.configure(**{property_name: color})
            except Exception:
                if key in cls._active_animations:
                    del cls._active_animations[key]
                return

            widget.after(int(delay), lambda: update_step(step + 1))

        update_step(0)

    @classmethod
    def fade_in(cls, widget: Any, duration: int = 200,
                on_complete: Optional[Callable] = None) -> None:
        """Fade in a widget by animating its opacity.

        Note: CustomTkinter doesn't support true opacity, so this simulates
        fade by animating from background color to final color.
        """
        # For CTk widgets, we can't truly fade, but we can animate visibility
        try:
            widget.configure(fg_color=widget.cget('fg_color'))
        except Exception:
            pass

        if on_complete:
            widget.after(duration, on_complete)

    @classmethod
    def pulse(cls, widget: Any, property_name: str,
              base_color: str, pulse_color: str,
              duration: int = 600,
              on_complete: Optional[Callable] = None) -> None:
        """Create a pulse effect (color flash and return).

        Args:
            widget: The widget to pulse
            property_name: Color property to animate
            base_color: The base/resting color
            pulse_color: The highlight color to pulse to
            duration: Total duration of pulse cycle
            on_complete: Callback when pulse completes
        """
        # Skip pulse if animations disabled
        if not cls._animations_enabled:
            if on_complete:
                on_complete()
            return

        half_duration = duration // 2

        def return_to_base():
            cls.animate_color(
                widget, property_name,
                pulse_color, base_color,
                duration=half_duration,
                on_complete=on_complete
            )

        cls.animate_color(
            widget, property_name,
            base_color, pulse_color,
            duration=half_duration,
            on_complete=return_to_base
        )

    @classmethod
    def scale_bounce(cls, widget: Any, duration: int = 150,
                     scale_factor: float = 0.95) -> None:
        """Create a scale bounce effect for button clicks.

        Note: CTk doesn't support true scaling, so this is a visual placeholder
        that could be enhanced with canvas-based widgets.
        """
        # This is a stub - true scaling would require canvas manipulation
        pass


class AnimationManager:
    """Legacy compatibility wrapper for AnimationEngine.

    Maintains backward compatibility with existing code.
    """

    @staticmethod
    def progress_smooth(progressbar: Any, target_value: float,
                        duration: int = 500) -> None:
        """Animate progress bar smoothly to target value."""
        try:
            current = progressbar.get()
            AnimationEngine.animate(
                progressbar, 'progress',
                current, target_value,
                duration=duration,
                easing=ease_out_cubic
            )
        except Exception:
            # Fallback to instant update
            try:
                progressbar.set(target_value)
            except Exception:
                pass

    @staticmethod
    def fade_in(widget: Any, duration: int = 300,
                callback: Optional[Callable] = None) -> None:
        """Fade in animation for widgets."""
        AnimationEngine.fade_in(widget, duration, callback)
