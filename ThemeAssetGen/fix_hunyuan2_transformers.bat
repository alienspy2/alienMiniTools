@echo off
chcp 65001 >nul
echo ========================================
echo  Hunyuan2 transformers/diffusers Fix
echo ========================================
echo.

call conda activate hunyuan2
if errorlevel 1 goto :error_conda

echo [1/2] Upgrading PyTorch to 2.6.0...
pip install torch==2.6.0 torchvision==0.21.0 torchaudio==2.6.0 --index-url https://download.pytorch.org/whl/cu124
if errorlevel 1 goto :error_pip

echo.
echo [2/2] Installing compatible transformers...
pip install "transformers>=4.46.0"
if errorlevel 1 goto :error_pip

echo.
echo ========================================
echo  Fix Complete!
echo ========================================
pause
exit /b 0

:error_conda
echo [ERROR] hunyuan2 conda environment not found
pause
exit /b 1

:error_pip
echo [ERROR] pip install failed
pause
exit /b 1
