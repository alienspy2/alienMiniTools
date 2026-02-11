#!/bin/bash
cd "$(dirname "$0")"
source .venv/bin/activate
export GTK_IM_MODULE=fcitx
export XMODIFIERS=@im=fcitx
python main.py
