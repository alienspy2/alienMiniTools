@echo off
:: Activate Conda environment 'tunnel'
:: Assuming Conda is in path or standard location. If not, user might need to adjust.
:: Trying generic activation hook
call conda activate tunnel
if %errorlevel% neq 0 (
    echo [ERROR] Failed to activate conda environment 'tunnel'.
    echo Make sure you have created it: conda create -n tunnel python=3.12
    pause
    exit /b
)

python generate_client_key.py
