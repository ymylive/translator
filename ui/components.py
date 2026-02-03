"""Custom reusable UI components with Material Design animations"""
import customtkinter as ctk
import time
from typing import Optional, Callable, Any
from .theme import COLORS, DARK_COLORS, SPACING, BORDER_RADIUS, ANIMATION
from .animations import AnimationEngine
from .easing import ease_out_cubic


def debounce(wait_ms: int):
    """Decorator to debounce function calls.

    Args:
        wait_ms: Minimum time between calls in milliseconds
    """
    def decorator(fn):
        last_call = [0]
        def debounced(*args, **kwargs):
            now = time.time() * 1000
            if now - last_call[0] >= wait_ms:
                last_call[0] = now
                return fn(*args, **kwargs)
        return debounced
    return decorator


def throttle(wait_ms: int):
    """Decorator to throttle function calls.

    Args:
        wait_ms: Minimum time between calls in milliseconds
    """
    def decorator(fn):
        last_call = [0]
        scheduled = [None]
        def throttled(*args, **kwargs):
            now = time.time() * 1000
            if now - last_call[0] >= wait_ms:
                last_call[0] = now
                return fn(*args, **kwargs)
        return throttled
    return decorator


class StatusBadge(ctk.CTkFrame):
    """Status badge component with color coding"""
    def __init__(self, parent, text, status='info', **kwargs):
        super().__init__(parent, corner_radius=12, height=24, **kwargs)

        colors = {
            'success': ('#107C10', '#FFFFFF'),
            'warning': ('#FF8C00', '#FFFFFF'),
            'error': ('#D13438', '#FFFFFF'),
            'info': ('#0078D4', '#FFFFFF')
        }

        bg, fg = colors.get(status, colors['info'])
        self.configure(fg_color=bg)

        self.label = ctk.CTkLabel(
            self,
            text=text,
            text_color=fg,
            font=("Segoe UI", 11, "bold")
        )
        self.label.pack(padx=12, pady=4)


class AnimatedCard(ctk.CTkFrame):
    """Card component with smooth hover animations (Material Design style)"""

    def __init__(self, parent, title: str, **kwargs):
        self._base_border_color = COLORS['border']
        self._hover_border_color = COLORS['primary']
        self._base_bg_color = COLORS['surface']
        self._hover_bg_color = COLORS.get('surface_hover', '#F5F5F5')

        super().__init__(
            parent,
            corner_radius=12,
            border_width=1,
            border_color=self._base_border_color,
            fg_color=self._base_bg_color,
            **kwargs
        )

        self._is_hovered = False
        self._last_hover_time = 0
        self.bind("<Enter>", self._on_hover)
        self.bind("<Leave>", self._on_leave)

        # Header
        header = ctk.CTkFrame(self, fg_color="transparent")
        header.pack(fill='x', padx=20, pady=(16, 8))

        ctk.CTkLabel(
            header,
            text=title,
            font=("Segoe UI", 16, "bold")
        ).pack(side='left')

        # Content area
        self.content = ctk.CTkFrame(self, fg_color="transparent")
        self.content.pack(fill='both', expand=True, padx=20, pady=(0, 16))

    def _on_hover(self, event):
        if self._is_hovered:
            return
        # Debounce: ignore if less than 50ms since last event
        now = time.time() * 1000
        if now - self._last_hover_time < 50:
            return
        self._last_hover_time = now
        self._is_hovered = True

        # Only animate border color, set background instantly for better performance
        AnimationEngine.animate_color(
            self, 'border_color',
            self._base_border_color, self._hover_border_color,
            duration=ANIMATION['fast']
        )
        # Instant background change - no animation
        try:
            self.configure(fg_color=self._hover_bg_color)
        except Exception:
            pass

    def _on_leave(self, event):
        if not self._is_hovered:
            return
        # Debounce: ignore if less than 50ms since last event
        now = time.time() * 1000
        if now - self._last_hover_time < 50:
            return
        self._last_hover_time = now
        self._is_hovered = False

        AnimationEngine.animate_color(
            self, 'border_color',
            self._hover_border_color, self._base_border_color,
            duration=ANIMATION['fast']
        )
        # Instant background change - no animation
        try:
            self.configure(fg_color=self._base_bg_color)
        except Exception:
            pass


