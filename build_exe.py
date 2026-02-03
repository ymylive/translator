"""
RenPy Translator 打包脚本
使用 PyInstaller 打包为 Windows 可执行文件

用法:
    python build_exe.py          # 默认打包 (单文件)
    python build_exe.py --dir    # 目录模式 (启动更快)
    python build_exe.py --debug  # 调试模式 (显示控制台)
"""
import os
import subprocess
import sys
import shutil
from pathlib import Path


def get_customtkinter_path():
    """获取 CustomTkinter 包的安装路径"""
    try:
        import customtkinter
        return Path(customtkinter.__file__).parent
    except ImportError:
        return None


def get_tkinterdnd2_path():
    """获取 tkinterdnd2 包的安装路径"""
    try:
        import tkinterdnd2
        return Path(tkinterdnd2.__file__).parent
    except ImportError:
        return None


def build_exe(onefile=True, debug=False):
    """
    打包应用程序

    Args:
        onefile: True=单文件模式, False=目录模式
        debug: True=显示控制台窗口
    """
    print("=" * 60)
    print("RenPy Translator 打包工具")
    print("=" * 60)

    # 检查并安装 PyInstaller
    try:
        import PyInstaller
        print(f"PyInstaller 版本: {PyInstaller.__version__}")
    except ImportError:
        print("正在安装 PyInstaller...")
        subprocess.check_call([sys.executable, "-m", "pip", "install", "pyinstaller"])
        import PyInstaller

    # 获取项目根目录
    project_root = Path(__file__).parent
    os.chdir(project_root)

    # 清理旧的构建文件
    for folder in ["build", "dist"]:
        folder_path = project_root / folder
        if folder_path.exists():
            print(f"清理 {folder} 目录...")
            shutil.rmtree(folder_path)

    spec_file = project_root / "RenPyTranslator.spec"
    if spec_file.exists():
        spec_file.unlink()

    # 构建命令
    cmd = [
        sys.executable,
        "-m",
        "PyInstaller",
        "--name=RenPyTranslator",
        "--noconfirm",
        "--clean",
    ]

    # 单文件或目录模式
    if onefile:
        cmd.append("--onefile")
        print("打包模式: 单文件 (--onefile)")
    else:
        cmd.append("--onedir")
        print("打包模式: 目录 (--onedir)")

    # 窗口模式
    if debug:
        cmd.append("--console")
        print("窗口模式: 控制台 (调试)")
    else:
        cmd.append("--windowed")
        print("窗口模式: 无控制台")

    # 添加图标 (如果存在)
    icon_path = project_root / "icon.ico"
    if icon_path.exists():
        cmd.append(f"--icon={icon_path}")
        print(f"应用图标: {icon_path}")

    # 添加数据文件
    data_dirs = ["translators", "game_engines", "ui"]
    for data_dir in data_dirs:
        if (project_root / data_dir).exists():
            cmd.append(f"--add-data={data_dir};{data_dir}")

    # 添加 CustomTkinter 资源文件 (关键!)
    ctk_path = get_customtkinter_path()
    if ctk_path:
        cmd.append(f"--add-data={ctk_path};customtkinter")
        print(f"CustomTkinter 路径: {ctk_path}")

    # 添加 tkinterdnd2 资源文件
    dnd_path = get_tkinterdnd2_path()
    if dnd_path:
        cmd.append(f"--add-data={dnd_path};tkinterdnd2")
        print(f"tkinterdnd2 路径: {dnd_path}")

    # 隐藏导入 - 核心模块
    hidden_imports = [
        # UI 模块
        "customtkinter",
        "tkinter",
        "tkinter.ttk",
        "tkinter.filedialog",
        "tkinter.messagebox",
        # 项目 UI 模块
        "ui",
        "ui.theme",
        "ui.easing",
        "ui.animations",
        "ui.components",
        "ui.shortcuts",
        # 翻译器模块
        "translators",
        "translators.base",
        "translators.api_manager",
        "translators.openai_translator",
        "translators.claude_translator",
        "translators.google_translator",
        "translators.deepl_translator",
        # 游戏引擎模块
        "game_engines",
        "game_engines.base",
        "game_engines.detector",
        "game_engines.renpy_engine",
        "game_engines.rpgmaker_engine",
        "game_engines.unity_engine",
        # 核心模块
        "plugin_manager",
        "config_schema",
        "config_utils",
        "glossary",
        "post_processor",
        "prompt_templates",
        "translator_core",
        "models",
        "renpy_utils",
        # 第三方依赖
        "PIL",
        "PIL._tkinter_finder",
        "openai",
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

    # 排除不需要的模块 (减小体积)
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

    # 添加入口文件
    cmd.append("app.py")

    print("\n开始打包...")
    print("-" * 60)

    try:
        subprocess.check_call(cmd)
    except subprocess.CalledProcessError as e:
        print(f"\n打包失败: {e}")
        return False

    print("-" * 60)
    print("\n打包完成!")

    # 显示输出位置
    if onefile:
        exe_path = project_root / "dist" / "RenPyTranslator.exe"
    else:
        exe_path = project_root / "dist" / "RenPyTranslator" / "RenPyTranslator.exe"

    if exe_path.exists():
        size_mb = exe_path.stat().st_size / (1024 * 1024)
        print(f"可执行文件: {exe_path}")
        print(f"文件大小: {size_mb:.1f} MB")

    return True


def main():
    """主函数"""
    import argparse

    parser = argparse.ArgumentParser(description="RenPy Translator 打包工具")
    parser.add_argument("--dir", action="store_true", help="目录模式 (启动更快)")
    parser.add_argument("--debug", action="store_true", help="调试模式 (显示控制台)")

    args = parser.parse_args()

    onefile = not args.dir
    success = build_exe(onefile=onefile, debug=args.debug)

    if success:
        print("\n提示: 运行 dist/RenPyTranslator.exe 启动程序")
        if args.debug:
            print("      (调试模式下会显示控制台窗口)")

    return 0 if success else 1


if __name__ == "__main__":
    sys.exit(main())
