from typing import List, Tuple
from translators.base import TranslatorBase

try:
    import deepl
    HAVE_DEEPL = True
except ImportError:
    HAVE_DEEPL = False


class DeeplTranslator(TranslatorBase):
    def __init__(self, base_url: str, api_key: str, model: str):
        if not HAVE_DEEPL:
            raise ImportError("deepl package not installed. Run: pip install deepl")
        self.api_key = api_key
        self.translator = deepl.Translator(api_key)

    def translate(self, texts: List[str], source_lang: str, target_lang: str) -> List[str]:
        lang_map = {
            "trad_chinese": "ZH-HANT",
            "simp_chinese": "ZH-HANS",
            "english": "EN-US",
            "japanese": "JA",
            "korean": "KO"
        }

        target = lang_map.get(target_lang, "EN-US")
        source = None if source_lang == "auto" else source_lang.upper()

        results = self.translator.translate_text(texts, source_lang=source, target_lang=target)

        if isinstance(results, list):
            return [r.text for r in results]
        return [results.text]

    def get_supported_languages(self) -> Tuple[List[str], List[str]]:
        return (["auto"], ["trad_chinese", "simp_chinese", "english", "japanese", "korean"])

    def get_name(self) -> str:
        return "DeepL"
