"""
Prompt template system for translation.

Inspired by LunaTranslator's gptcommon.py prompt handling.
Supports customizable system and user prompts with placeholders.
"""

import re
from pathlib import Path
from typing import Dict, List, Optional
from dataclasses import dataclass

from config_utils import safe_save_json, safe_load_json


# Default prompts (LunaTranslator style)
DEFAULT_SYSTEM_PROMPT = """You are a professional translator. Please translate the following {srclang} text into {tgtlang}.
You should only output the translation result without any additional explanations or notes.
Maintain the original formatting, including line breaks and special characters."""

DEFAULT_USER_PROMPT = """{DictWithPrompt[When translating, please use the following glossary for specific terms:
]}
{sentence}"""

# Language name mappings
LANGUAGE_NAMES = {
    "zh-CN": "Simplified Chinese",
    "zh-TW": "Traditional Chinese",
    "trad_chinese": "Traditional Chinese",
    "simp_chinese": "Simplified Chinese",
    "en": "English",
    "ja": "Japanese",
    "ko": "Korean",
    "fr": "French",
    "de": "German",
    "es": "Spanish",
    "ru": "Russian",
    "pt": "Portuguese",
    "it": "Italian",
    "auto": "the source language",
}


@dataclass
class PromptTemplate:
    """A prompt template with metadata."""
    name: str
    system_prompt: str
    user_prompt: str
    description: str = ""

    def to_dict(self) -> Dict:
        return {
            "name": self.name,
            "system_prompt": self.system_prompt,
            "user_prompt": self.user_prompt,
            "description": self.description,
        }

    @classmethod
    def from_dict(cls, data: Dict) -> "PromptTemplate":
        return cls(
            name=data.get("name", "Custom"),
            system_prompt=data.get("system_prompt", DEFAULT_SYSTEM_PROMPT),
            user_prompt=data.get("user_prompt", DEFAULT_USER_PROMPT),
            description=data.get("description", ""),
        )


class PromptManager:
    """
    Manages prompt templates for translation.

    Features:
    - Multiple template support
    - Placeholder rendering
    - Glossary integration
    - Template persistence
    """

    def __init__(self):
        self.templates: Dict[str, PromptTemplate] = {}
        self.current_template: str = "default"
        self._init_default_templates()

    def _init_default_templates(self):
        """Initialize built-in templates."""
        self.templates["default"] = PromptTemplate(
            name="default",
            system_prompt=DEFAULT_SYSTEM_PROMPT,
            user_prompt=DEFAULT_USER_PROMPT,
            description="Default translation prompt",
        )

        self.templates["literal"] = PromptTemplate(
            name="literal",
            system_prompt="""You are a literal translator. Translate the following {srclang} text into {tgtlang} as literally as possible.
Preserve the original sentence structure and word order where grammatically acceptable.
Do not add interpretations or explanations.""",
            user_prompt="{sentence}",
            description="Literal translation style",
        )

        self.templates["natural"] = PromptTemplate(
            name="natural",
            system_prompt="""You are a professional translator specializing in natural, fluent translations.
Translate the following {srclang} text into {tgtlang} in a way that sounds natural to native speakers.
Adapt idioms and expressions appropriately while preserving the original meaning.""",
            user_prompt="""{DictWithPrompt[Use these translations for specific terms:
]}
{sentence}""",
            description="Natural, fluent translation style",
        )

        self.templates["game"] = PromptTemplate(
            name="game",
            system_prompt="""You are a game localization expert. Translate the following {srclang} game text into {tgtlang}.
Preserve all formatting tags, placeholders like [name], {w}, {p}, and special characters.
Maintain the tone and style appropriate for games (casual, dramatic, etc. based on context).""",
            user_prompt="""{DictWithPrompt[Character and term translations:
]}
{sentence}""",
            description="Game localization style",
        )

    def get_template(self, name: Optional[str] = None) -> PromptTemplate:
        """Get a template by name or the current template."""
        template_name = name or self.current_template
        return self.templates.get(template_name, self.templates["default"])

    def add_template(self, template: PromptTemplate) -> None:
        """Add or update a template."""
        self.templates[template.name] = template

    def remove_template(self, name: str) -> bool:
        """Remove a template (cannot remove built-in templates)."""
        if name in ["default", "literal", "natural", "game"]:
            return False
        if name in self.templates:
            del self.templates[name]
            if self.current_template == name:
                self.current_template = "default"
            return True
        return False

    def render_system_prompt(
        self,
        src_lang: str,
        tgt_lang: str,
        template_name: Optional[str] = None
    ) -> str:
        """
        Render system prompt with language placeholders.

        Args:
            src_lang: Source language code
            tgt_lang: Target language code
            template_name: Optional template name

        Returns:
            Rendered system prompt
        """
        template = self.get_template(template_name)
        prompt = template.system_prompt

        # Replace language placeholders
        src_name = LANGUAGE_NAMES.get(src_lang, src_lang)
        tgt_name = LANGUAGE_NAMES.get(tgt_lang, tgt_lang)

        prompt = prompt.replace("{srclang}", src_name)
        prompt = prompt.replace("{tgtlang}", tgt_name)

        return prompt

    def render_user_prompt(
        self,
        sentence: str,
        glossary: Optional[List[Dict]] = None,
        template_name: Optional[str] = None
    ) -> str:
        """
        Render user prompt with sentence and glossary.

        Args:
            sentence: Text to translate
            glossary: Optional list of glossary entries [{src, dst, info}, ...]
            template_name: Optional template name

        Returns:
            Rendered user prompt
        """
        template = self.get_template(template_name)
        prompt = template.user_prompt

        # Handle {DictWithPrompt[...]} placeholder
        dict_pattern = r"\{DictWithPrompt\[(.*?)\]\}"
        match = re.search(dict_pattern, prompt, re.DOTALL)

        if match:
            prefix = match.group(1)
            if glossary:
                dict_text = "\n".join([
                    f"  {g.get('src', '')} -> {g.get('dst', '')}"
                    + (f" ({g.get('info', '')})" if g.get('info') else "")
                    for g in glossary
                ])
                replacement = prefix + dict_text + "\n"
            else:
                replacement = ""
            prompt = re.sub(dict_pattern, replacement, prompt, flags=re.DOTALL)

        # Replace sentence placeholder
        prompt = prompt.replace("{sentence}", sentence)

        return prompt

    def load_from_file(self, path: Path) -> int:
        """Load custom templates from file."""
        data = safe_load_json(path, {"templates": [], "current": "default"})

        for template_data in data.get("templates", []):
            template = PromptTemplate.from_dict(template_data)
            self.templates[template.name] = template

        self.current_template = data.get("current", "default")
        return len(data.get("templates", []))

    def save_to_file(self, path: Path) -> bool:
        """Save custom templates to file."""
        # Only save non-built-in templates
        custom_templates = [
            t.to_dict() for name, t in self.templates.items()
            if name not in ["default", "literal", "natural", "game"]
        ]

        data = {
            "version": "1.0",
            "current": self.current_template,
            "templates": custom_templates,
        }
        return safe_save_json(path, data)

    @property
    def template_names(self) -> List[str]:
        """Get list of available template names."""
        return list(self.templates.keys())


# Global instance for convenience
_default_manager: Optional[PromptManager] = None


def get_prompt_manager() -> PromptManager:
    """Get the global prompt manager instance."""
    global _default_manager
    if _default_manager is None:
        _default_manager = PromptManager()
    return _default_manager
