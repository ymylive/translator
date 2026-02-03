# RenPy Translator

<div align="center">

![Python](https://img.shields.io/badge/Python-3.10+-blue.svg)
![License](https://img.shields.io/badge/License-MIT-green.svg)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey.svg)

**A modern GUI tool for translating Ren'Py visual novel games using AI-powered translation APIs.**

[Features](#features) • [Installation](#installation) • [Usage](#usage) • [Configuration](#configuration) • [Building](#building)

</div>

---

## Features

- **Multi-Engine Support**: Translate games built with Ren'Py, RPG Maker, and Unity
- **Multiple Translation APIs**:
  - OpenAI / OpenRouter (GPT-4, DeepSeek, Qwen, etc.)
  - Anthropic Claude
  - DeepL
  - Google Cloud Translation
- **Modern UI**: Built with CustomTkinter for a clean Material Design experience
- **Smart Features**:
  - Drag & drop game files
  - Stop/Resume translation with progress preservation
  - Translation caching (memory + SQLite)
  - Glossary support for consistent terminology
  - Post-processing rules for text corrections
  - API key rotation for rate limit handling
- **Performance Optimized**:
  - 60fps smooth animations
  - Virtualized log viewer
  - Batch processing with configurable size
- **Keyboard Shortcuts**:
  - `Ctrl+S` - Save configuration
  - `Ctrl+Enter` - Start translation
  - `Escape` - Stop translation
  - `Ctrl+1~5` - Switch tabs

## Installation

### Prerequisites

- Python 3.10 or higher
- pip package manager

### From Source

```bash
# Clone the repository
git clone https://github.com/ymylive/translator.git
cd translator

# Install dependencies
pip install -r requirements.txt

# Run the application
python app.py
```

### From Release

Download the latest release from the [Releases](https://github.com/ymylive/translator/releases) page.

## Usage

### Quick Start

1. **Launch the application**: Run `python app.py` or the executable
2. **Select your game**:
   - Drag & drop the game EXE onto the window, or
   - Click "Browse" to select the game executable
3. **Configure translation**:
   - Choose your target language
   - Select a translation API (OpenAI, Claude, DeepL, etc.)
   - Enter your API key
4. **Start translating**: Click "Start Translation" button

### Output

Translated files are saved to:
```
<game_root>/game/tl/<language>/zz_auto_strings.rpy
```

### Stop & Resume

- Click **Stop** to pause translation (current batch will complete)
- Click **Start** again to resume from where you left off
- Progress is automatically cached in the `caches/` directory

## Configuration

### Translation Settings

| Setting | Description | Default |
|---------|-------------|---------|
| Batch Size | Number of strings per API call | 100 |
| Max Characters | Maximum characters per batch | 60000 |
| Workers | Concurrent translation threads | 5 |
| Force Language | Set game language on startup | Enabled |

### Glossary

Create a glossary file (JSON or CSV) to ensure consistent translation of:
- Character names
- Game-specific terms
- Technical vocabulary

### Post-Processing Rules

Define text replacement rules to fix common translation issues:
- Regex support for pattern matching
- Applied after translation completes

## Supported Games

### Ren'Py
- Automatic `.rpyc` decompilation
- RPA archive extraction
- Preserves placeholders (`[Name]`, `{i}`, etc.)

### RPG Maker (Planned)
- MV/MZ JSON extraction
- VX/VX Ace support

### Unity (Planned)
- I2 Localization support
- TextMesh Pro extraction

## Building

### Windows Executable

```bash
# Default single-file build
python build_exe.py

# Directory mode (faster startup)
python build_exe.py --dir

# Debug mode (with console)
python build_exe.py --debug
```

### GitHub Actions

Releases are automatically built via GitHub Actions when a new tag is pushed:

```bash
git tag v1.0.0
git push origin v1.0.0
```

## Project Structure

```
translator/
├── app.py              # Main application entry
├── translator_core.py  # Core translation logic
├── ui/                 # UI components
│   ├── components.py   # Custom widgets
│   ├── animations.py   # Animation engine
│   ├── theme.py        # Color themes
│   └── shortcuts.py    # Keyboard shortcuts
├── translators/        # Translation API implementations
│   ├── openai_translator.py
│   ├── claude_translator.py
│   ├── deepl_translator.py
│   └── google_translator.py
├── game_engines/       # Game engine handlers
│   ├── renpy_engine.py
│   ├── rpgmaker_engine.py
│   └── unity_engine.py
└── build_exe.py        # PyInstaller build script
```

## Requirements

```
customtkinter>=5.2.0
tkinterdnd2>=0.3.0
pywinstyles>=1.8
httpx>=0.27.0
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [CustomTkinter](https://github.com/TomSchimansky/CustomTkinter) for the modern UI framework
- [unrpyc](https://github.com/CensoredUsername/unrpyc) for Ren'Py decompilation
