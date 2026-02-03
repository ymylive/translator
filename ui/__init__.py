"""Modern UI components and utilities with Material Design animations"""
from .theme import COLORS, DARK_COLORS, SPACING, BORDER_RADIUS, FONTS, ANIMATION
from .easing import (
    ease_out_cubic, ease_in_cubic, ease_in_out_cubic,
    ease_out_back, ease_out_elastic, ease_out_quad, ease_out_quart, linear
)
from .animations import AnimationManager, AnimationEngine
from .components import (
    StatusBadge, AnimatedCard,
    MDButton, MDEntry, MDComboBox, MDProgressBar, MDCheckBox, MDTabview,
    GlossaryEntryRow, PostProcessRuleRow,
    Toast, VirtualizedLog, debounce, throttle
)
from .shortcuts import ShortcutManager, setup_app_shortcuts

__all__ = [
    # Theme
    'COLORS', 'DARK_COLORS', 'SPACING', 'BORDER_RADIUS', 'FONTS', 'ANIMATION',
    # Easing functions
    'ease_out_cubic', 'ease_in_cubic', 'ease_in_out_cubic',
    'ease_out_back', 'ease_out_elastic', 'ease_out_quad', 'ease_out_quart', 'linear',
    # Animation
    'AnimationManager', 'AnimationEngine',
    # Components
    'StatusBadge', 'AnimatedCard',
    'MDButton', 'MDEntry', 'MDComboBox', 'MDProgressBar', 'MDCheckBox', 'MDTabview',
    'GlossaryEntryRow', 'PostProcessRuleRow',
    'Toast', 'VirtualizedLog',
    # Utilities
    'debounce', 'throttle',
    # Shortcuts
    'ShortcutManager', 'setup_app_shortcuts'
]