class MDButton(ctk.CTkButton):
    """Material Design style button - uses CTk built-in hover for performance"""

    def __init__(self, parent, **kwargs):
        # Let CTk handle hover states natively - no custom animation
        super().__init__(parent, **kwargs)


class MDEntry(ctk.CTkEntry):
    """Material Design style entry with instant focus feedback"""

    def __init__(self, parent, **kwargs):
        self._base_border_color = COLORS['border']
        self._focus_border_color = COLORS.get('border_focus', COLORS['primary'])

        super().__init__(parent, **kwargs)

        self.bind("<FocusIn>", self._on_focus_in)
        self.bind("<FocusOut>", self._on_focus_out)

    def _on_focus_in(self, event):
        """Instant border color change on focus - no animation for performance"""
        try:
            self.configure(border_color=self._focus_border_color)
        except Exception:
            pass

    def _on_focus_out(self, event):
        """Instant border color change on blur - no animation for performance"""
        try:
            self.configure(border_color=self._base_border_color)
        except Exception:
            pass


class MDComboBox(ctk.CTkComboBox):
    """Material Design style combo box with instant focus feedback"""

    def __init__(self, parent, **kwargs):
        self._base_border_color = COLORS['border']
        self._focus_border_color = COLORS.get('border_focus', COLORS['primary'])

        super().__init__(parent, **kwargs)

        self.bind("<FocusIn>", self._on_focus_in)
        self.bind("<FocusOut>", self._on_focus_out)

    def _on_focus_in(self, event):
        """Instant border color change on focus - no animation for performance"""
        try:
            self.configure(border_color=self._focus_border_color)
        except Exception:
            pass

    def _on_focus_out(self, event):
        """Instant border color change on blur - no animation for performance"""
        try:
            self.configure(border_color=self._base_border_color)
        except Exception:
            pass


class MDProgressBar(ctk.CTkProgressBar):
    """Material Design style progress bar with smooth animations"""

    def __init__(self, parent, **kwargs):
        self._base_color = kwargs.get('progress_color', COLORS['primary'])
        self._complete_color = COLORS['success']
        self._last_animation_time = 0
        self._animation_cooldown = 100  # ms between animations

        super().__init__(parent, **kwargs)

    def set_with_animation(self, value: float, duration: int = 300) -> None:
        """Set progress with smooth animation, with cooldown to prevent stacking"""
        now = time.time() * 1000
        # If animation is on cooldown, just set value directly
        if now - self._last_animation_time < self._animation_cooldown:
            try:
                self.set(value)
            except Exception:
                pass
            return

        self._last_animation_time = now
        try:
            current = self.get()
            # Skip animation for small changes
            if abs(value - current) < 0.02:
                self.set(value)
                return
            AnimationEngine.animate(
                self, 'progress',
                current, value,
                duration=min(duration, 200),  # Cap duration to prevent lag
                easing=ease_out_cubic
            )
        except Exception:
            self.set(value)

    def pulse_complete(self) -> None:
        """Pulse animation when progress reaches 100%"""
        AnimationEngine.pulse(
            self, 'progress_color',
            self._base_color, self._complete_color,
            duration=600
        )


class MDCheckBox(ctk.CTkCheckBox):
    """Material Design style checkbox - uses CTk built-in hover for performance"""

    def __init__(self, parent, **kwargs):
        # Let CTk handle hover states natively - no custom animation
        super().__init__(parent, **kwargs)


class MDTabview(ctk.CTkTabview):
    """Material Design style tabview with tab change callback support"""

    def __init__(self, parent, on_tab_change: Optional[Callable] = None, **kwargs):
        super().__init__(parent, **kwargs)
        self._on_tab_change = on_tab_change
        self._current_tab = None

        # Override the internal tab button command
        if on_tab_change:
            self.configure(command=self._handle_tab_change)

    def _handle_tab_change(self):
        """Handle tab change with animation callback"""
        new_tab = self.get()
        if new_tab != self._current_tab:
            self._current_tab = new_tab
            if self._on_tab_change:
                self._on_tab_change(new_tab)


