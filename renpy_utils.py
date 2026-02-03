"""
Tag extraction and restoration utilities for Ren'Py text.

Handles extraction and reinsertion of special tags like:
- Color tags: {color=#fff}text{/color}
- Variables: [name], [player_name]
- Size tags: {size=+10}text{/size}
- Other formatting tags

Inspired by teo-lin/renpy-translator's tag handling.
"""

import re
from typing import List, Tuple, Dict, Optional


class RenpyTagExtractor:
    """Extract and restore Ren'Py formatting tags."""

    # Pattern for Ren'Py tags
    TAG_PATTERNS = [
        # Color tags: {color=#fff}, {/color}
        (r'\{color=[^}]+\}', 'color_open'),
        (r'\{/color\}', 'color_close'),
        # Size tags: {size=+10}, {/size}
        (r'\{size=[^}]+\}', 'size_open'),
        (r'\{/size\}', 'size_close'),
        # Font tags: {font=...}, {/font}
        (r'\{font=[^}]+\}', 'font_open'),
        (r'\{/font\}', 'font_close'),
        # CPS (characters per second): {cps=20}, {/cps}
        (r'\{cps=\d+\}', 'cps_open'),
        (r'\{/cps\}', 'cps_close'),
        # Variables: [name], [player_name], etc.
        (r'\[[^\]]+\]', 'variable'),
        # Image tags: {image=...}
        (r'\{image=[^}]+\}', 'image'),
        # Wait/pause: {w}, {w=1.0}, {p}, {p=1.0}
        (r'\{[wp](?:=[\d.]+)?\}', 'wait'),
        # Fast display: {fast}
        (r'\{fast\}', 'fast'),
        # No wait: {nw}
        (r'\{nw\}', 'nw'),
        # Space: {space=10}
        (r'\{space=\d+\}', 'space'),
        # Vertical space: {vspace=10}
        (r'\{vspace=\d+\}', 'vspace'),
        # Alpha: {alpha=0.5}, {/alpha}
        (r'\{alpha=[^}]+\}', 'alpha_open'),
        (r'\{/alpha\}', 'alpha_close'),
        # Bold/Italic: {b}, {/b}, {i}, {/i}
        (r'\{b\}', 'bold_open'),
        (r'\{/b\}', 'bold_close'),
        (r'\{i\}', 'italic_open'),
        (r'\{/i\}', 'italic_close'),
        # Underline/Strikethrough: {u}, {/u}, {s}, {/s}
        (r'\{u\}', 'underline_open'),
        (r'\{/u\}', 'underline_close'),
        (r'\{s\}', 'strike_open'),
        (r'\{/s\}', 'strike_close'),
        # Generic curly brace tags
        (r'\{[^{}]+\}', 'generic'),
    ]

    # Combined pattern for all tags
    COMBINED_PATTERN = re.compile(
        '|'.join(f'({p[0]})' for p in TAG_PATTERNS)
    )

    def __init__(self):
        self._tag_counter = 0

    def extract_tags(self, text: str) -> Tuple[str, List[Tuple[int, str]]]:
        """
        Extract all tags from text, returning clean text and tag positions.

        Args:
            text: Original text with tags

        Returns:
            Tuple of (clean_text, [(position, tag), ...])
        """
        tags: List[Tuple[int, str]] = []
        clean_parts: List[str] = []
        last_end = 0
        clean_pos = 0

        for match in self.COMBINED_PATTERN.finditer(text):
            # Add text before this tag
            before = text[last_end:match.start()]
            clean_parts.append(before)
            clean_pos += len(before)

            # Record tag position (in clean text)
            tags.append((clean_pos, match.group(0)))

            last_end = match.end()

        # Add remaining text
        clean_parts.append(text[last_end:])

        return ''.join(clean_parts), tags

    def restore_tags(self, clean_text: str, tags: List[Tuple[int, str]]) -> str:
        """
        Restore tags to translated text.

        Args:
            clean_text: Translated text without tags
            tags: List of (position, tag) tuples

        Returns:
            Text with tags restored
        """
        if not tags:
            return clean_text

        # Sort tags by position (descending) to insert from end
        sorted_tags = sorted(tags, key=lambda x: x[0], reverse=True)

        result = clean_text
        for pos, tag in sorted_tags:
            # Clamp position to valid range
            pos = min(pos, len(result))
            result = result[:pos] + tag + result[pos:]

        return result

    def create_placeholders(self, text: str) -> Tuple[str, Dict[str, str]]:
        """
        Replace tags with placeholders for translation.

        Args:
            text: Original text with tags

        Returns:
            Tuple of (text_with_placeholders, {placeholder: original_tag})
        """
        placeholders: Dict[str, str] = {}
        self._tag_counter = 0

        def replace_tag(match: re.Match) -> str:
            tag = match.group(0)
            placeholder = f"<TAG{self._tag_counter}>"
            placeholders[placeholder] = tag
            self._tag_counter += 1
            return placeholder

        masked = self.COMBINED_PATTERN.sub(replace_tag, text)
        return masked, placeholders

    def restore_from_placeholders(self, text: str, placeholders: Dict[str, str]) -> str:
        """
        Restore original tags from placeholders.

        Args:
            text: Text with placeholders
            placeholders: Mapping of placeholder to original tag

        Returns:
            Text with original tags restored
        """
        result = text
        for placeholder, tag in placeholders.items():
            result = result.replace(placeholder, tag)
        return result

    def classify_tag(self, tag: str) -> str:
        """Classify a tag by type."""
        for pattern, tag_type in self.TAG_PATTERNS:
            if re.match(pattern, tag):
                return tag_type
        return 'unknown'


class TextPreprocessor:
    """Preprocess text for translation while preserving special elements."""

    def __init__(self):
        self.tag_extractor = RenpyTagExtractor()

    def preprocess(self, text: str) -> Tuple[str, Dict[str, any]]:
        """
        Preprocess text for translation.

        Returns:
            Tuple of (processed_text, metadata_for_restoration)
        """
        # Extract tags
        masked, placeholders = self.tag_extractor.create_placeholders(text)

        # Normalize whitespace
        normalized = ' '.join(masked.split())

        metadata = {
            'placeholders': placeholders,
            'original_whitespace': text != normalized,
        }

        return normalized, metadata

    def postprocess(self, text: str, metadata: Dict[str, any]) -> str:
        """
        Restore text after translation.

        Args:
            text: Translated text
            metadata: Metadata from preprocessing

        Returns:
            Restored text with original formatting
        """
        # Restore tags
        placeholders = metadata.get('placeholders', {})
        restored = self.tag_extractor.restore_from_placeholders(text, placeholders)

        return restored


# Convenience functions
def extract_tags(text: str) -> Tuple[str, List[Tuple[int, str]]]:
    """Extract tags from text."""
    return RenpyTagExtractor().extract_tags(text)


def restore_tags(clean_text: str, tags: List[Tuple[int, str]]) -> str:
    """Restore tags to text."""
    return RenpyTagExtractor().restore_tags(clean_text, tags)


def mask_tags(text: str) -> Tuple[str, Dict[str, str]]:
    """Replace tags with placeholders."""
    return RenpyTagExtractor().create_placeholders(text)


def unmask_tags(text: str, placeholders: Dict[str, str]) -> str:
    """Restore tags from placeholders."""
    return RenpyTagExtractor().restore_from_placeholders(text, placeholders)
