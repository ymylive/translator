import json
import re
import time
import urllib.request
from json import JSONDecodeError
from typing import List, Optional, Tuple, Generator

from .base import TranslatorBase

NL_TOKEN = "<NL>"


class OpenAITranslator(TranslatorBase):
    """OpenAI-compatible API translator with enhanced features."""

    def __init__(self, base_url: str, api_key: str, model: str, timeout: int = 60):
        super().__init__()  # Initialize base class
        self.base_url = base_url
        self.api_key = api_key
        self.model = model
        self.timeout = timeout
        self._request_interval = 0.5  # Override default rate limit
        # Custom prompt support
        self.system_prompt: Optional[str] = None
        self.user_prompt_template: Optional[str] = None

    def get_name(self) -> str:
        return "OpenAI"

    def get_supported_languages(self) -> Tuple[List[str], List[str]]:
        # OpenAI models support most languages
        return (["auto"], ["zh-CN", "zh-TW", "en", "ja", "ko", "fr", "de", "es"])

    def translate(self, texts: List[str], source_lang: str, target_lang: str) -> List[str]:
        return self._call_api(texts, target_lang)

    def _call_api(self, texts: List[str], target_lang: str, max_retries: int = 0) -> List[str]:
        def parse_indexed_parts(parts: List[str], expected: int) -> List[str]:
            indexed: List[Optional[str]] = [None] * expected
            seen = set()
            all_indexed = True
            for part in parts:
                part = part.strip()
                match = re.match(r"^(\d+)\|(.*)$", part, re.S)
                if not match:
                    all_indexed = False
                    break
                idx = int(match.group(1)) - 1
                if idx < 0 or idx >= expected or idx in seen:
                    all_indexed = False
                    break
                indexed[idx] = match.group(2)
                seen.add(idx)
            if all_indexed and all(v is not None for v in indexed):
                return [v for v in indexed if v is not None]
            cleaned = []
            for part in parts:
                part = part.strip()
                match = re.match(r"^(\d+)\|(.*)$", part, re.S)
                cleaned.append(match.group(2) if match else part)
            return cleaned

        delim = f"<<<RENpySEP:{int(time.time() * 1000)}>>>"
        system_prompt = (
            "You are a translation engine. Translate each input to Simplified Chinese. "
            "Keep placeholders like <P0>, <P1> unchanged. "
            "If you see <NL>, keep it as <NL>. "
            "There are N inputs; output exactly N items in the same order. "
            "Each item must keep its original index prefix in the format: index|translation. "
            "Return ONLY the items joined by the exact delimiter below, with no extra text."
        )

        joined = delim.join([f"{i + 1}|{t.replace('\n', NL_TOKEN)}" for i, t in enumerate(texts)])
        payload = {
            "model": self.model,
            "messages": [
                {"role": "system", "content": system_prompt},
                {
                    "role": "user",
                    "content": (
                        f"DELIMITER:\n{delim}\n"
                        "INPUTS (format index|text):\n"
                        f"{joined}\n"
                        "Output format (must match): index|translation joined by delimiter."
                    ),
                },
            ],
            "temperature": 0.2,
        }

        data = json.dumps(payload).encode("utf-8")
        req = urllib.request.Request(
            self.base_url.rstrip("/") + "/chat/completions",
            data=data,
            headers={
                "Content-Type": "application/json",
                "Authorization": f"Bearer {self.api_key}",
                "HTTP-Referer": "http://localhost",
                "X-Title": "renpy-translate",
            },
        )

        with urllib.request.urlopen(req, timeout=self.timeout) as resp:
            raw = resp.read().decode("utf-8", errors="replace")

        def normalize_parts(parts: List[str], expected: int) -> List[str]:
            if len(parts) == expected:
                return parts
            if len(parts) == expected + 1 and parts and parts[0].strip() == "":
                return parts[1:]
            if len(parts) == expected + 1 and parts and parts[-1].strip() == "":
                return parts[:-1]
            if len(parts) == expected + 2 and parts and parts[0].strip() == "" and parts[-1].strip() == "":
                return parts[1:-1]
            return parts

        def split_by_delim(content: str, expected: int) -> List[str]:
            return normalize_parts(content.split(delim), expected)

        try:
            parsed = json.loads(raw)
        except JSONDecodeError as exc:
            lines = [line.strip() for line in raw.splitlines() if line.strip().startswith("data:")]
            if lines:
                chunks: List[str] = []
                for line in lines:
                    data = line[len("data:") :].strip()
                    if data == "[DONE]":
                        continue
                    try:
                        obj = json.loads(data)
                    except JSONDecodeError:
                        continue
                    choice = obj.get("choices", [{}])[0]
                    if "message" in choice and "content" in choice["message"]:
                        content = choice["message"]["content"]
                        return parse_indexed_parts(split_by_delim(content, len(texts)), len(texts))
                    delta = choice.get("delta", {}).get("content")
                    if delta:
                        chunks.append(delta)
                if chunks:
                    content = "".join(chunks)
                    return parse_indexed_parts(split_by_delim(content, len(texts)), len(texts))

            start = raw.find("{")
            end = raw.rfind("}")
            if start != -1 and end != -1 and end > start:
                try:
                    parsed = json.loads(raw[start : end + 1])
                except JSONDecodeError:
                    raise RuntimeError(f"Invalid JSON response: {exc}. Raw: {raw[:500]}")
            else:
                raise RuntimeError(f"Invalid JSON response: {exc}. Raw: {raw[:500]}")
        if "choices" not in parsed:
            if "error" in parsed:
                raise RuntimeError(f"API error: {parsed['error']}")
            raise RuntimeError(f"Unexpected API response: {raw[:500]}")
        content = parsed["choices"][0]["message"]["content"]

        if "```" in content:
            parts = content.split("```")
            if len(parts) >= 2:
                content = parts[1]
                if "\n" in content:
                    content = content.split("\n", 1)[1]

        if delim not in content:
            start = content.find("[")
            end = content.rfind("]")
            if start != -1 and end != -1:
                try:
                    return json.loads(content[start : end + 1])
                except Exception:
                    pass
            lines = [line.strip() for line in content.splitlines() if line.strip() != ""]
            if len(lines) == len(texts):
                return parse_indexed_parts(lines, len(texts))
            raise RuntimeError(f"Unexpected response format: {content[:500]}")

        parts = split_by_delim(content, len(texts))
        if len(parts) == len(texts):
            return parse_indexed_parts(parts, len(texts))

        start = content.find("[")
        end = content.rfind("]")
        if start != -1 and end != -1:
            try:
                alt = json.loads(content[start : end + 1])
                if isinstance(alt, list) and len(alt) == len(texts):
                    return alt
            except Exception:
                pass

        lines = [line.strip() for line in content.splitlines() if line.strip() != ""]
        if len(lines) == len(texts):
            return lines

        if max_retries > 0:
            strict_prompt = (
                system_prompt
                + " If you are unsure, output the source text unchanged for that item."
            )
            payload["messages"][0]["content"] = strict_prompt
            data_retry = json.dumps(payload).encode("utf-8")
            req_retry = urllib.request.Request(
                self.base_url.rstrip("/") + "/chat/completions",
                data=data_retry,
                headers={
                    "Content-Type": "application/json",
                    "Authorization": f"Bearer {self.api_key}",
                    "HTTP-Referer": "http://localhost",
                    "X-Title": "renpy-translate",
                },
            )
            with urllib.request.urlopen(req_retry, timeout=self.timeout) as resp:
                raw_retry = resp.read().decode("utf-8", errors="replace")
            try:
                parsed_retry = json.loads(raw_retry)
                content_retry = parsed_retry["choices"][0]["message"]["content"]
                if "```" in content_retry:
                    parts_retry = content_retry.split("```")
                    if len(parts_retry) >= 2:
                        content_retry = parts_retry[1]
                        if "\n" in content_retry:
                            content_retry = content_retry.split("\n", 1)[1]
                if delim in content_retry:
                    parts_retry = split_by_delim(content_retry, len(texts))
                    if len(parts_retry) == len(texts):
                        return parse_indexed_parts(parts_retry, len(texts))
            except Exception:
                pass

            return self._call_api(texts, target_lang, max_retries - 1)

        raise RuntimeError(f"Translation count mismatch. Expected {len(texts)}, got {len(parts)}.")

    def translate_streaming(
        self,
        texts: List[str],
        target_lang: str,
        on_chunk: Optional[callable] = None
    ) -> Generator[str, None, str]:
        """
        Stream translation results in real-time.

        Yields partial results as they arrive from the API.
        LunaTranslator-style streaming support.

        Args:
            texts: Texts to translate
            target_lang: Target language
            on_chunk: Optional callback for each chunk

        Yields:
            Partial translation results

        Returns:
            Final complete translation
        """
        delim = f"<<<RENpySEP:{int(time.time() * 1000)}>>>"
        system_prompt = self.system_prompt or (
            "You are a translation engine. Translate each input to Simplified Chinese. "
            "Keep placeholders like <P0>, <P1> unchanged. "
            "If you see <NL>, keep it as <NL>. "
            "There are N inputs; output exactly N items in the same order. "
            "Each item must keep its original index prefix in the format: index|translation. "
            "Return ONLY the items joined by the exact delimiter below, with no extra text."
        )

        joined = delim.join([f"{i + 1}|{t.replace(chr(10), NL_TOKEN)}" for i, t in enumerate(texts)])
        payload = {
            "model": self.model,
            "messages": [
                {"role": "system", "content": system_prompt},
                {
                    "role": "user",
                    "content": (
                        f"DELIMITER:\n{delim}\n"
                        "INPUTS (format index|text):\n"
                        f"{joined}\n"
                        "Output format (must match): index|translation joined by delimiter."
                    ),
                },
            ],
            "temperature": 0.2,
            "stream": True,
        }

        data = json.dumps(payload).encode("utf-8")
        req = urllib.request.Request(
            self.base_url.rstrip("/") + "/chat/completions",
            data=data,
            headers={
                "Content-Type": "application/json",
                "Authorization": f"Bearer {self.api_key}",
                "HTTP-Referer": "http://localhost",
                "X-Title": "renpy-translate",
            },
        )

        buffer = ""
        with urllib.request.urlopen(req, timeout=self.timeout) as resp:
            for line in resp:
                line = line.decode("utf-8").strip()
                if not line.startswith("data: "):
                    continue

                data_str = line[6:]
                if data_str == "[DONE]":
                    break

                try:
                    obj = json.loads(data_str)
                    delta = obj.get("choices", [{}])[0].get("delta", {}).get("content", "")
                    if delta:
                        buffer += delta
                        if on_chunk:
                            on_chunk(buffer)
                        yield buffer
                except (JSONDecodeError, KeyError, IndexError):
                    continue

        return buffer

    def set_custom_prompts(
        self,
        system_prompt: Optional[str] = None,
        user_prompt_template: Optional[str] = None
    ):
        """
        Set custom prompts for translation.

        Args:
            system_prompt: Custom system prompt
            user_prompt_template: Custom user prompt template with {sentence} placeholder
        """
        self.system_prompt = system_prompt
        self.user_prompt_template = user_prompt_template

