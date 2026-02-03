import json
import os
import queue
import threading
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox
import customtkinter as ctk

try:
    from tkinterdnd2 import DND_FILES, TkinterDnD
    BaseTk = TkinterDnD.Tk
    HAVE_DND = True
except Exception:
    BaseTk = tk.Tk
    HAVE_DND = False

try:
    import pywinstyles
    HAVE_PYWINSTYLES = True
except ImportError:
    HAVE_PYWINSTYLES = False

from translator_core import Translator
from plugin_manager import PluginManager
from game_engines.detector import EngineDetector
from config_schema import ConfigSchema
from config_utils import safe_save_json, safe_load_json
from glossary import GlossaryManager, GlossaryEntry
from post_processor import PostProcessor, PostProcessRule
from prompt_templates import PromptManager, DEFAULT_SYSTEM_PROMPT, DEFAULT_USER_PROMPT
from ui import (
    COLORS, AnimationManager, AnimationEngine, AnimatedCard,
    MDButton, MDEntry, MDComboBox, MDProgressBar, MDCheckBox, MDTabview,
    Toast, VirtualizedLog, setup_app_shortcuts
)

CONFIG_PATH = Path(__file__).parent / "config.json"
CACHE_DIR = Path(__file__).parent / "caches"
WORK_DIR = Path(__file__).parent / "work"
GLOSSARY_DIR = Path(__file__).parent / "glossaries"
POSTPROCESS_PATH = Path(__file__).parent / "postprocess_rules.json"
PROMPTS_PATH = Path(__file__).parent / "custom_prompts.json"

# Set CustomTkinter appearance
ctk.set_appearance_mode("system")
ctk.set_default_color_theme("blue")


