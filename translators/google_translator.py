from typing import List, Tuple
from translators.base import TranslatorBase

try:
    from google.cloud import translate_v2 as translate
    HAVE_GOOGLE = True
except ImportError:
    HAVE_GOOGLE = False


class GoogleTranslator(TranslatorBase):
    def __init__(self, base_url: str, api_key: str, model: str):
        if not HAVE_GOOGLE:
            raise ImportError("google-cloud-translate package not installed. Run: pip install google-cloud-translate")
        self.client = translate.Client(api_key=api_key)

    def translate(self, texts: List[str], source_lang: str, target_lang: str) -> List[str]:
        lang_map = {
            "trad_chinese": "zh-TW",
            "simp_chinese": "zh-CN",
            "english": "en",
            "japanese": "ja",
            "korean": "ko"
        }

        target = lang_map.get(target_lang, "en")
        source = None if source_lang == "auto" else source_lang

        results = self.client.translate(texts, target_language=target, source_language=source)

        if isinstance(results, list):
            return [r['translatedText'] for r in results]
        return [results['translatedText']]

    def get_supported_languages(self) -> Tuple[List[str], List[str]]:
        return (["auto"], ["trad_chinese", "simp_chinese", "english", "japanese", "korean"])

    def get_name(self) -> str:
        return "Google Translate"
