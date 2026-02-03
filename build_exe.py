"""
RenPy Translator Build Script
Build Windows executable using PyInstaller

Usage:
    python build_exe.py          # Default single-file build
    python build_exe.py --dir    # Directory mode (faster startup)
    python build_exe.py --debug  # Debug mode (show console)
"""
import os
import subprocess
import sys
import shutil
from pathlib import Path


def get_customtkinter_path():
    """Get CustomTkinter package installation path."""
    try:
        import customtkinter
        return Path(customtkinter.__file__).parent
    except ImportError:
        return None


def get_tkinterdnd2_path():
    """Get tkinterdnd2 package installation path."""
    try:
        import tkinterdnd2
        return Path(tkinterdnd2.__file__).parent
    except ImportError:
        return None


def build_exe(onefile=True, debug=False):
    """
    Build the application executable.

    Args:
        onefile: True=single file mode, False=directory mode
        debug: True=show console window
    """
    print("=" * 60)
    print("RenPy Translator Build Tool")
    print("=" * 60)

    # Check and install PyInstaller
    try:
        import PyInstaller
        print(f"PyInstaller version: {PyInstaller.__version__}")
    except ImportError:
        print("Installing PyInstaller...")
        subprocess.check_call([sys.executable, "-m", "pip", "install", "pyinstaller"])
        import PyInstaller

    # Get project root directory
    project_root = Path(__file__).parent
    os.chdir(project_root)

    # Clean old build files
    for folder in ["build", "dist"]:
        folder_path = project_root / folder
        if folder_path.exists():
            print(f"Cleaning {folder} directory...")
            shutil.rmtree(folder_path)

    spec_file = project_root / "RenPyTranslator.spec"
    if spec_file.exists():
        spec_file.unlink()

    # Build command
    cmd = [
        sys.executable,
        "-m",
        "PyInstaller",
        "--name=RenPyTranslator",
        "--noconfirm",
        "--clean",
    ]

    # Single file or directory mode
    if onefile:
        cmd.append("--onefile")
        print("Build mode: single file (--onefile)")
    else:
        cmd.append("--onedir")
        print("Build mode: directory (--onedir)")

    # Window mode
    if debug:
        cmd.append("--console")
        print("Window mode: console (debug)")
    else:
        cmd.append("--windowed")
        print("Window mode: no console")

    # Add icon if exists
    icon_path = project_root / "icon.ico"
    if icon_path.exists():
        cmd.append(f"--icon={icon_path}")
        print(f"App icon: {icon_path}")

    # Add data files
    data_dirs = ["translators", "game_engines", "ui"]
    for data_dir in data_dirs:
        if (project_root / data_dir).exists():
            cmd.append(f"--add-data={data_dir};{data_dir}")

    # Add CustomTkinter resources (critical!)
    ctk_path = get_customtkinter_path()
    if ctk_path:
        cmd.append(f"--add-data={ctk_path};customtkinter")
        print(f"CustomTkinter path: {ctk_path}")

    # Add tkinterdnd2 resources
    dnd_path = get_tkinterdnd2_path()
    if dnd_path:
        cmd.append(f"--add-data={dnd_path};tkinterdnd2")
        print(f"tkinterdnd2 path: {dnd_path}")

    # Hidden imports - core modules
    hidden_imports = [
        # UI modules
        "customtkinter",
        "tkinter",
        "tkinter.ttk",
        "tkinter.filedialog",
        "tkinter.messagebox",
        # Project UI modules
        "ui",
        "ui.theme",
        "ui.easing",
        "ui.animations",
        "ui.components",
        "ui.shortcuts",
        # Translator modules
        "translators",
        "translators.base",
        "translators.api_manager",
        "translators.openai_translator",
        "translators.claude_translator",
        "translators.google_translator",
        "translators.deepl_translator",
        # Game engine modules
        "game_engines",
        "game_engines.base",
        "game_engines.detector",
        "game_engines.renpy_engine",
        "game_engines.rpgmaker_engine",
        "game_engines.unity_engine",
        # Core modules
        "plugin_manager",
        "config_schema",
        "config_utils",
        "glossary",
        "post_processor",
        "prompt_templates",
        "translator_core",
        "models",
        "renpy_utils",
        "unrpyc",
        # Third-party dependencies
        "PIL",
        "PIL._tkinter_finder",
        "httpx",
        "anyio",
        "certifi",
        "charset_normalizer",
        "idna",
        "sniffio",
        "h11",
        "httpcore",
    ]

    for imp in hidden_imports:
        cmd.append(f"--hidden-import={imp}")

    # Exclude unnecessary modules (reduce size)
    excludes = [
        "matplotlib",
        "numpy",
        "pandas",
        "scipy",
        "pytest",
        "setuptools",
    ]
    for exc in excludes:
        cmd.append(f"--exclude-module={exc}")

    # Add entry file
    cmd.append("app.py")

    print("\nStarting build...")
    print("-" * 60)

    try:
        subprocess.check_call(cmd)
    except subprocess.CalledProcessError as e:
        print(f"\nBuild failed: {e}")
        return False

    print("-" * 60)
    print("\nBuild complete!")

    # Show output location
    if onefile:
        exe_path = project_root / "dist" / "RenPyTranslator.exe"
    else:
        exe_path = project_root / "dist" / "RenPyTranslator" / "RenPyTranslator.exe"

    if exe_path.exists():
        size_mb = exe_path.stat().st_size / (1024 * 1024)
        print(f"Executable: {exe_path}")
        print(f"File size: {size_mb:.1f} MB")

    return True


def main():
    """Main function."""
    import argparse

    parser = argparse.ArgumentParser(description="RenPy Translator Build Tool")
    parser.add_argument("--dir", action="store_true", help="Directory mode (faster startup)")
    parser.add_argument("--debug", action="store_true", help="Debug mode (show console)")

    args = parser.parse_args()

    onefile = not args.dir
    success = build_exe(onefile=onefile, debug=args.debug)

    if success:
        print("\nTip: Run dist/RenPyTranslator.exe to start the program")
        if args.debug:
            print("     (Debug mode shows console window)")

    return 0 if success else 1


if __name__ == "__main__":
    sys.exit(main())