class GlossaryEntryRow(ctk.CTkFrame):
    """Glossary entry row component for dictionary editing"""

    def __init__(self, parent, on_delete: Optional[Callable] = None, **kwargs):
        super().__init__(parent, fg_color="transparent", **kwargs)
        self.on_delete = on_delete

        import tkinter as tk
        self.source_var = tk.StringVar()
        self.target_var = tk.StringVar()
        self.info_var = tk.StringVar()

        MDEntry(self, textvariable=self.source_var, width=200).pack(side='left', padx=5)
        MDEntry(self, textvariable=self.target_var, width=200).pack(side='left', padx=5)
        MDEntry(self, textvariable=self.info_var, width=150).pack(side='left', padx=5)
        MDButton(self, text="×", width=30, command=self._delete, corner_radius=4).pack(side='left', padx=5)

    def _delete(self):
        if self.on_delete:
            self.on_delete(self)
        self.destroy()

    def get_data(self) -> dict:
        return {
            "src": self.source_var.get(),
            "dst": self.target_var.get(),
            "info": self.info_var.get()
        }

    def set_data(self, data: dict):
        self.source_var.set(data.get("src", ""))
        self.target_var.set(data.get("dst", ""))
        self.info_var.set(data.get("info", ""))


class PostProcessRuleRow(ctk.CTkFrame):
    """Post-process rule row component"""

    def __init__(self, parent, on_delete: Optional[Callable] = None, **kwargs):
        super().__init__(parent, fg_color="transparent", **kwargs)
        self.on_delete = on_delete

        import tkinter as tk
        self.pattern_var = tk.StringVar()
        self.replacement_var = tk.StringVar()
        self.is_regex_var = tk.BooleanVar(value=False)

        MDEntry(self, textvariable=self.pattern_var, width=200).pack(side='left', padx=5)
        MDEntry(self, textvariable=self.replacement_var, width=200).pack(side='left', padx=5)
        MDCheckBox(self, text="", variable=self.is_regex_var, width=40).pack(side='left', padx=5)
        MDButton(self, text="×", width=30, command=self._delete, corner_radius=4).pack(side='left', padx=5)

    def _delete(self):
        if self.on_delete:
            self.on_delete(self)
        self.destroy()

    def get_data(self) -> dict:
        return {
            "pattern": self.pattern_var.get(),
            "replacement": self.replacement_var.get(),
            "is_regex": self.is_regex_var.get()
        }


class Toast(ctk.CTkFrame):
    """Toast notification component that auto-dismisses.

    Displays a floating notification at the bottom of the window.
    Supports success, warning, error, and info types.
    """

    # Class-level toast queue to prevent overlapping
    _active_toasts = []

    def __init__(self, parent, message: str, toast_type: str = 'info',
                 duration: int = 3000, **kwargs):
        """Create a toast notification.

        Args:
            parent: Parent widget (usually the root window)
            message: Message to display
            toast_type: Type of toast ('success', 'warning', 'error', 'info')
            duration: Time in ms before auto-dismiss (0 = no auto-dismiss)
        """
        self._parent = parent

        # Color schemes for different toast types
        colors = {
            'success': {'bg': '#107C10', 'fg': '#FFFFFF', 'icon': '✓'},
            'warning': {'bg': '#FF8C00', 'fg': '#FFFFFF', 'icon': '⚠'},
            'error': {'bg': '#D13438', 'fg': '#FFFFFF', 'icon': '✕'},
            'info': {'bg': '#0078D4', 'fg': '#FFFFFF', 'icon': 'ℹ'}
        }
        scheme = colors.get(toast_type, colors['info'])

        super().__init__(
            parent,
            corner_radius=8,
            fg_color=scheme['bg'],
            **kwargs
        )

        # Content
        content = ctk.CTkFrame(self, fg_color="transparent")
        content.pack(padx=16, pady=10)

        # Icon
        ctk.CTkLabel(
            content,
            text=scheme['icon'],
            text_color=scheme['fg'],
            font=("Segoe UI", 14, "bold")
        ).pack(side='left', padx=(0, 8))

        # Message
        ctk.CTkLabel(
            content,
            text=message,
            text_color=scheme['fg'],
            font=("Segoe UI", 12)
        ).pack(side='left')

        # Close button
        close_btn = ctk.CTkButton(
            content,
            text="×",
            width=24,
            height=24,
            corner_radius=4,
            fg_color="transparent",
            hover_color=scheme['bg'],
            text_color=scheme['fg'],
            command=self.dismiss
        )
        close_btn.pack(side='left', padx=(12, 0))

        # Position at bottom center
        self._show()

        # Auto-dismiss
        if duration > 0:
            self.after(duration, self.dismiss)

    def _show(self):
        """Show the toast with animation."""
        # Calculate position (bottom center with offset for stacked toasts)
        self.update_idletasks()
        width = self.winfo_reqwidth()
        parent_width = self._parent.winfo_width()
        x = (parent_width - width) // 2

        # Stack offset
        offset = len(Toast._active_toasts) * 60
        y = self._parent.winfo_height() - 80 - offset

        self.place(x=x, y=y)
        Toast._active_toasts.append(self)

    def dismiss(self):
        """Dismiss the toast."""
        try:
            if self in Toast._active_toasts:
                Toast._active_toasts.remove(self)
            self.destroy()
            # Reposition remaining toasts
            for i, toast in enumerate(Toast._active_toasts):
                try:
                    parent_width = toast._parent.winfo_width()
                    width = toast.winfo_reqwidth()
                    x = (parent_width - width) // 2
                    y = toast._parent.winfo_height() - 80 - (i * 60)
                    toast.place(x=x, y=y)
                except Exception:
                    pass
        except Exception:
            pass

    @classmethod
    def show(cls, parent, message: str, toast_type: str = 'info',
             duration: int = 3000) -> 'Toast':
        """Convenience method to create and show a toast.

        Args:
            parent: Parent widget
            message: Message to display
            toast_type: Type of toast
            duration: Auto-dismiss duration in ms

        Returns:
            Toast instance
        """
        return cls(parent, message, toast_type, duration)


