from typing import List, Tuple
from translators.base import TranslatorBase

try:
    import anthropic
    HAVE_ANTHROPIC = True
except ImportError:
    HAVE_ANTHROPIC = False


class ClaudeTranslator(TranslatorBase):
    def __init__(self, base_url: str, api_key: str, model: str):
        if not HAVE_ANTHROPIC:
            raise ImportError("anthropic package not installed. Run: pip install anthropic")
        self.api_key = api_key
        self.model = model
        self.client = anthropic.Anthropic(api_key=api_key)

    def translate(self, texts: List[str], source_lang: str, target_lang: str) -> List[str]:
        prompt = f"""Translate the following texts from {source_lang} to {target_lang}.
Return ONLY the translations, one per line, in the same order.
Do not add explanations or numbering.

Texts to translate:
{chr(10).join(f'{i+1}. {text}' for i, text in enumerate(texts))}"""

        message = self.client.messages.create(
            model=self.model,
            max_tokens=4096,
            messages=[{"role": "user", "content": prompt}]
        )

        result = message.content[0].text.strip()
        translations = [line.strip() for line in result.split('\n') if line.strip()]

        if len(translations) != len(texts):
            translations = texts

        return translations

    def get_supported_languages(self) -> Tuple[List[str], List[str]]:
        return (["auto"], ["trad_chinese", "simp_chinese", "english", "japanese", "korean"])

    def get_name(self) -> str:
        return "Claude"
