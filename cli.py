import argparse
from pathlib import Path

from translator_core import Translator


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, help="Game root directory.")
    parser.add_argument("--language", default="trad_chinese")
    parser.add_argument("--base-url", default="https://openrouter.ai/api/v1")
    parser.add_argument("--api-key", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--batch", type=int, default=150)
    parser.add_argument("--max-chars", type=int, default=12000)
    parser.add_argument("--workers", type=int, default=2)
    parser.add_argument("--work", default=str(Path(__file__).parent / "work"))
    parser.add_argument("--force-language", action="store_true")
    args = parser.parse_args()

    translator = Translator(
        game_root=Path(args.root),
        language=args.language,
        base_url=args.base_url,
        api_key=args.api_key,
        model=args.model,
        batch_size=args.batch,
        max_chars=args.max_chars,
        workers=args.workers,
        cache_dir=Path(__file__).parent / "caches",
        work_root=Path(args.work),
        force_language=bool(args.force_language),
        on_log=print,
        on_progress=lambda d, t, c="": print(f"{d}/{t} {c}" if c else f"{d}/{t}"),
    )
    translator.run()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