class App(ctk.CTk if not HAVE_DND else type('CTkDnD', (ctk.CTk, TkinterDnD.Tk), {})):
    def __init__(self) -> None:
        super().__init__()
        self.title("RenPy Translator")
        self.geometry("1000x750")
        self.minsize(800, 600)

        self.queue = queue.Queue()
        self.worker = None
        self.stop_event = None

        self.plugin_manager = PluginManager()
        self.detector = EngineDetector()
        self.config_schema = ConfigSchema()

        self._build_ui()
        self._load_config()
        self._poll_queue()

        # Initialize keyboard shortcuts
        self.shortcut_manager = setup_app_shortcuts(self)

        if HAVE_PYWINSTYLES:
            try:
                pywinstyles.apply_style(self, 'mica')
                pywinstyles.change_header_color(self, '#1f1f1f')
            except Exception:
                pass

        if HAVE_DND:
            try:
                self.drop_target_register(DND_FILES)
                self.dnd_bind("<<Drop>>", self._on_drop)
            except Exception:
                pass

    def _build_ui(self) -> None:
        # Main container
        main = ctk.CTkFrame(self, fg_color=COLORS['background'])
        main.pack(fill='both', expand=True)

        # Header
        header = ctk.CTkFrame(main, height=80, corner_radius=0, fg_color=COLORS['surface'])
        header.pack(fill='x')
        header.pack_propagate(False)

        title_container = ctk.CTkFrame(header, fg_color="transparent")
        title_container.pack(side='left', padx=32, pady=20)

        ctk.CTkLabel(
            title_container,
            text="🎮 RenPy 翻译工具",
            font=("Segoe UI", 24, "bold"),
            text_color=COLORS['text_primary']
        ).pack(side='left')

        # Content area
        content = ctk.CTkFrame(main, fg_color="transparent")
        content.pack(fill='both', expand=True, padx=32, pady=24)

        # Tabview
        self.tabview = MDTabview(content, corner_radius=12)
        self.tabview.pack(fill='both', expand=True)

        self.tabview.add("⚙️ 基本设置")
        self.tabview.add("📖 词典管理")
        self.tabview.add("🔄 后处理规则")
        self.tabview.add("🔧 高级设置")
        self.tabview.add("📋 运行日志")

        self._build_main_tab(self.tabview.tab("⚙️ 基本设置"))
        self._build_glossary_tab(self.tabview.tab("📖 词典管理"))
        self._build_postprocess_tab(self.tabview.tab("🔄 后处理规则"))
        self._build_advanced_tab(self.tabview.tab("🔧 高级设置"))
        self._build_log_tab(self.tabview.tab("📋 运行日志"))

        # Action buttons
        btn_frame = ctk.CTkFrame(main, fg_color="transparent")
        btn_frame.pack(fill='x', padx=32, pady=(0, 16))

        self.start_btn = MDButton(
            btn_frame,
            text="开始翻译",
            command=self._start,
            fg_color=COLORS['primary'],
            hover_color=COLORS['primary_hover'],
            corner_radius=8,
            height=40,
            font=("Segoe UI", 14, "bold")
        )
        self.start_btn.pack(side='left', padx=(0, 10))

        self.stop_btn = MDButton(
            btn_frame,
            text="停止",
            command=self._stop,
            state='disabled',
            corner_radius=8,
            height=40,
            font=("Segoe UI", 14)
        )
        self.stop_btn.pack(side='left', padx=(0, 10))

        MDButton(
            btn_frame,
            text="保存配置",
            command=self._save_config,
            corner_radius=8,
            height=40,
            font=("Segoe UI", 14)
        ).pack(side='left')

        # Progress bar
        self.progress = MDProgressBar(main, corner_radius=8, height=20)
        self.progress.pack(fill='x', padx=32, pady=(10, 5))
        self.progress.set(0)

        self.progress_label = ctk.CTkLabel(
            main,
            text="就绪",
            font=("Segoe UI", 11),
            text_color=COLORS['text_secondary']
        )
        self.progress_label.pack(anchor='w', padx=32)

        # Status bar
        status_frame = ctk.CTkFrame(main, fg_color="transparent")
        status_frame.pack(fill='x', padx=32, pady=(5, 16))
        self.status_label = ctk.CTkLabel(
            status_frame,
            text="状态: 就绪",
            font=("Segoe UI", 11),
            text_color=COLORS['text_secondary']
        )
        self.status_label.pack(side='left')

    def _build_main_tab(self, parent):
        # Scrollable frame
        scroll = ctk.CTkScrollableFrame(parent, fg_color="transparent")
        scroll.pack(fill='both', expand=True, padx=10, pady=10)

        # Game Config Card
        game_card = AnimatedCard(scroll, "游戏配置")
        game_card.pack(fill='x', pady=(0, 16))

        self._add_path_row(game_card.content, "游戏 EXE:", self._browse_exe, is_exe=True)
        self._add_path_row(game_card.content, "游戏根目录:", self._browse_root)

        # Engine selection
        engine_row = ctk.CTkFrame(game_card.content, fg_color="transparent")
        engine_row.pack(fill='x', pady=8)
        ctk.CTkLabel(engine_row, text="游戏引擎:", width=120, anchor='w').pack(side='left', padx=(0, 10))

        self.engine_var = tk.StringVar(value="renpy")
        engines = list(self.plugin_manager.discover_engines().keys())
        self.engine_combo = MDComboBox(
            engine_row,
            variable=self.engine_var,
            values=engines,
            state='readonly',
            width=150
        )
        self.engine_combo.pack(side='left', padx=(0, 10))

        MDButton(
            engine_row,
            text="自动识别",
            command=self._auto_detect_engine,
            width=100,
            corner_radius=8
        ).pack(side='left')

        # Translation Config Card
        trans_card = AnimatedCard(scroll, "翻译配置")
        trans_card.pack(fill='x', pady=(0, 16))

        # Translator type selection with category
        row = ctk.CTkFrame(trans_card.content, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="翻译器类型:", width=120, anchor='w').pack(side='left', padx=(0, 10))

        self.translator_category_var = tk.StringVar(value="ai")
        category_combo = MDComboBox(
            row,
            variable=self.translator_category_var,
            values=["AI 翻译", "传统翻译"],
            state='readonly',
            width=120,
            command=self._on_translator_category_change
        )
        category_combo.pack(side='left', padx=(0, 10))

        # Translator selection
        ctk.CTkLabel(row, text="翻译器:", width=80, anchor='w').pack(side='left', padx=(0, 10))
        self.translator_var = tk.StringVar(value="openai")
        translators = list(self.plugin_manager.discover_translators().keys())
        self.translator_combo = MDComboBox(
            row,
            variable=self.translator_var,
            values=translators,
            state='readonly',
            width=150,
            command=self._on_translator_change
        )
        self.translator_combo.pack(side='left', fill='x', expand=True)

        # Target language
        row = ctk.CTkFrame(trans_card.content, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="目标语言:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        self.language_var = tk.StringVar(value="trad_chinese")
        lang_combo = MDComboBox(
            row,
            variable=self.language_var,
            values=["trad_chinese", "simp_chinese", "english", "japanese", "korean"],
            state='readonly',
            width=200
        )
        lang_combo.pack(side='left')

        # Dynamic Translator Config Card - will be rebuilt based on translator type
        self.translator_config_card = AnimatedCard(scroll, "翻译器配置")
        self.translator_config_card.pack(fill='x', pady=(0, 16))
        self.translator_config_frame = self.translator_config_card.content

        # Initialize translator config variables
        self._init_translator_config_vars()

        # Build initial config panel
        self._build_translator_config_panel()

    def _init_translator_config_vars(self):
        """Initialize all translator configuration variables."""
        # OpenAI / AI translator config
        self.ai_protocol_var = tk.StringVar(value="openai")
        self.base_url_var = tk.StringVar(value="https://openrouter.ai/api/v1")
        self.model_var = tk.StringVar(value="tngtech/deepseek-r1t2-chimera:free")
        self.api_key_var = tk.StringVar(value="")

        # Claude config
        self.claude_api_key_var = tk.StringVar(value="")
        self.claude_model_var = tk.StringVar(value="claude-3-sonnet-20240229")

        # DeepL config
        self.deepl_api_key_var = tk.StringVar(value="")
        self.deepl_formality_var = tk.StringVar(value="default")

        # Google config
        self.google_api_key_var = tk.StringVar(value="")
        self.google_project_id_var = tk.StringVar(value="")

    def _on_translator_category_change(self, choice=None):
        """Handle translator category change."""
        category = self.translator_category_var.get()

        # Define translator categories
        ai_translators = ["openai", "claude"]
        traditional_translators = ["deepl", "google"]

        if "AI" in category:
            available = [t for t in self.plugin_manager.discover_translators().keys() if t in ai_translators]
            if not available:
                available = ai_translators
        else:
            available = [t for t in self.plugin_manager.discover_translators().keys() if t in traditional_translators]
            if not available:
                available = traditional_translators

        self.translator_combo.configure(values=available)
        if available:
            self.translator_var.set(available[0])
            self._on_translator_change()

    def _on_translator_change(self, choice=None):
        """Handle translator selection change."""
        self._build_translator_config_panel()

    def _build_translator_config_panel(self):
        """Build dynamic configuration panel based on selected translator."""
        # Clear existing config widgets
        for widget in self.translator_config_frame.winfo_children():
            widget.destroy()

        translator = self.translator_var.get()

        if translator == "openai":
            self._build_openai_config()
        elif translator == "claude":
            self._build_claude_config()
        elif translator == "deepl":
            self._build_deepl_config()
        elif translator == "google":
            self._build_google_config()
        else:
            # Default to OpenAI-style config
            self._build_openai_config()

    def _build_openai_config(self):
        """Build OpenAI/OpenAI-compatible API configuration panel."""
        frame = self.translator_config_frame

        # Title hint
        hint = ctk.CTkLabel(
            frame,
            text="支持 OpenAI、OpenRouter、LocalAI 等兼容 API",
            font=("Segoe UI", 10),
            text_color=COLORS['text_secondary']
        )
        hint.pack(anchor='w', pady=(0, 8))

        # Base URL
        row = ctk.CTkFrame(frame, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="API 地址:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        MDEntry(row, textvariable=self.base_url_var).pack(side='left', fill='x', expand=True)

        # Model
        row = ctk.CTkFrame(frame, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="模型:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        model_combo = MDComboBox(
            row,
            variable=self.model_var,
            values=[
                "gpt-4o",
                "gpt-4o-mini",
                "gpt-4-turbo",
                "gpt-3.5-turbo",
                "deepseek-chat",
                "tngtech/deepseek-r1t2-chimera:free",
                "qwen/qwen-2.5-72b-instruct:free",
            ],
            width=300
        )
        model_combo.pack(side='left', fill='x', expand=True)

        # API Key
        row = ctk.CTkFrame(frame, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="API Key:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        MDEntry(row, textvariable=self.api_key_var, show="*").pack(side='left', fill='x', expand=True)

    def _build_claude_config(self):
        """Build Claude API configuration panel."""
        frame = self.translator_config_frame

        # Title hint
        hint = ctk.CTkLabel(
            frame,
            text="Anthropic Claude API 配置",
            font=("Segoe UI", 10),
            text_color=COLORS['text_secondary']
        )
        hint.pack(anchor='w', pady=(0, 8))

        # API Key
        row = ctk.CTkFrame(frame, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="API Key:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        MDEntry(row, textvariable=self.claude_api_key_var, show="*").pack(side='left', fill='x', expand=True)

        # Model
        row = ctk.CTkFrame(frame, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="模型:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        model_combo = MDComboBox(
            row,
            variable=self.claude_model_var,
            values=[
                "claude-3-opus-20240229",
                "claude-3-sonnet-20240229",
                "claude-3-haiku-20240307",
                "claude-3-5-sonnet-20241022",
            ],
            state='readonly',
            width=300
        )
        model_combo.pack(side='left')

        # Note
        note = ctk.CTkLabel(
            frame,
            text="需要安装 anthropic 包: pip install anthropic",
            font=("Segoe UI", 10),
            text_color=COLORS['text_secondary']
        )
        note.pack(anchor='w', pady=(8, 0))

    def _build_deepl_config(self):
        """Build DeepL API configuration panel."""
        frame = self.translator_config_frame

        # Title hint
        hint = ctk.CTkLabel(
            frame,
            text="DeepL 翻译 API - 高质量机器翻译",
            font=("Segoe UI", 10),
            text_color=COLORS['text_secondary']
        )
        hint.pack(anchor='w', pady=(0, 8))

        # API Key
        row = ctk.CTkFrame(frame, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="API Key:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        MDEntry(row, textvariable=self.deepl_api_key_var, show="*").pack(side='left', fill='x', expand=True)

        # Formality
        row = ctk.CTkFrame(frame, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="正式程度:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        formality_combo = MDComboBox(
            row,
            variable=self.deepl_formality_var,
            values=["default", "more", "less", "prefer_more", "prefer_less"],
            state='readonly',
            width=200
        )
        formality_combo.pack(side='left')

        # Note
        note = ctk.CTkLabel(
            frame,
            text="需要安装 deepl 包: pip install deepl\n免费版每月 500,000 字符",
            font=("Segoe UI", 10),
            text_color=COLORS['text_secondary']
        )
        note.pack(anchor='w', pady=(8, 0))

    def _build_google_config(self):
        """Build Google Translate API configuration panel."""
        frame = self.translator_config_frame

        # Title hint
        hint = ctk.CTkLabel(
            frame,
            text="Google Cloud Translation API",
            font=("Segoe UI", 10),
            text_color=COLORS['text_secondary']
        )
        hint.pack(anchor='w', pady=(0, 8))

        # API Key
        row = ctk.CTkFrame(frame, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="API Key:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        MDEntry(row, textvariable=self.google_api_key_var, show="*").pack(side='left', fill='x', expand=True)

        # Project ID (optional)
        row = ctk.CTkFrame(frame, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="项目 ID:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        MDEntry(row, textvariable=self.google_project_id_var, placeholder_text="可选").pack(side='left', fill='x', expand=True)

        # Note
        note = ctk.CTkLabel(
            frame,
            text="需要安装 google-cloud-translate 包:\npip install google-cloud-translate",
            font=("Segoe UI", 10),
            text_color=COLORS['text_secondary']
        )
        note.pack(anchor='w', pady=(8, 0))

    def _get_current_translator_config(self):
        """Get configuration for the currently selected translator."""
        translator = self.translator_var.get()

        if translator == "openai":
            return {
                "base_url": self.base_url_var.get().strip(),
                "api_key": self.api_key_var.get().strip(),
                "model": self.model_var.get().strip(),
            }
        elif translator == "claude":
            return {
                "base_url": "",
                "api_key": self.claude_api_key_var.get().strip(),
                "model": self.claude_model_var.get().strip(),
            }
        elif translator == "deepl":
            return {
                "base_url": "",
                "api_key": self.deepl_api_key_var.get().strip(),
                "model": "",
                "formality": self.deepl_formality_var.get(),
            }
        elif translator == "google":
            return {
                "base_url": "",
                "api_key": self.google_api_key_var.get().strip(),
                "model": "",
                "project_id": self.google_project_id_var.get().strip(),
            }
        else:
            return {
                "base_url": self.base_url_var.get().strip(),
                "api_key": self.api_key_var.get().strip(),
                "model": self.model_var.get().strip(),
            }

    def _build_advanced_tab(self, parent):
        scroll = ctk.CTkScrollableFrame(parent, fg_color="transparent")
        scroll.pack(fill='both', expand=True, padx=10, pady=10)

        # Performance Mode Card
        perf_card = AnimatedCard(scroll, "性能设置")
        perf_card.pack(fill='x', pady=(0, 16))

        row = ctk.CTkFrame(perf_card.content, fg_color="transparent")
        row.pack(fill='x', pady=8)
        self.performance_mode_var = tk.BooleanVar(value=False)
        MDCheckBox(
            row,
            text="性能模式（禁用动画效果，提升响应速度）",
            variable=self.performance_mode_var,
            command=self._toggle_performance_mode
        ).pack(side='left')

        adv_card = AnimatedCard(scroll, "高级配置")
        adv_card.pack(fill='x', pady=(0, 16))

        row = ctk.CTkFrame(adv_card.content, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="批次大小:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        self.batch_var = tk.StringVar(value="100")
        MDEntry(row, textvariable=self.batch_var, width=100).pack(side='left', padx=(0, 20))

        ctk.CTkLabel(row, text="最大字符:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        self.max_chars_var = tk.StringVar(value="60000")
        MDEntry(row, textvariable=self.max_chars_var, width=100).pack(side='left', padx=(0, 20))

        ctk.CTkLabel(row, text="工作线程:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        self.workers_var = tk.StringVar(value="5")
        MDEntry(row, textvariable=self.workers_var, width=100).pack(side='left')

        row = ctk.CTkFrame(adv_card.content, fg_color="transparent")
        row.pack(fill='x', pady=8)
        self.force_lang_var = tk.BooleanVar(value=True)
        MDCheckBox(row, text="启动时强制使用目标语言", variable=self.force_lang_var).pack(side='left')

        # Prompt Template Card
        prompt_card = AnimatedCard(scroll, "Prompt 模板")
        prompt_card.pack(fill='x', pady=(0, 16))

        row = ctk.CTkFrame(prompt_card.content, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="系统 Prompt:", width=120, anchor='nw').pack(side='left', padx=(0, 10))

        self.system_prompt_text = ctk.CTkTextbox(prompt_card.content, height=80)
        self.system_prompt_text.pack(fill='x', pady=8)
        self.system_prompt_text.insert("1.0", DEFAULT_SYSTEM_PROMPT)

        row = ctk.CTkFrame(prompt_card.content, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="用户 Prompt:", width=120, anchor='nw').pack(side='left', padx=(0, 10))

        self.user_prompt_text = ctk.CTkTextbox(prompt_card.content, height=80)
        self.user_prompt_text.pack(fill='x', pady=8)
        self.user_prompt_text.insert("1.0", DEFAULT_USER_PROMPT)

        hint_label = ctk.CTkLabel(
            prompt_card.content,
            text="支持占位符: {srclang}, {tgtlang}, {sentence}, {DictWithPrompt[...]}",
            font=("Segoe UI", 10),
            text_color=COLORS['text_secondary']
        )
        hint_label.pack(anchor='w', pady=4)

        # API Key Pool Card
        apikey_card = AnimatedCard(scroll, "API Key 池")
        apikey_card.pack(fill='x', pady=(0, 16))

        row = ctk.CTkFrame(apikey_card.content, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="API Keys:", width=120, anchor='nw').pack(side='left', padx=(0, 10))

        self.api_keys_text = ctk.CTkTextbox(apikey_card.content, height=100)
        self.api_keys_text.pack(fill='x', pady=8)

        hint_label = ctk.CTkLabel(
            apikey_card.content,
            text="每行一个 Key，支持自动轮换和错误冷却",
            font=("Segoe UI", 10),
            text_color=COLORS['text_secondary']
        )
        hint_label.pack(anchor='w', pady=4)

        # Cache Config Card
        cache_card = AnimatedCard(scroll, "缓存配置")
        cache_card.pack(fill='x', pady=(0, 16))

        row = ctk.CTkFrame(cache_card.content, fg_color="transparent")
        row.pack(fill='x', pady=8)
        self.use_cache_var = tk.BooleanVar(value=True)
        MDCheckBox(row, text="启用翻译缓存（内存 + SQLite）", variable=self.use_cache_var).pack(side='left')

        row = ctk.CTkFrame(cache_card.content, fg_color="transparent")
        row.pack(fill='x', pady=8)
        MDButton(row, text="清除缓存", command=self._clear_cache, corner_radius=8).pack(side='left', padx=(0, 10))
        MDButton(row, text="导出缓存", command=self._export_cache, corner_radius=8).pack(side='left')

    def _build_glossary_tab(self, parent):
        """Build glossary management tab."""
        scroll = ctk.CTkScrollableFrame(parent, fg_color="transparent")
        scroll.pack(fill='both', expand=True, padx=10, pady=10)

        # Glossary Config Card
        glossary_card = AnimatedCard(scroll, "专有名词词典")
        glossary_card.pack(fill='x', pady=(0, 16))

        # Glossary file path
        row = ctk.CTkFrame(glossary_card.content, fg_color="transparent")
        row.pack(fill='x', pady=8)
        ctk.CTkLabel(row, text="词典文件:", width=120, anchor='w').pack(side='left', padx=(0, 10))
        self.glossary_path_var = tk.StringVar()
        MDEntry(row, textvariable=self.glossary_path_var).pack(side='left', fill='x', expand=True, padx=(0, 10))
        MDButton(row, text="浏览", command=self._browse_glossary, width=80, corner_radius=8).pack(side='left')

        # Enable glossary
        row = ctk.CTkFrame(glossary_card.content, fg_color="transparent")
        row.pack(fill='x', pady=8)
        self.use_glossary_var = tk.BooleanVar(value=True)
        MDCheckBox(row, text="启用专有名词词典", variable=self.use_glossary_var).pack(side='left')

        # Glossary Edit Card
        edit_card = AnimatedCard(scroll, "词典条目编辑")
        edit_card.pack(fill='x', pady=(0, 16))

        # Header
        header = ctk.CTkFrame(edit_card.content, fg_color="transparent")
        header.pack(fill='x', pady=4)
        ctk.CTkLabel(header, text="原文", width=200, anchor='w', font=("Segoe UI", 11, "bold")).pack(side='left', padx=5)
        ctk.CTkLabel(header, text="译文", width=200, anchor='w', font=("Segoe UI", 11, "bold")).pack(side='left', padx=5)
        ctk.CTkLabel(header, text="备注", width=150, anchor='w', font=("Segoe UI", 11, "bold")).pack(side='left', padx=5)

        # Glossary entries list
        self.glossary_list_frame = ctk.CTkScrollableFrame(edit_card.content, height=200)
        self.glossary_list_frame.pack(fill='x', pady=8)

        # Store entry rows
        self.glossary_entry_rows = []

        # Buttons
        btn_row = ctk.CTkFrame(edit_card.content, fg_color="transparent")
        btn_row.pack(fill='x', pady=8)
        MDButton(btn_row, text="+ 添加条目", command=self._add_glossary_entry, corner_radius=8).pack(side='left', padx=(0, 10))
        MDButton(btn_row, text="从 CSV 导入", command=self._import_glossary_csv, corner_radius=8).pack(side='left', padx=(0, 10))
        MDButton(btn_row, text="保存词典", command=self._save_glossary, corner_radius=8).pack(side='left')

    def _build_postprocess_tab(self, parent):
        """Build post-processing rules tab."""
        scroll = ctk.CTkScrollableFrame(parent, fg_color="transparent")
        scroll.pack(fill='both', expand=True, padx=10, pady=10)

        # Post-process Config Card
        post_card = AnimatedCard(scroll, "翻译结果修正")
        post_card.pack(fill='x', pady=(0, 16))

        # Enable post-processing
        row = ctk.CTkFrame(post_card.content, fg_color="transparent")
        row.pack(fill='x', pady=8)
        self.use_postprocess_var = tk.BooleanVar(value=True)
        MDCheckBox(row, text="启用翻译结果后处理", variable=self.use_postprocess_var).pack(side='left')

        # Rules Card
        rules_card = AnimatedCard(scroll, "替换规则")
        rules_card.pack(fill='x', pady=(0, 16))

        # Header
        header = ctk.CTkFrame(rules_card.content, fg_color="transparent")
        header.pack(fill='x', pady=4)
        ctk.CTkLabel(header, text="查找", width=200, anchor='w', font=("Segoe UI", 11, "bold")).pack(side='left', padx=5)
        ctk.CTkLabel(header, text="替换为", width=200, anchor='w', font=("Segoe UI", 11, "bold")).pack(side='left', padx=5)
        ctk.CTkLabel(header, text="正则", width=60, anchor='w', font=("Segoe UI", 11, "bold")).pack(side='left', padx=5)

        # Rules list
        self.postprocess_list_frame = ctk.CTkScrollableFrame(rules_card.content, height=200)
        self.postprocess_list_frame.pack(fill='x', pady=8)

        # Store rule rows
        self.postprocess_rule_rows = []

        # Buttons
        btn_row = ctk.CTkFrame(rules_card.content, fg_color="transparent")
        btn_row.pack(fill='x', pady=8)
        MDButton(btn_row, text="+ 添加规则", command=self._add_postprocess_rule, corner_radius=8).pack(side='left', padx=(0, 10))
        MDButton(btn_row, text="保存规则", command=self._save_postprocess_rules, corner_radius=8).pack(side='left')

    def _build_log_tab(self, parent):
        # Use virtualized log for better performance with large logs
        self.log = VirtualizedLog(parent, max_lines=1000)
        self.log.pack(fill='both', expand=True, padx=10, pady=10)

    def _add_path_row(self, parent, label, browse_cmd, is_exe=False):
        row = ctk.CTkFrame(parent, fg_color="transparent")
        row.pack(fill='x', pady=8)

        ctk.CTkLabel(row, text=label, width=120, anchor='w').pack(side='left', padx=(0, 10))

        if is_exe:
            self.exe_path_var = tk.StringVar()
            var = self.exe_path_var
        else:
            self.game_root_var = tk.StringVar()
            var = self.game_root_var

        MDEntry(row, textvariable=var).pack(side='left', fill='x', expand=True, padx=(0, 10))
        MDButton(row, text="浏览", command=browse_cmd, width=80, corner_radius=8).pack(side='left')

    def _browse_exe(self) -> None:
        path = filedialog.askopenfilename(title="选择游戏 EXE 文件",
                                         filetypes=[("可执行文件", "*.exe"), ("所有文件", "*.*")])
        if path:
            self.exe_path_var.set(path)
            self.game_root_var.set(str(Path(path).parent))
            self._update_status(f"已选择: {Path(path).name}")

    def _browse_root(self) -> None:
        path = filedialog.askdirectory(title="选择游戏根目录")
        if path:
            self.game_root_var.set(path)
            self._update_status(f"已选择: {Path(path).name}")

    def _browse_glossary(self) -> None:
        path = filedialog.askopenfilename(
            title="选择词典文件",
            filetypes=[("JSON 文件", "*.json"), ("CSV 文件", "*.csv"), ("所有文件", "*.*")]
        )
        if path:
            self.glossary_path_var.set(path)
            self._load_glossary_entries(Path(path))

    def _load_glossary_entries(self, path: Path) -> None:
        """Load glossary entries into the UI."""
        # Clear existing entries
        for row in self.glossary_entry_rows:
            row.destroy()
        self.glossary_entry_rows.clear()

        if not path.exists():
            return

        manager = GlossaryManager()
        if path.suffix.lower() == '.csv':
            manager.load_from_csv(path)
        else:
            manager.load(path)

        for entry in manager.entries:
            self._add_glossary_entry(entry.source, entry.target, entry.context)

        self._log(f"已加载词典: {len(manager)} 条目")

    def _add_glossary_entry(self, source: str = "", target: str = "", info: str = "") -> None:
        """Add a glossary entry row to the UI."""
        row = ctk.CTkFrame(self.glossary_list_frame, fg_color="transparent")
        row.pack(fill='x', pady=2)

        source_var = tk.StringVar(value=source)
        target_var = tk.StringVar(value=target)
        info_var = tk.StringVar(value=info)

        MDEntry(row, textvariable=source_var, width=200).pack(side='left', padx=5)
        MDEntry(row, textvariable=target_var, width=200).pack(side='left', padx=5)
        MDEntry(row, textvariable=info_var, width=150).pack(side='left', padx=5)

        def delete_row():
            row.destroy()
            if row in self.glossary_entry_rows:
                self.glossary_entry_rows.remove(row)

        MDButton(row, text="×", width=30, command=delete_row, corner_radius=4).pack(side='left', padx=5)

        row.source_var = source_var
        row.target_var = target_var
        row.info_var = info_var
        self.glossary_entry_rows.append(row)

    def _import_glossary_csv(self) -> None:
        """Import glossary from CSV file."""
        path = filedialog.askopenfilename(
            title="选择 CSV 文件",
            filetypes=[("CSV 文件", "*.csv"), ("所有文件", "*.*")]
        )
        if path:
            self._load_glossary_entries(Path(path))

    def _save_glossary(self) -> None:
        """Save glossary entries to file."""
        path_str = self.glossary_path_var.get().strip()
        if not path_str:
            path = filedialog.asksaveasfilename(
                title="保存词典文件",
                defaultextension=".json",
                filetypes=[("JSON 文件", "*.json")]
            )
            if not path:
                return
            self.glossary_path_var.set(path)
            path_str = path

        manager = GlossaryManager()
        for row in self.glossary_entry_rows:
            source = row.source_var.get().strip()
            target = row.target_var.get().strip()
            info = row.info_var.get().strip()
            if source and target:
                manager.add_entry(GlossaryEntry(source=source, target=target, context=info))

        manager.save(Path(path_str))
        self._log(f"词典已保存: {len(manager)} 条目")
        Toast.show(self, f"词典已保存: {len(manager)} 条目", "success", 2000)

    def _add_postprocess_rule(self, pattern: str = "", replacement: str = "", is_regex: bool = False) -> None:
        """Add a post-process rule row to the UI."""
        row = ctk.CTkFrame(self.postprocess_list_frame, fg_color="transparent")
        row.pack(fill='x', pady=2)

        pattern_var = tk.StringVar(value=pattern)
        replacement_var = tk.StringVar(value=replacement)
        is_regex_var = tk.BooleanVar(value=is_regex)

        MDEntry(row, textvariable=pattern_var, width=200).pack(side='left', padx=5)
        MDEntry(row, textvariable=replacement_var, width=200).pack(side='left', padx=5)
        MDCheckBox(row, text="", variable=is_regex_var, width=40).pack(side='left', padx=5)

        def delete_row():
            row.destroy()
            if row in self.postprocess_rule_rows:
                self.postprocess_rule_rows.remove(row)

        MDButton(row, text="×", width=30, command=delete_row, corner_radius=4).pack(side='left', padx=5)

        row.pattern_var = pattern_var
        row.replacement_var = replacement_var
        row.is_regex_var = is_regex_var
        self.postprocess_rule_rows.append(row)

    def _save_postprocess_rules(self) -> None:
        """Save post-process rules to file."""
        processor = PostProcessor()
        for row in self.postprocess_rule_rows:
            pattern = row.pattern_var.get().strip()
            replacement = row.replacement_var.get()
            is_regex = row.is_regex_var.get()
            if pattern:
                processor.add_rule(pattern, replacement, is_regex)

        processor.save_to_file(POSTPROCESS_PATH)
        self._log(f"后处理规则已保存: {len(processor)} 条")
        Toast.show(self, f"后处理规则已保存: {len(processor)} 条", "success", 2000)

    def _load_postprocess_rules(self) -> None:
        """Load post-process rules into the UI."""
        if not POSTPROCESS_PATH.exists():
            return

        processor = PostProcessor()
        processor.load_from_file(POSTPROCESS_PATH)

        for rule in processor.rules:
            self._add_postprocess_rule(rule.pattern, rule.replacement, rule.is_regex)

    def _clear_cache(self) -> None:
        """Clear translation cache."""
        if messagebox.askyesno("确认", "确定要清除所有翻译缓存吗？"):
            import shutil
            try:
                if CACHE_DIR.exists():
                    shutil.rmtree(CACHE_DIR)
                    CACHE_DIR.mkdir(parents=True, exist_ok=True)
                self._log("缓存已清除")
                Toast.show(self, "缓存已清除", "success", 2000)
            except Exception as e:
                Toast.show(self, f"清除缓存失败: {e}", "error", 3000)

    def _export_cache(self) -> None:
        """Export translation cache."""
        path = filedialog.asksaveasfilename(
            title="导出缓存",
            defaultextension=".json",
            filetypes=[("JSON 文件", "*.json")]
        )
        if not path:
            return

        try:
            all_cache = {}
            for cache_file in CACHE_DIR.glob("*.json"):
                data = safe_load_json(cache_file, {})
                all_cache[cache_file.stem] = data

            safe_save_json(Path(path), all_cache)
            self._log(f"缓存已导出到: {path}")
            Toast.show(self, "缓存已导出", "success", 2000)
        except Exception as e:
            Toast.show(self, f"导出缓存失败: {e}", "error", 3000)

    def _toggle_performance_mode(self) -> None:
        """Toggle performance mode - enables/disables animations."""
        enabled = not self.performance_mode_var.get()
        AnimationEngine.set_enabled(enabled)
        if self.performance_mode_var.get():
            self._log("性能模式已启用 - 动画已禁用")
        else:
            self._log("性能模式已禁用 - 动画已启用")

    def _auto_detect_engine(self) -> None:
        game_root = self.game_root_var.get().strip()
        if not game_root:
            Toast.show(self, "请先选择游戏根目录", "warning", 2000)
            return

        detected = self.detector.detect(Path(game_root))
        if detected:
            self.engine_var.set(detected)
            self._update_status(f"检测到引擎: {detected}")
            Toast.show(self, f"检测到游戏引擎: {detected}", "success", 2000)
        else:
            Toast.show(self, "无法自动识别游戏引擎，请手动选择", "warning", 3000)

    def _on_drop(self, event) -> None:
        data = event.data
        if data.startswith("{") and data.endswith("}"):
            data = data[1:-1]
        if data.lower().endswith(".exe"):
            self.exe_path_var.set(data)
            self.game_root_var.set(str(Path(data).parent))
            self._update_status(f"已拖放: {Path(data).name}")

    def _save_config(self) -> None:
        # Get API keys from text box
        api_keys_text = self.api_keys_text.get("1.0", "end-1c").strip()
        api_keys = [k.strip() for k in api_keys_text.split("\n") if k.strip()]

        # Get current translator config
        translator_config = self._get_current_translator_config()

        data = {
            "exe_path": self.exe_path_var.get().strip(),
            "game_root": self.game_root_var.get().strip(),
            "game_engine": self.engine_var.get().strip(),
            "translator_type": self.translator_var.get().strip(),
            "translator_category": self.translator_category_var.get().strip(),
            "language": self.language_var.get().strip(),
            "batch_size": self.batch_var.get().strip(),
            "max_chars": self.max_chars_var.get().strip(),
            "workers": self.workers_var.get().strip(),
            "force_language": bool(self.force_lang_var.get()),
            # Translator-specific configs
            "translators": {
                "openai": {
                    "base_url": self.base_url_var.get().strip(),
                    "api_key": self.api_key_var.get().strip(),
                    "model": self.model_var.get().strip(),
                },
                "claude": {
                    "api_key": self.claude_api_key_var.get().strip(),
                    "model": self.claude_model_var.get().strip(),
                },
                "deepl": {
                    "api_key": self.deepl_api_key_var.get().strip(),
                    "formality": self.deepl_formality_var.get(),
                },
                "google": {
                    "api_key": self.google_api_key_var.get().strip(),
                    "project_id": self.google_project_id_var.get().strip(),
                },
            },
            # Other config items
            "glossary_path": self.glossary_path_var.get().strip(),
            "use_glossary": bool(self.use_glossary_var.get()),
            "use_postprocess": bool(self.use_postprocess_var.get()),
            "use_cache": bool(self.use_cache_var.get()),
            "performance_mode": bool(self.performance_mode_var.get()),
            "system_prompt": self.system_prompt_text.get("1.0", "end-1c"),
            "user_prompt": self.user_prompt_text.get("1.0", "end-1c"),
            "api_keys": api_keys,
        }
        safe_save_json(CONFIG_PATH, data)
        self._log("配置已保存")
        self._update_status("配置已保存")
        Toast.show(self, "配置已成功保存", "success", 2000)

    def _load_config(self) -> None:
        # Initialize translator config vars first
        self._init_translator_config_vars()

        if not CONFIG_PATH.exists():
            self._load_postprocess_rules()
            return

        data = safe_load_json(CONFIG_PATH, {})

        self.exe_path_var.set(data.get("exe_path", ""))
        self.game_root_var.set(data.get("game_root", ""))
        self.engine_var.set(data.get("game_engine", "renpy"))

        # Load translator category and type
        self.translator_category_var.set(data.get("translator_category", "AI 翻译"))
        self.translator_var.set(data.get("translator_type", "openai"))

        self.language_var.set(data.get("language", "trad_chinese"))
        self.batch_var.set(data.get("batch_size", "100"))
        self.max_chars_var.set(data.get("max_chars", "60000"))
        self.workers_var.set(data.get("workers", "5"))
        self.force_lang_var.set(data.get("force_language", True))

        # Load translator-specific configs
        translators_config = data.get("translators", {})

        # OpenAI config
        openai_config = translators_config.get("openai", {})
        self.base_url_var.set(openai_config.get("base_url", data.get("base_url", "https://openrouter.ai/api/v1")))
        self.model_var.set(openai_config.get("model", data.get("model", "tngtech/deepseek-r1t2-chimera:free")))
        self.api_key_var.set(openai_config.get("api_key", ""))

        # Claude config
        claude_config = translators_config.get("claude", {})
        self.claude_api_key_var.set(claude_config.get("api_key", ""))
        self.claude_model_var.set(claude_config.get("model", "claude-3-sonnet-20240229"))

        # DeepL config
        deepl_config = translators_config.get("deepl", {})
        self.deepl_api_key_var.set(deepl_config.get("api_key", ""))
        self.deepl_formality_var.set(deepl_config.get("formality", "default"))

        # Google config
        google_config = translators_config.get("google", {})
        self.google_api_key_var.set(google_config.get("api_key", ""))
        self.google_project_id_var.set(google_config.get("project_id", ""))

        # Load other config items
        self.glossary_path_var.set(data.get("glossary_path", ""))
        self.use_glossary_var.set(data.get("use_glossary", True))
        self.use_postprocess_var.set(data.get("use_postprocess", True))
        self.use_cache_var.set(data.get("use_cache", True))

        # Load performance mode setting
        performance_mode = data.get("performance_mode", False)
        self.performance_mode_var.set(performance_mode)
        AnimationEngine.set_enabled(not performance_mode)

        # Load prompts
        system_prompt = data.get("system_prompt", DEFAULT_SYSTEM_PROMPT)
        user_prompt = data.get("user_prompt", DEFAULT_USER_PROMPT)
        self.system_prompt_text.delete("1.0", "end")
        self.system_prompt_text.insert("1.0", system_prompt)
        self.user_prompt_text.delete("1.0", "end")
        self.user_prompt_text.insert("1.0", user_prompt)

        # Load API keys
        api_keys = data.get("api_keys", [])
        if api_keys:
            self.api_keys_text.delete("1.0", "end")
            self.api_keys_text.insert("1.0", "\n".join(api_keys))

        # Load glossary entries if path exists
        glossary_path = data.get("glossary_path", "")
        if glossary_path and Path(glossary_path).exists():
            self._load_glossary_entries(Path(glossary_path))

        # Load post-process rules
        self._load_postprocess_rules()

        # Rebuild translator config panel
        self._build_translator_config_panel()

        self._update_status("配置已加载")

    def _start(self) -> None:
        if self.worker and self.worker.is_alive():
            return
        game_root = self.game_root_var.get().strip()
        if not game_root:
            messagebox.showerror("错误", "请选择游戏根目录")
            return

        # Get translator config
        translator_type = self.translator_var.get().strip()
        translator_config = self._get_current_translator_config()

        # Validate API key
        api_key = translator_config.get("api_key", "")
        if not api_key:
            messagebox.showerror("错误", f"请输入 {translator_type} 的 API Key")
            return

        # Get API key(s) for AI translators
        api_keys_text = self.api_keys_text.get("1.0", "end-1c").strip()
        api_keys = [k.strip() for k in api_keys_text.split("\n") if k.strip()]

        # Use single API key if no pool configured
        if not api_keys:
            api_keys = [api_key]

        self.stop_event = threading.Event()
        self.start_btn.configure(state='disabled')
        self.stop_btn.configure(state='normal')
        self.progress_label.configure(text="正在初始化...")
        self._log("=" * 60)
        self._log(f"开始翻译任务... (使用 {translator_type} 翻译器)")
        self._update_status("翻译中...")

        # Get glossary path
        glossary_path = self.glossary_path_var.get().strip()
        glossary_path = Path(glossary_path) if glossary_path else None

        # Get custom prompts (only for AI translators)
        system_prompt = None
        user_prompt = None
        if translator_type in ["openai", "claude"]:
            system_prompt = self.system_prompt_text.get("1.0", "end-1c").strip()
            user_prompt = self.user_prompt_text.get("1.0", "end-1c").strip()
            if system_prompt == DEFAULT_SYSTEM_PROMPT:
                system_prompt = None
            if user_prompt == DEFAULT_USER_PROMPT:
                user_prompt = None

        def worker_fn() -> None:
            try:
                translator = Translator(
                    game_root=Path(game_root),
                    language=self.language_var.get().strip(),
                    base_url=translator_config.get("base_url", ""),
                    api_key=api_key,
                    model=translator_config.get("model", ""),
                    batch_size=int(self.batch_var.get() or "100"),
                    max_chars=int(self.max_chars_var.get() or "60000"),
                    workers=int(self.workers_var.get() or "5"),
                    cache_dir=CACHE_DIR,
                    work_root=WORK_DIR,
                    force_language=bool(self.force_lang_var.get()),
                    translator_type=translator_type,
                    game_engine=self.engine_var.get().strip(),
                    # New parameters
                    glossary_path=glossary_path,
                    use_glossary=bool(self.use_glossary_var.get()),
                    postprocess_path=POSTPROCESS_PATH if self.use_postprocess_var.get() else None,
                    use_postprocess=bool(self.use_postprocess_var.get()),
                    use_cache=bool(self.use_cache_var.get()),
                    system_prompt=system_prompt,
                    user_prompt=user_prompt,
                    api_keys=api_keys if len(api_keys) > 1 else None,
                    on_log=lambda m: self.queue.put(("log", m)),
                    on_progress=lambda d, t, c="": self.queue.put(("progress", d, t, c)),
                    stop_event=self.stop_event,
                )
                translator.run()
                self.queue.put(("done",))
            except Exception as exc:
                self.queue.put(("error", str(exc)))

        self.worker = threading.Thread(target=worker_fn, daemon=True)
        self.worker.start()

    def _stop(self) -> None:
        if self.stop_event:
            self.stop_event.set()
            self._log("正在停止...")
            self._update_status("正在停止...")

    def _poll_queue(self) -> None:
        # Batch process queue messages for better performance
        messages = []
        try:
            while len(messages) < 10:  # Process up to 10 messages per poll
                item = self.queue.get_nowait()
                messages.append(item)
        except queue.Empty:
            pass

        for item in messages:
            self._handle_queue_item(item)

        # Optimized poll interval: 50ms for responsive UI (20 updates/sec)
        self.after(50, self._poll_queue)

    def _handle_queue_item(self, item) -> None:
        kind = item[0]
        if kind == "log":
            self._log(item[1])
        elif kind == "progress":
            done, total = item[1], item[2]
            target = done / max(total, 1)
            self.progress.set_with_animation(target, duration=300)
            percent = int(target * 100)
            self.progress_label.configure(text=f"进度: {done} / {total} ({percent}%)")
        elif kind == "error":
            self._log(f"错误: {item[1]}")
            self.progress_label.configure(text="翻译失败")
            self._update_status("错误")
            self._reset_buttons()
            messagebox.showerror("错误", f"翻译失败:\n{item[1]}")
        elif kind == "done":
            self._log("=" * 60)
            self._log("翻译完成")
            self.progress_label.configure(text="翻译完成")
            self._update_status("完成")
            self.progress.pulse_complete()  # Celebration pulse animation
            self._reset_buttons()
            Toast.show(self, "翻译任务已完成", "success", 3000)

    def _reset_buttons(self) -> None:
        self.start_btn.configure(state='normal')
        self.stop_btn.configure(state='disabled')

    def _log(self, msg: str) -> None:
        self.log.insert(msg + '\n')

    def _update_status(self, msg: str) -> None:
        self.status_label.configure(text=f"状态: {msg}")


if __name__ == "__main__":
    app = App()
    app.mainloop()
