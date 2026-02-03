"""Theme configuration for modern UI design"""

# Light theme colors
COLORS = {
    'primary': '#0078D4',
    'primary_hover': '#106EBE',
    'success': '#107C10',
    'warning': '#FF8C00',
    'error': '#D13438',
    'background': '#F3F3F3',
    'surface': '#FFFFFF',
    'text_primary': '#1F1F1F',
    'text_secondary': '#605E5C',
    'border': '#E1DFDD',
    'shadow': 'rgba(0,0,0,0.1)'
}

# Dark theme colors
DARK_COLORS = {
    'primary': '#60CDFF',
    'primary_hover': '#4DB8E8',
    'success': '#6CCB5F',
    'warning': '#FFA94D',
    'error': '#F1707B',
    'background': '#1F1F1F',
    'surface': '#2D2D2D',
    'text_primary': '#FFFFFF',
    'text_secondary': '#C8C6C4',
    'border': '#3B3B3B',
    'shadow': 'rgba(0,0,0,0.3)'
}

# Spacing scale
SPACING = {
    'xs': 4,
    'sm': 8,
    'md': 16,
    'lg': 24,
    'xl': 32
}

# Border radius scale
BORDER_RADIUS = {
    'sm': 4,
    'md': 8,
    'lg': 12,
    'xl': 16
}

# Typography
FONTS = {
    'title': ('Segoe UI', 24, 'bold'),
    'heading': ('Segoe UI', 16, 'bold'),
    'subheading': ('Segoe UI', 14, 'bold'),
    'body': ('Segoe UI', 11),
    'small': ('Segoe UI', 10),
    'code': ('Consolas', 9)
}

# Animation durations (milliseconds) - optimized for performance
ANIMATION = {
    'fast': 100,      # Quick feedback (clicks, micro-interactions)
    'normal': 150,    # Standard transitions (hover, focus)
    'slow': 250,      # Slower animations (page transitions, modals)
}

# Extended colors for hover states
COLORS['surface_hover'] = '#F5F5F5'      # Card hover background
COLORS['primary_light'] = '#E6F2FF'      # Input focus background
COLORS['border_focus'] = '#0078D4'       # Focused element border

DARK_COLORS['surface_hover'] = '#383838'
DARK_COLORS['primary_light'] = '#1A3A5C'
DARK_COLORS['border_focus'] = '#60CDFF'
