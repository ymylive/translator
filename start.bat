@echo off
echo ========================================
echo RenPy 翻译程序 - 启动
echo ========================================
echo.

if exist "dist\RenPyTranslator.exe" (
    echo 启动已打包的程序...
    start "" "dist\RenPyTranslator.exe"
) else (
    echo 未找到打包程序，使用Python启动...
    python app.py
)
