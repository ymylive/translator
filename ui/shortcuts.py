"""Keyboard shortcuts manager for the application"""
from typing import Callable, Dict, Optional, Any
import tkinter as tk


class ShortcutManager:
    """Manages keyboard shortcuts for the application.

    Provides a centralized way to register and handle keyboard shortcuts.
    Supports modifier keys (Ctrl, Alt, Shift) and function keys.
    """

    def __init__(self, root: tk.Tk):
        """Initialize the shortcut manager.

        Args:
            root: The root Tkinter window
        """
        self.root = root
        self._shortcuts: Dict[str, Callable] = {}
        self._enabled = True

    def enable(self) -> None:
        """Enable all shortcuts."""
        self._enabled = True

    def disable(self) -> None:
        """Disable all shortcuts."""
        self._enabled = False

    def register(self, key_combo: str, callback: Callable, description: str = "") -> None:
        """Register a keyboard shortcut.

        Args:
            key_combo: Key combination (e.g., "Control-s", "Control-Return", "Escape")
            callback: Function to call when shortcut is triggered
            description: Optional description for the shortcut
        """
        self._shortcuts[key_combo] = callback
        self.root.bind(f"<{key_combo}>", self._create_handler(callback))

    def _create_handler(self, callback: Callable) -> Callable:
        """Create an event handler that respects enabled state."""
        def handler(event):
            if self._enabled:
                callback()
                return "break"  # Prevent event propagation
        return handler

    def unregister(self, key_combo: str) -> None:
        """Unregister a keyboard shortcut.

        Args:
            key_combo: Key combination to unregister
        """
        if key_combo in self._shortcuts:
            del self._shortcuts[key_combo]
            self.root.unbind(f"<{key_combo}>")

    def get_shortcuts(self) -> Dict[str, Callable]:
        """Get all registered shortcuts."""
        return self._shortcuts.copy()


def setup_app_shortcuts(app: Any) -> ShortcutManager:
    """Set up default application shortcuts.

    Args:
        app: The main application instance

    Returns:
        ShortcutManager instance with registered shortcuts
    """
    manager = ShortcutManager(app)

    # Ctrl+S: Save configuration
    manager.register("Control-s", app._save_config, "Save configuration")

    # Ctrl+Enter: Start translation
    manager.register("Control-Return", app._start, "Start translation")

    # Escape: Stop translation
    manager.register("Escape", app._stop, "Stop translation")

    # Ctrl+1~5: Switch tabs
    def switch_to_tab(index: int):
        def switch():
            tabs = ["⚙️ 基本设置", "📖 词典管理", "🔄 后处理规则", "🔧 高级设置", "📋 运行日志"]
            if 0 <= index < len(tabs):
                try:
                    app.tabview.set(tabs[index])
                except Exception:
                    pass
        return switch

    manager.register("Control-Key-1", switch_to_tab(0), "Switch to Basic Settings")
    manager.register("Control-Key-2", switch_to_tab(1), "Switch to Glossary")
    manager.register("Control-Key-3", switch_to_tab(2), "Switch to Post-process")
    manager.register("Control-Key-4", switch_to_tab(3), "Switch to Advanced")
    manager.register("Control-Key-5", switch_to_tab(4), "Switch to Log")

    return manager