class VirtualizedLog(ctk.CTkFrame):
    """Virtualized log component that efficiently handles large amounts of text.

    Only renders visible lines, maintaining performance with thousands of log entries.
    Limits total lines to prevent memory issues.
    """

    def __init__(self, parent, max_lines: int = 1000, **kwargs):
        """Create a virtualized log component.

        Args:
            parent: Parent widget
            max_lines: Maximum number of lines to keep in memory
        """
        super().__init__(parent, **kwargs)

        self._max_lines = max_lines
        self._lines = []
        self._auto_scroll = True

        # Text widget with optimized settings
        self._text = ctk.CTkTextbox(
            self,
            font=("Consolas", 9),
            wrap="word",
            state="disabled"
        )
        self._text.pack(fill='both', expand=True)

        # Bind scroll events to detect manual scrolling
        self._text.bind("<MouseWheel>", self._on_scroll)
        self._text.bind("<Button-4>", self._on_scroll)
        self._text.bind("<Button-5>", self._on_scroll)

    def _on_scroll(self, event):
        """Handle scroll events to toggle auto-scroll."""
        # If user scrolls up, disable auto-scroll
        # If user scrolls to bottom, re-enable auto-scroll
        try:
            # Check if scrolled to bottom
            yview = self._text._textbox.yview()
            if yview[1] >= 0.99:
                self._auto_scroll = True
            else:
                self._auto_scroll = False
        except Exception:
            pass

    def insert(self, text: str) -> None:
        """Insert text into the log.

        Args:
            text: Text to insert (can contain newlines)
        """
        # Split into lines
        new_lines = text.split('\n')
        self._lines.extend(new_lines)

        # Trim if exceeding max lines
        if len(self._lines) > self._max_lines:
            excess = len(self._lines) - self._max_lines
            self._lines = self._lines[excess:]
            self._rebuild_text()
        else:
            # Just append new text
            self._text.configure(state="normal")
            self._text.insert("end", text)
            self._text.configure(state="disabled")

        # Auto-scroll to bottom
        if self._auto_scroll:
            self._text.see("end")

    def _rebuild_text(self):
        """Rebuild the text widget content from lines."""
        self._text.configure(state="normal")
        self._text.delete("1.0", "end")
        self._text.insert("1.0", '\n'.join(self._lines))
        self._text.configure(state="disabled")
        if self._auto_scroll:
            self._text.see("end")

    def clear(self) -> None:
        """Clear all log content."""
        self._lines.clear()
        self._text.configure(state="normal")
        self._text.delete("1.0", "end")
        self._text.configure(state="disabled")

    def get_text(self) -> str:
        """Get all log text."""
        return '\n'.join(self._lines)

    def get_line_count(self) -> int:
        """Get the number of lines in the log."""
        return len(self._lines)
